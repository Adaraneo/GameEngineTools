// WorkCatalogLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using GameEngineTools.Constants;
    using GameEngineTools.World.Objects;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Loads <c>materials.json</c> and <c>objecttypes.json</c>. Each file is read from the given directory, else from <c>SourceFiles\World</c> next to the program, else from the copy built into the
    /// library, so a designer can add materials and tools without touching code. Comments and trailing commas are allowed in the files.
    /// </summary>
    public static class WorkCatalogLoader
    {
        private const string MaterialsFile = "materials.json";
        private const string ObjectTypesFile = "objecttypes.json";
        private const string TasksFile = "tasks.json";

        private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>Loads and checks the catalog.</summary>
        /// <param name="directory">A folder holding the files, or null for the program folder SourceFiles\World and then the built-in copy.</param>
        /// <exception cref="FormatException">A file is malformed or the catalog fails its checks; the message names the file and the entry.</exception>
        public static WorkCatalog Load(string? directory = null)
            => Parse(ReadFile(MaterialsFile, directory), ReadFile(ObjectTypesFile, directory), ReadFile(TasksFile, directory));

        /// <summary>Parses the text of the two files; public so tests and tools can validate a file before shipping it.</summary>
        public static WorkCatalog Parse(string materialsJson, string objectTypesJson, string? tasksJson = null)
        {
            var materials = new List<MaterialDefinition>();
            foreach (var m in Entries(materialsJson, MaterialsFile, "materials"))
            {
                var id = Text(m, "id", MaterialsFile);
                materials.Add(new MaterialDefinition(
                    id,
                    OptionalText(m, "name") ?? id,
                    Enum.TryParse<MaterialClass>(OptionalText(m, "class") ?? "Other", ignoreCase: true, out var cls) ? cls : throw Bad(MaterialsFile, id, "unknown class"),
                    Number(m, "densityKgM3", MaterialsFile, id),
                    OptionalText(m, "source")));
            }

            var types = new List<ObjectTypeDefinition>();
            foreach (var t in Entries(objectTypesJson, ObjectTypesFile, "types"))
            {
                var id = Text(t, "id", ObjectTypesFile);
                var parts = new List<ObjectPart>();
                if (t.TryGetProperty("parts", out var partsElement))
                    foreach (var p in partsElement.EnumerateArray())
                        parts.Add(new ObjectPart(Text(p, "material", ObjectTypesFile), Number(p, "volumeCm3", ObjectTypesFile, id)));

                var classes = new Dictionary<string, double>(StringComparer.Ordinal);
                if (t.TryGetProperty("toolClasses", out var classesElement))
                    foreach (var c in classesElement.EnumerateObject())
                        classes[c.Name] = c.Value.GetDouble();

                types.Add(new ObjectTypeDefinition(
                    id,
                    OptionalText(t, "name") ?? id,
                    Enum.TryParse<WorldObjectCategory>(OptionalText(t, "category") ?? "Tool", ignoreCase: true, out var cat) ? cat : throw Bad(ObjectTypesFile, id, "unknown category"),
                    !t.TryGetProperty("pickable", out var pick) || pick.GetBoolean(),
                    parts,
                    classes,
                    t.TryGetProperty("durabilityHours", out var dur) && dur.ValueKind == JsonValueKind.Number ? dur.GetDouble() : null,
                    OptionalText(t, "source"),
                    t.TryGetProperty("weightGrams", out var weight) && weight.ValueKind == JsonValueKind.Number ? weight.GetInt32() : null,
                    t.TryGetProperty("affordances", out var affordancesElement) ? affordancesElement.EnumerateObject().ToDictionary(a => a.Name, a => a.Value.GetDouble(), StringComparer.Ordinal) : null,
                    t.TryGetProperty("spoilageHours", out var spoil) && spoil.ValueKind == JsonValueKind.Number ? spoil.GetDouble() : null,
                    t.TryGetProperty("referencePrice", out var price) && price.ValueKind == JsonValueKind.Number ? price.GetDouble() : null));
            }

            var tasks = new List<TaskDefinition>();
            foreach (var t in tasksJson is null ? new List<JsonElement>() : Entries(tasksJson, TasksFile, "tasks"))
            {
                var id = Text(t, "id", TasksFile);
                var inputs = new List<TaskItem>();
                if (t.TryGetProperty("inputs", out var inputsElement))
                    foreach (var i in inputsElement.EnumerateArray())
                        inputs.Add(new TaskItem(Text(i, "item", TasksFile), (int)Number(i, "qty", TasksFile, id)));

                TaskItem? output = null;
                if (t.TryGetProperty("output", out var outputElement))
                    output = new TaskItem(Text(outputElement, "item", TasksFile), (int)Number(outputElement, "qty", TasksFile, id));

                tasks.Add(new TaskDefinition(
                    id,
                    OptionalText(t, "name") ?? id,
                    Enum.TryParse<TaskKind>(Text(t, "kind", TasksFile), ignoreCase: true, out var kind) ? kind : throw Bad(TasksFile, id, "unknown kind"),
                    inputs,
                    output,
                    OptionalText(t, "outputName"),
                    t.TryGetProperty("durationMinutes", out var minutes) && minutes.ValueKind == JsonValueKind.Number ? minutes.GetInt32() : 0,
                    OptionalText(t, "location"),
                    OptionalText(t, "action"),
                    OptionalText(t, "tool"),
                    OptionalText(t, "source"),
                    t.TryGetProperty("requires", out var requiresElement) ? requiresElement.EnumerateArray().Select(r => r.GetString() ?? string.Empty).ToList() : null));
            }

            return new WorkCatalog(materials, types, tasks);
        }

        private static List<JsonElement> Entries(string json, string file, string arrayName)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(json, DocumentOptions); }
            catch (JsonException e) { throw new FormatException($"{file}: {e.Message}", e); }

            using (document)
            {
                if (!document.RootElement.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
                    throw new FormatException($"{file}: the top level must be an object with an array '{arrayName}'.");

                var entries = new List<JsonElement>();
                foreach (var entry in array.EnumerateArray())
                    entries.Add(entry.Clone());
                return entries;
            }
        }

        private static string Text(JsonElement e, string name, string file)
            => OptionalText(e, name) ?? throw new FormatException($"{file}: an entry has no '{name}'.");

        private static string? OptionalText(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static double Number(JsonElement e, string name, string file, string id)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : throw Bad(file, id, $"'{name}' is missing or not a number");

        private static FormatException Bad(string file, string id, string what) => new($"{file}: '{id}': {what}.");

        private static string ReadFile(string file, string? directory)
        {
            if (directory is not null && File.Exists(Path.Combine(directory, file)))
                return File.ReadAllText(Path.Combine(directory, file), Encoding.UTF8);

            var disk = file == MaterialsFile ? FileSystemConstant.SourceFilePath.WorkMaterials
                : file == ObjectTypesFile ? FileSystemConstant.SourceFilePath.WorkObjectTypes
                : FileSystemConstant.SourceFilePath.WorkTasks;
            if (directory is null && File.Exists(disk))
                return File.ReadAllText(disk, Encoding.UTF8);

            var name = "GameEngineTools.Work." + file;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new FileNotFoundException($"Embedded resource '{name}' not found; add World\\Data\\Work\\{file} to GameEngineTools.csproj.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
    }
}
