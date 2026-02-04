// FileSystemConstant.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Constants
{
    /// <summary>
    /// FileSystem Constants containts Direcotries and Filenames
    /// </summary>
    internal static class FileSystemConstant
    {
        /// <summary>
        /// Directories for source files
        /// </summary>
        private static class SourceDirectory
        {
            private const string root = @"SourceFiles\";
            public const string armory = root + @"Armory\";
            public const string names = root + @"Names\";
        }

        /// <summary>
        /// Filenames of source files
        /// </summary>
        private static class SourceFilename
        {
            public const string armorPartsFilename = "ArmorParts";
            public const string femaleNamesFilename = "Female";
            public const string maleNamesFilename = "Male";
            public const string surnamesFilename = "Surnames";
            public const string weaponsFilename = "Weapons";
        }

        /// <summary>
        /// Files in source directory
        /// </summary>
        internal static class SourceFilePath
        {
            public const string armorParts = SourceDirectory.armory + SourceFilename.armorPartsFilename + Extension.sourceCSV;
            public const string femaleNames = SourceDirectory.names + SourceFilename.femaleNamesFilename + Extension.sourceCSV;
            public const string maleNames = SourceDirectory.names + SourceFilename.maleNamesFilename + Extension.sourceCSV;
            public const string surnames = SourceDirectory.names + SourceFilename.surnamesFilename + Extension.sourceCSV;
            public const string weapons = SourceDirectory.armory + SourceFilename.weaponsFilename + Extension.sourceCSV;
        }

        /// <summary>
        /// Extensions
        /// </summary>
        public static class Extension
        {
            public const string generatedJSON = ".json";
            public const string sourceCSV = ".csv";
        }

        /// <summary>
        /// Directories for generated files
        /// </summary>
        public static class GeneratedDirectory
        {
            public const string npcs = root + @"NPCs\";
            public const string player = root + @"Player\";
            public const string root = @"Generated\";
        }
    }

    /// <summary>
    /// For test purposes only!
    /// </summary>
    public static class FileSystemConstantsForTest
    {
        public const string npc = FileSystemConstant.GeneratedDirectory.npcs;
        public const string pc = FileSystemConstant.GeneratedDirectory.player;
        public const string root = FileSystemConstant.GeneratedDirectory.root;
    }

    /// <summary>
    /// TODO: Remove! For test purposes only!
    /// </summary>
    public static class TestFSConstatns
    {
        public static string gfiles = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + @"\gfiles\";
        public static string NPCs = gfiles + @"NPCs\";
        public static string player = gfiles + @"Player\";
    }
}
