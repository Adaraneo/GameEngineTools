// Enums.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    #region Homo

    #region Relationship ortogonal enums

    /// <summary>Civil (relationship) status of a character.</summary>
    public enum CivilStatus
    {
        /// <summary>Single.</summary>
        Single,

        /// <summary>In a relationship.</summary>
        InRelationship,

        /// <summary>Engaged.</summary>
        Engaged,

        /// <summary>Married.</summary>
        Married,

        /// <summary>Divorced.</summary>
        Divorced,

        /// <summary>Widowed.</summary>
        Widowed
    }

    #endregion Relationship ortogonal enums

    /// <summary>Life stage of a character.</summary>
    public enum StadiumType
    {
        /// <summary>Baby.</summary>
        Baby,

        /// <summary>Child.</summary>
        Child,

        /// <summary>Teenager.</summary>
        Teenager,

        /// <summary>Adult.</summary>
        Adult,

        /// <summary>Middle-aged.</summary>
        MidAged,

        /// <summary>Old.</summary>
        Old
    };

    /// <summary>Vital status of a character.</summary>
    public enum StatusType
    {
        /// <summary>Not yet born.</summary>
        Unborn,

        /// <summary>Alive.</summary>
        Alive,

        /// <summary>Dead.</summary>
        Dead
    };

    #endregion Homo

    #region Magic

    /// <summary>Elemental magic type.</summary>
    public enum MagicType
    {
        /// <summary>Water.</summary>
        Water,

        /// <summary>Air.</summary>
        Air,

        /// <summary>Fire.</summary>
        Fire,

        /// <summary>Earth.</summary>
        Earth,

        /// <summary>Unknown / none.</summary>
        Unknown
    }

    #endregion Magic
}
