// Enums.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    #region Homo

    public enum GenusType
    { Male, Female };

    #region Relationship ortogonal enums

    // Civilní stav osoby (náhrada za RelationshipStatusType; patří na Person)
    public enum CivilStatus
    {
        Single, InRelationship, Engaged, Married, Divorced, Widowed
    }

    #endregion Relationship ortogonal enums

    public enum StadiumType
    { Baby, Child, Teenager, Adult, MidAged, Old };

    public enum StatusType
    { Unborn, Alive, Dead };

    #endregion Homo

    #region Magic

    public enum MagicType
    { Water, Air, Fire, Earth, Unknown }

    #endregion Magic
}
