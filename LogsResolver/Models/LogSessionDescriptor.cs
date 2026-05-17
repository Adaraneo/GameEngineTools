namespace LogsResolver.Models;

public sealed class LogSessionDescriptor
{
    public string RootPath { get; init; }

    public string? GlobalJsonLinesPath { get; init; }

    public string? GlobalTextLogPath { get; init; }

    public List<string> PersonFolders { get; } = new();

    public List<JsonLogFileDescriptor> ScopedJsonLinesFiles { get; } = new();

    public List<string> ScopedTextLogFiles { get; } = new();

    public IEnumerable<JsonLogFileDescriptor> AllJsonLinesFiles
    {
        get
        {
            if (GlobalJsonLinesPath is not null)
            {
                yield return new JsonLogFileDescriptor
                {
                    FilePath = GlobalJsonLinesPath,
                    SourceKind = LogSourceKind.Global
                };
            }

            foreach (var file in ScopedJsonLinesFiles)
            {
                yield return file;
            }
        }
    }

    public IEnumerable<string> AllTextLogFiles
    {
        get
        {
            if (GlobalTextLogPath is not null)
            {
                yield return GlobalTextLogPath;
            }

            foreach (var file in ScopedTextLogFiles)
            {
                yield return file;
            }
        }
    }
}
