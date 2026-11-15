using System.Buffers.Binary;

namespace Usa.Game.Services.Voice;

/// <summary>Canonical 44-byte-header RIFF/WAVE, PCM 16-bit little-endian, mono. Pure.</summary>
public static class WavEncoder
{
    public const int HeaderBytes = 44;
    private const short PcmFormat = 1;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BytesPerSample = BitsPerSample / 8;

    /// <param name="samples">Mono samples in [-1, 1]; values outside are clipped, NaN becomes silence.</param>
    public static byte[] EncodePcm16Mono(ReadOnlySpan<float> samples, int sampleRateHz)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRateHz, 1);
        var dataBytes = samples.Length * BytesPerSample;
        var wav = new byte[HeaderBytes + dataBytes];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], PcmFormat);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRateHz);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRateHz * Channels * BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], Channels * BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        var data = span[HeaderBytes..];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * BytesPerSample)..], ToPcm16(samples[i]));
        }
        return wav;
    }

    public static short ToPcm16(float sample)
    {
        if (float.IsNaN(sample)) return 0;
        var clipped = Math.Clamp(sample, -1f, 1f);
        return (short)MathF.Round(clipped * short.MaxValue);
    }
}
