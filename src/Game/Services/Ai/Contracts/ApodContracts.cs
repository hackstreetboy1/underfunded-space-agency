namespace Usa.Game.Services.Ai.Contracts;

/// <summary>
/// <c>GET /v1/apod</c> (<c>app.schemas.apod.ApodResponse</c>): the proxy's cached Astronomy Picture of the Day.
/// <see cref="ImagePath"/> is the proxy path of the cached image; <see cref="Url"/> is NASA's own URL, for reference only
/// (the game never loads it). <see cref="Copyright"/> names the holder of a non-public-domain image.
/// </summary>
public sealed record ApodResponse(string Date, string Title, string MediaType, string Url, string ImagePath)
{
    public string Explanation { get; init; } = "";

    public string? Copyright { get; init; }

    /// <summary>True when today's picture was unusable (video day, error) and an earlier cached day is served.</summary>
    public bool IsFallback { get; init; }
}
