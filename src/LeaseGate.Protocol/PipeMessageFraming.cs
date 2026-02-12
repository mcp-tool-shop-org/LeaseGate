using System.Text;

namespace LeaseGate.Protocol;

public static class PipeMessageFraming
{
    private const int MaxPayloadBytes = 16 * 1024 * 1024; // 16 MB
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(payload);
        var lengthPrefix = BitConverter.GetBytes(bytes.Length);

        await stream.WriteAsync(lengthPrefix, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await ReadExactAsync(stream, lengthBytes, cancellationToken);

        var payloadLength = BitConverter.ToInt32(lengthBytes, 0);
        if (payloadLength <= 0)
        {
            throw new InvalidOperationException("Invalid payload length.");
        }

        if (payloadLength > MaxPayloadBytes)
        {
            throw new InvalidOperationException($"Payload length {payloadLength} exceeds maximum of {MaxPayloadBytes} bytes.");
        }

        var payloadBytes = new byte[payloadLength];
        await ReadExactAsync(stream, payloadBytes, cancellationToken);
        var json = Encoding.UTF8.GetString(payloadBytes);

        return ProtocolJson.Deserialize<T>(json);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected EOF while reading framed message.");
            }

            totalRead += read;
        }
    }
}
