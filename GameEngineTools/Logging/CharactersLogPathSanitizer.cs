// CharactersLogPathSanitizer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Sanitises subsystem names before using them in a file name.
    /// </summary>
    internal static class CharactersLogPathSanitizer
    {
        public static string SanitizeSubsystemFileName(string? subsystem)
        {
            if (string.IsNullOrWhiteSpace(subsystem))
            {
                return "person";
            }

            var invalid = Path.GetInvalidFileNameChars();
            var chars = subsystem.Trim().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (invalid.Contains(chars[i]))
                {
                    chars[i] = '_';
                }
            }

            var sanitized = new string(chars);
            return string.IsNullOrWhiteSpace(sanitized) ? "person" : sanitized;
        }
    }
}
