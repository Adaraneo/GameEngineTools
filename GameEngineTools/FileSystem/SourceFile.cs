// SourceFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System.Globalization;
    using System.Text;
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;

    internal class SourceFile
    {
        private string[]? filenames;

        private List<object> GetRowsAt(int indexOfFile)
        {
            var extension = new FileInfo(filenames[indexOfFile]).Extension;
            StreamReader reader = new StreamReader(filenames[indexOfFile], Encoding.UTF8);
            List<object> rows = new List<object>();
            string row = null;
            switch (extension)
            {
                case ".csv":
                    row = reader.ReadLine();
                    while ((row = reader.ReadLine()) != null)
                    {
                        string[] values = row.Split(';');
                        rows.Add(values);
                    }
                    break;

                case ".txt":
                    throw new NotImplementedException();
            }

            reader.Close();
            return rows;
        }

        public void Load<T>(out List<T> objects, int indexOfFile) where T : class, new()
        {
            objects = new List<T>();
            foreach (var row in GetRowsAt(indexOfFile))
            {
                string[]? values = row as string[];
                T @object = new T();
                if (@object is Name names)
                {
                    names.Original = values[0];
                    names.Familiar = values[1].Split(' ');
                    objects.Add(@object);
                }

                if (@object is Surname surnames)
                {
                    surnames.Male = values[0];
                    surnames.Female = values[1];
                    objects.Add(@object);
                }

                if (@object is Weapon weapon)
                {
                    var v = new Weapon(values[0], Enum.Parse<Weapon.WeaponType>(values[1]), double.Parse(values[2], CultureInfo.InvariantCulture));
                    weapon.HitPoints = v.HitPoints;
                    weapon.MaxHitPoints = v.MaxHitPoints;
                    weapon.Type = v.Type;
                    weapon.Name = v.Name;
                    objects.Add(@object);
                }

                if (@object is ArmorPart armorPart)
                {
                    var ap = new ArmorPart(values[0], Enum.Parse<ArmorPart.PartType>(values[1]), double.Parse(values[2], CultureInfo.InvariantCulture));
                    armorPart.Protection = ap.Protection;
                    armorPart.MaxProtection = ap.MaxProtection;
                    armorPart.Name = ap.Name;
                    armorPart.TypeOfPart = ap.TypeOfPart;
                    objects.Add(@object);
                }
            }
        }

        public void SetFilenames(params string[] filenames)
        {
            this.filenames = filenames;
        }
    }
}
