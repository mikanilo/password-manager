using System.Text;
using System.Text.Json;

namespace PasswordManager.NativeHost;

/// <summary>
/// Chrome's native messaging protocol is simple but unforgiving: each message
/// is a 4-byte little-endian length prefix followed by that many bytes of
/// UTF-8 JSON, written directly to stdin/stdout as raw bytes.
///
/// Critically, stdout must ONLY ever contain this protocol -- no stray
/// Console.WriteLine calls, no exception text, nothing else. Anything else
/// written to stdout corrupts the stream and Chrome will kill the connection.
/// That's why this class uses the raw Console.OpenStandardInput/Output
/// streams directly instead of Console.In/Console.Out, which can apply
/// text-mode newline translation on Windows and silently corrupt binary data.
/// </summary>
public static class NativeMessaging
{
    private const int MaxMessageSizeBytes = 1024 * 1024; // 1 MB, Chrome's own limit

    public static async Task<JsonDocument?> ReadMessageAsync(Stream input)
    {
        var lengthBytes = new byte[4];
        var bytesRead = await ReadExactAsync(input, lengthBytes, 4);
        if (bytesRead < 4)
        {
            // Stdin closed -- Chrome disconnected the port. This is the
            // normal way the host process is told to shut down.
            return null;
        }

        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
        if (messageLength <= 0 || messageLength > MaxMessageSizeBytes)
        {
            throw new InvalidDataException($"Invalid native message length: {messageLength}");
        }

        var messageBytes = new byte[messageLength];
        await ReadExactAsync(input, messageBytes, messageLength);

        return JsonDocument.Parse(messageBytes);
    }

    public static async Task WriteMessageAsync(Stream output, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var lengthBytes = BitConverter.GetBytes(jsonBytes.Length);

        await output.WriteAsync(lengthBytes);
        await output.WriteAsync(jsonBytes);
        await output.FlushAsync();
    }

    private static async Task<int> ReadExactAsync(Stream input, byte[] buffer, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await input.ReadAsync(buffer.AsMemory(totalRead, count - totalRead));
            if (read == 0)
            {
                break; // stream closed
            }
            totalRead += read;
        }
        return totalRead;
    }
}
