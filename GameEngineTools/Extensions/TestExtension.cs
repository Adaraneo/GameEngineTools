// TestExtension.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using System.Data;
    using System.Text;
    using GameEngineTools;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.World.Utils.Time;

    public static class TestExtension
    {
        //internal static string WithWho(this CivilStatus status, in IHuman person, bool withDNA = false)
        //{
        //    string information = null;
        //    Person other = null;
        //    string fullName = null;
        //    RelationshipModel relations = null;
        //    foreach (var relation in person.Relationships.Values)
        //    {
        //        if (relation.BondKind == BondKind.RomanticInterest)
        //        {
        //            relations = relation;
        //        }
        //    }

        //    switch (status)
        //    {
        //        case CivilStatus.InRelationship:
        //        case CivilStatus.Engaged:
        //        case CivilStatus.Married:
        //            other = NPPC.People.FindByDNA(relations!.TargetId);
        //            break;
        //    }

        //    fullName = other == null ? string.Empty : other.Name.ToString() + " " + other.Surname;
        //    information = other == null ? string.Empty : (withDNA ? fullName + "\n\tDNA: " + other.Body.DNA : fullName);
        //    return information;
        //}

        public static List<CharacterBase> CreateFamilyForPlayer(this GameEngineToolsManager instance, PC player)
        {
            throw new NotImplementedException();
        }

        public static void DoMagic(this CharacterBase caster, params object[] tagrets)
        {
            if (caster != null)
            {
                foreach (var target in tagrets)
                {
                    caster.DoMagic(target);
                }
            }
        }

        public static string PrintInfo(this CharacterBase nppc, bool basicInfo = true, bool withDNA = false)
        {
            var sbResult = new StringBuilder();
            var person = nppc.Person;
            var name = person.Identity.FirstName;
            sbResult.AppendLine($"Name: {(string.IsNullOrEmpty(name.Familiar[0]) ? name.Original : name.Familiar[new Random().Next(0, name.Familiar.Length)])} {person.Identity.LastName}");
            sbResult.AppendLine($"Born in: {person.Identity.BirthDate.Year}");

            if (!basicInfo)
            {

            }

            return sbResult.ToString();
        }
    }
}
