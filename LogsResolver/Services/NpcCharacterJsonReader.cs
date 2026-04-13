using System.Text.Json;
using LogsResolver.Models;

namespace LogsResolver.Services;

public sealed class NpcCharacterJsonReader
{
    public Task<IReadOnlyList<NpcCharacterDescriptor>> LoadAsync(string folder)
        => Task.Run(() => Load(folder));

    private static IReadOnlyList<NpcCharacterDescriptor> Load(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<NpcCharacterDescriptor>();
        }

        var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly).ToList();
        if (files.Count == 0)
        {
            files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).ToList();
        }

        var characters = new List<NpcCharacterDescriptor>(files.Count);
        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (TryRead(file, out var character))
            {
                characters.Add(character);
            }
        }

        return characters
            .GroupBy(c => c.PersonId)
            .Select(g => g.First())
            .OrderBy(c => c.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.PersonId)
            .ToList();
    }

    private static bool TryRead(string file, out NpcCharacterDescriptor character)
    {
        character = null!;
        try
        {
            using var stream = File.OpenRead(file);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (!TryFindGuid(root, out var personId)
                && !Guid.TryParse(Path.GetFileNameWithoutExtension(file), out personId))
            {
                return false;
            }

            character = new NpcCharacterDescriptor
            {
                PersonId = personId,
                FilePath = file,
                DisplayName = BuildDisplayName(root),
                BirthDateText = BuildBirthDateText(root),
                BiologyText = TryGetProperty(root, "Biology", out var biology) ? biology.ToString() : null
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? BuildDisplayName(JsonElement root)
    {
        if (!TryGetProperty(root, "Identity", out var identity))
        {
            return FindString(root, "Name", "FullName", "DisplayName");
        }

        var firstName = TryGetProperty(identity, "FirstName", out var firstNameElement)
            ? FindString(firstNameElement, "Original", "Name")
            : null;
        var lastName = TryGetProperty(identity, "LastName", out var lastNameElement)
            ? FindString(lastNameElement, "Male", "Female", "Original", "Name")
            : null;

        var fullName = string.Join(" ", new[] { firstName, lastName }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return string.IsNullOrWhiteSpace(fullName)
            ? FindString(root, "FullName", "DisplayName", "Name")
            : fullName;
    }

    private static string? BuildBirthDateText(JsonElement root)
    {
        if (!TryGetProperty(root, "Identity", out var identity)
            || !TryGetProperty(identity, "BirthDate", out var birthDate))
        {
            return null;
        }

        var year = TryGetProperty(birthDate, "Year", out var yearElement) ? yearElement.ToString() : null;
        var month = TryGetProperty(birthDate, "Month", out var monthElement) ? monthElement.ToString() : null;
        var day = TryGetProperty(birthDate, "Day", out var dayElement) ? dayElement.ToString() : null;
        var dayIndex = TryGetProperty(birthDate, "DayIndex", out var dayIndexElement) ? dayIndexElement.ToString() : null;

        if (!string.IsNullOrWhiteSpace(year) && !string.IsNullOrWhiteSpace(month) && !string.IsNullOrWhiteSpace(day))
        {
            return $"Y{year} M{month} D{day}";
        }

        return string.IsNullOrWhiteSpace(dayIndex) ? null : $"DayIndex {dayIndex}";
    }

    private static bool TryFindGuid(JsonElement element, out Guid value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsGuidPropertyName(property.Name)
                    && property.Value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(property.Value.GetString(), out value))
                {
                    return true;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindGuid(property.Value, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindGuid(item, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool IsGuidPropertyName(string name)
        => string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "PersonId", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "HumanId", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "CharacterId", StringComparison.OrdinalIgnoreCase);

    private static string? FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindString(item, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
