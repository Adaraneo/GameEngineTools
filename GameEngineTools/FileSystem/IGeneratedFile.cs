// IFileGeneratedFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.GameObjects;

    public interface IGeneratedFile
    {
        string Export(PC player);
        string Export(NPC npc);
        PC ImportPC(string filename);
        NPC ImportNPC(string filename);
        void ExportNPPCs(string pathToRootDirectory = null);
        void ImportNPPCs(string pathToRootDirectory = null);
    }
}
