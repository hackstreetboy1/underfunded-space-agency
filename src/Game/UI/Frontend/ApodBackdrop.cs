using Godot;
using Usa.Game.Content;
using Usa.Game.Flow.Screens;
using Usa.Game.Services.Ai;
using Usa.Simulation.Commands;

namespace Usa.Game.UI.Frontend;

/// <summary>
/// The loading screens' Astronomy Picture of the Day as a texture and caption, decoded once per launch from
/// <see cref="ApodService.Current"/>. The decode runs on a worker thread; <see cref="TryGet"/> is polled from the main
/// thread and never blocks. Without guardrails (the file failed to load) the title is never shown.
/// </summary>
public sealed class ApodBackdrop(ApodService service, AiGuardrails? guardrails, StringTable strings)
{
    private Task<Image?>? _decode;
    private Texture2D? _texture;
    private ApodCaption? _caption;
    private bool _failed;

    /// <summary>True while a picture may still arrive: the fetch or the decode is still running.</summary>
    public bool Pending => _texture is null && !_failed && !(service.Settled && service.Current is null);

    public bool TryGet(out Texture2D texture, out ApodCaption caption)
    {
        if (_texture is null && !_failed) Advance();
        texture = _texture!;
        caption = _caption!;
        return _texture is not null;
    }

    private void Advance()
    {
        if (service.Current is not { } picture) return;
        _decode ??= Task.Run(() => Decode(picture.Image));
        if (!_decode.IsCompleted) return;

        var image = _decode.IsCompletedSuccessfully ? _decode.Result : null;
        if (image is null || image.IsEmpty())
        {
            _failed = true;
            return;
        }

        var info = picture.Info;
        _caption = ApodCaption.Create(info.Date, info.Title, info.Copyright, guardrails, strings);
        _texture = ImageTexture.CreateFromImage(image);
    }

    private static Image? Decode(byte[] bytes)
    {
        var image = new Image();
        var isPng = bytes.Length > 1 && bytes[0] == 0x89;
        var error = isPng ? image.LoadPngFromBuffer(bytes) : image.LoadJpgFromBuffer(bytes);
        return error == Error.Ok ? image : null;
    }
}
