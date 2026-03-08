// GenerateFileSystem.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using GameEngineTools.Exceptions;
    using GD = GameEngineTools.Constants.FileSystemConstant.GeneratedDirectory;

    internal class GenerateFileSystem
    {
        /* TODO:
         * Systém souborů pro import a export vygenerovaných souborů
         * Vytváření nezbytných složek (Název složky: DNA nppc)
         * Načítání a příprava souborů na import
         * Příprava na export
         */

        private List<string> filenames = new List<string>();

        private void GetAllFiles(params string[] dirNames)
        {
            if (dirNames.Count() > 0)
            {
                foreach (string dirName in dirNames)
                {
                    foreach (var filename in Directory.GetFiles(dirName))
                    {
                        var info = new FileInfo(filename);
                        var dirInfo = new DirectoryInfo(dirName);
                        switch (dirInfo.Name)
                        {
                            case "Player":
                                filenames.Insert(0, info.Name);
                                break;

                            case "NPCs":
                                filenames.Add(info.Name);
                                break;
                        }
                    }
                }
            }
            else
            {
                throw new GFSArgumentNullException("Input parameters are not set!");
            }
        }

        public GenerateFileSystem(string? root = null)
        {
            root ??= Directory.GetCurrentDirectory();
            var playerDirectory = Path.Combine(root, GD.player);
            var npcsDirectory = Path.Combine(root, GD.npcs); ;
            if (!Directory.Exists(playerDirectory) || !Directory.Exists(npcsDirectory))
            {
                Directory.CreateDirectory(playerDirectory);
                Directory.CreateDirectory(npcsDirectory);
            }
            else
            {
                GetAllFiles(playerDirectory, npcsDirectory);
            }
        }

        public List<string> Filenames
        { get { return filenames; } }
    }
}
