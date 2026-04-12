using System.Text;

namespace LogsResolver.Services;

public sealed class RawFileService
{
    public async Task<string> ReadTextAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return $"File not found: {filePath}";
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
