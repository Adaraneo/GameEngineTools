// CsvLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal static class CsvLoader
    {
        public static List<T> Load<T>(string filePath, Func<string[], T> parser)
        {
            var result = new List<T>();

            using var reader = new StreamReader(filePath, Encoding.UTF8);

            reader.ReadLine();
            string? line;
            while((line = reader.ReadLine()) != null)
            {
                var values = line.Split(';');
                result.Add(parser(values));
            }

            return result;
        }
    }
}
