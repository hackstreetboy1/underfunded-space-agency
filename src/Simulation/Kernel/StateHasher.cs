using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Usa.Simulation.Kernel;

/// <summary>Anything whose state takes part in the determinism hash.</summary>
public interface IHashContributor
{
    /// <summary>Writes state in a fixed, canonical order (sorted ids, no dictionary iteration order).</summary>
    void WriteHash(StateHashWriter writer);
}

/// <summary>Streams primitive values into a SHA-256 hash using exact bit patterns.</summary>
/// <remarks>
/// Values are buffered and fed to the hash in large blocks; the digest depends only on the byte stream,
/// so buffering changes cost, not results.
/// </remarks>
public sealed class StateHashWriter : IDisposable
{
    private const int BufferSize = 4096;

    // A lone surrogate throws instead of encoding as U+FFFD, so two different strings never share a byte stream.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _used;

    public void Write(double value) => Write(BitConverter.DoubleToInt64Bits(value));

    public void Write(long value)
    {
        if (_used + sizeof(long) > BufferSize) Flush();
        BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_used), value);
        _used += sizeof(long);
    }

    public void Write(bool value) => Write(value ? 1L : 0L);

    /// <exception cref="EncoderFallbackException"><paramref name="value"/> is not well-formed UTF-16 (a lone surrogate).</exception>
    public void Write(string value)
    {
        Write((long)value.Length);
        var maxBytes = StrictUtf8.GetMaxByteCount(value.Length);
        if (maxBytes > BufferSize)
        {
            Flush();
            _hash.AppendData(StrictUtf8.GetBytes(value));
            return;
        }
        if (_used + maxBytes > BufferSize) Flush();
        _used += StrictUtf8.GetBytes(value, _buffer.AsSpan(_used));
    }

    public string Finish()
    {
        Flush();
        return Convert.ToHexString(_hash.GetHashAndReset());
    }

    public void Dispose() => _hash.Dispose();

    private void Flush()
    {
        if (_used == 0) return;
        _hash.AppendData(_buffer, 0, _used);
        _used = 0;
    }
}

public static class StateHasher
{
    public static string Compute(IEnumerable<IHashContributor> contributors)
    {
        using var writer = new StateHashWriter();
        foreach (var contributor in contributors) contributor.WriteHash(writer);
        return writer.Finish();
    }
}
