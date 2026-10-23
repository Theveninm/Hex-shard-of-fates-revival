using System.Buffers.Binary;

namespace HexRevival.Server;

/// <summary>
/// Wire format shared with the game client (see docs/MULTIPLAYER.md):
/// [int32 big-endian payload length][byte kind][payload].
/// Kind 1 = JSON control message (UTF-8), kind 2 = game data relayed as-is to the match opponent.
/// </summary>
public static class Frame
{
    public const byte Json = 1;
    public const byte Relay = 2;

    /// <summary>Control messages are small; game packets can be large (a full board resync).</summary>
    public const int MaxJson = 8 * 1024;
    public const int MaxRelay = 1024 * 1024;

    public static byte[] Build(byte kind, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[5 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        frame[4] = kind;
        payload.CopyTo(frame.AsSpan(5));
        return frame;
    }

    /// <summary>Reads one frame; null when the peer closed the connection cleanly between two frames.</summary>
    public static async Task<(byte Kind, byte[] Payload)?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var head = new byte[5];
        int got = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (got == 0) return null;
        if (got < head.Length) throw new EndOfStreamException();

        int length = BinaryPrimitives.ReadInt32BigEndian(head);
        byte kind = head[4];
        int max = kind switch { Json => MaxJson, Relay => MaxRelay, _ => throw new InvalidDataException($"unknown frame kind {kind}") };
        if (length < 0 || length > max) throw new InvalidDataException($"frame too large ({length} bytes)");

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct);
        return (kind, payload);
    }
}
