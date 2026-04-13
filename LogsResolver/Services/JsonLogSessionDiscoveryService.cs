using LogsResolver.Models;

namespace LogsResolver.Services;

public sealed class JsonLogSessionDiscoveryService
{
    public LogSessionDescriptor Discover(string selectedPath)
    {
        var root = ResolveCharactersRoot(selectedPath);
        var session = new LogSessionDescriptor
        {
            RootPath = root,
            GlobalJsonLinesPath = ExistingFile(Path.Combine(root, "Characters.jsonl")),
            GlobalTextLogPath = ExistingFile(Path.Combine(root, "Characters.log"))
        };

        var personRoot = Path.Combine(root, "Person");
        if (!Directory.Exists(personRoot))
        {
            return session;
        }

        foreach (var personDirectory in Directory.EnumerateDirectories(personRoot).OrderBy(p => p))
        {
            session.PersonFolders.Add(personDirectory);
            var personId = Guid.TryParse(Path.GetFileName(personDirectory), out var parsedPersonId)
                ? parsedPersonId
                : (Guid?)null;

            foreach (var file in Directory.EnumerateFiles(personDirectory, "*.jsonl", SearchOption.TopDirectoryOnly).OrderBy(p => p))
            {
                session.ScopedJsonLinesFiles.Add(new JsonLogFileDescriptor
                {
                    FilePath = file,
                    SourceKind = LogSourceKind.Scoped,
                    PersonId = personId,
                    Subsystem = Path.GetFileNameWithoutExtension(file)
                });
            }

            foreach (var file in Directory.EnumerateFiles(personDirectory, "*.log", SearchOption.TopDirectoryOnly).OrderBy(p => p))
            {
                session.ScopedTextLogFiles.Add(file);
            }
        }

        return session;
    }

    private static string ResolveCharactersRoot(string selectedPath)
    {
        var fullPath = Path.GetFullPath(selectedPath);
        if (LooksLikeCharactersRoot(fullPath))
        {
            return fullPath;
        }

        var child = Path.Combine(fullPath, "Characters");
        if (LooksLikeCharactersRoot(child))
        {
            return child;
        }

        var logsCharacters = Path.Combine(fullPath, "logs", "Characters");
        if (LooksLikeCharactersRoot(logsCharacters))
        {
            return logsCharacters;
        }

        var parent = Directory.GetParent(fullPath);
        if (parent is not null && string.Equals(Path.GetFileName(fullPath), "Person", StringComparison.OrdinalIgnoreCase))
        {
            return parent.FullName;
        }

        return fullPath;
    }

    private static bool LooksLikeCharactersRoot(string path)
        => Directory.Exists(path)
           && (File.Exists(Path.Combine(path, "Characters.jsonl"))
               || File.Exists(Path.Combine(path, "Characters.log"))
               || Directory.Exists(Path.Combine(path, "Person")));

    private static string? ExistingFile(string path) => File.Exists(path) ? path : null;
}
