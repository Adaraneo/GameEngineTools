using System.Text;

namespace LogsResolver.Services;

public sealed class RawFileService
{
    private const int PreviewCharLimit = 2_000_000;

    public async Task<string> ReadTextAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return $"File not found: {filePath}";
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[PreviewCharLimit];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        var text = new string(buffer, 0, read);
        return stream.Position < stream.Length
            ? text + Environment.NewLine + Environment.NewLine + $"--- Preview truncated at {PreviewCharLimit:N0} characters. Open the file externally for full content. ---"
            : text;
    }
}
