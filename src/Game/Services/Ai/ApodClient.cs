using System.Net;
using System.Text.Json;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Services.Ai;

/// <summary>The proxy's Astronomy Picture of the Day with its cached image bytes (JPEG or PNG).</summary>
public sealed record ApodPicture(ApodResponse Info, byte[] Image);

/// <summary>How one <see cref="ApodClient.FetchAsync"/> ended.</summary>
public enum ApodFetchStatus
{
    Ok,

    /// <summary>The proxy answered but has no picture (404 <c>apod_unavailable</c>) or sent something unusable: final.</summary>
    Unavailable,

    /// <summary>The proxy did not answer (not started yet, timeout, 5xx): worth retrying later.</summary>
    Unreachable,
}

public sealed record ApodFetch(ApodFetchStatus Status, ApodPicture? Picture = null);

/// <summary>
/// Reads <c>GET /v1/apod</c> and then the cached image it names from the local proxy. The proxy owns the NASA budget
/// (one call per UTC day) and the image cache, so this client never contacts NASA and may be called freely. Redirects
/// are not followed and the image must come from the proxy's own <c>/v1/apod/image</c> path. Failures are values.
/// </summary>
public sealed class ApodClient : IDisposable
{
    public const string MetadataPath = "v1/apod";
    public const string ImagePathPrefix = "/v1/apod/image";

    /// <summary>The proxy caps the image at 5 MiB; anything larger is not its answer.</summary>
    public const int MaxImageBytes = 6 * 1024 * 1024;

    // The first request of a UTC day waits for the proxy's single NASA call and the image download.
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly Func<Uri> _baseUrl;

    /// <param name="baseUrl">The proxy URL, read per call so a settings change applies to the next fetch.</param>
    /// <param name="handler">Test seam; production uses the gateway's proxy-less loopback handler.</param>
    public ApodClient(Func<Uri> baseUrl, HttpMessageHandler? handler = null)
    {
        _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        _http = handler is null ? new HttpClient(AiGateway.CreateLoopbackHandler()) : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<ApodFetch> FetchAsync(CancellationToken cancellationToken)
    {
        var baseUrl = _baseUrl();
        ApodResponse? info;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(MetadataTimeout);
            using var response = await _http.GetAsync(new Uri(baseUrl, MetadataPath), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return new ApodFetch(ApodFetchStatus.Unavailable);
            if (!response.IsSuccessStatusCode) return new ApodFetch(Classify(response.StatusCode));
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            info = await JsonSerializer.DeserializeAsync(body, AiJsonContext.Default.ApodResponse, timeout.Token).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return new ApodFetch(ApodFetchStatus.Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ApodFetch(ApodFetchStatus.Unreachable);
        }

        if (info is null || ImageUri(baseUrl, info.ImagePath) is not { } imageUri) return new ApodFetch(ApodFetchStatus.Unavailable);
        var image = await FetchImageAsync(imageUri, cancellationToken).ConfigureAwait(false);
        return image.Bytes is null ? new ApodFetch(image.Status) : new ApodFetch(ApodFetchStatus.Ok, new ApodPicture(info, image.Bytes));
    }

    public void Dispose() => _http.Dispose();

    /// <summary>The image URL on the proxy itself; null for any path outside <see cref="ImagePathPrefix"/>.</summary>
    internal static Uri? ImageUri(Uri baseUrl, string imagePath)
    {
        if (!imagePath.StartsWith(ImagePathPrefix, StringComparison.Ordinal)) return null;
        var rest = imagePath[ImagePathPrefix.Length..];
        if (rest.Length > 0 && rest[0] != '?') return null;
        var uri = new Uri(baseUrl, imagePath.TrimStart('/'));
        return Uri.Compare(uri, baseUrl, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0 ? uri : null;
    }

    private static ApodFetchStatus Classify(HttpStatusCode status) =>
        (int)status >= 500 || status == HttpStatusCode.TooManyRequests ? ApodFetchStatus.Unreachable : ApodFetchStatus.Unavailable;

    private async Task<(ApodFetchStatus Status, byte[]? Bytes)> FetchImageAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ImageTimeout);
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (Classify(response.StatusCode), null);
            if (response.Content.Headers.ContentLength is > MaxImageBytes) return (ApodFetchStatus.Unavailable, null);

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[64 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxImageBytes) return (ApodFetchStatus.Unavailable, null);
                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.ToArray();
            return IsSupportedImage(bytes) ? (ApodFetchStatus.Ok, bytes) : (ApodFetchStatus.Unavailable, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return (ApodFetchStatus.Unreachable, null);
        }
    }

    /// <summary>JPEG or PNG by their leading bytes: the formats the loading screen can decode.</summary>
    public static bool IsSupportedImage(ReadOnlySpan<byte> data) =>
        data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) || data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
}
