// IGeneratedFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using GameEngineTools.Characters.GameObjects;

    /// <summary>Serialises playable and non-playable characters to and from generated files.</summary>
    public interface IGeneratedFile
    {
        /// <summary>Exports a playable character and returns the written file path.</summary>
        /// <param name="player">The playable character to export.</param>
        string Export(PC player);

        /// <summary>Exports a non-playable character and returns the written file path.</summary>
        /// <param name="npc">The non-playable character to export.</param>
        string Export(NPC npc);

        /// <summary>Imports a playable character from a file.</summary>
        /// <param name="filename">Source file path.</param>
        PC ImportPC(string filename);

        /// <summary>Imports a non-playable character from a file.</summary>
        /// <param name="filename">Source file path.</param>
        NPC ImportNPC(string filename);

        /// <summary>Exports all non-playable characters under the given root directory.</summary>
        /// <param name="pathToRootDirectory">Optional root directory; defaults to the configured location.</param>
        void ExportNPPCs(string? pathToRootDirectory = null);

        /// <summary>Imports all non-playable characters from the given root directory.</summary>
        /// <param name="pathToRootDirectory">Optional root directory; defaults to the configured location.</param>
        void ImportNPPCs(string? pathToRootDirectory = null);
    }
}
