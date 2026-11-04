// IdSequence.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Makes identifiers for things a character creates (memories, goals) from what is deterministic: who, when, and a running count. Equal seeds therefore give equal worlds, which random <see cref="Guid"/>s would break.
    /// </summary>
    internal sealed class IdSequence
    {
        private readonly string _salt;
        private long _count;

        /// <summary>Creates a sequence; <paramref name="salt"/> keeps different kinds of ids from colliding.</summary>
        public IdSequence(string salt) => _salt = salt;

        /// <summary>Returns the next id for something created at <paramref name="when"/> (optionally by <paramref name="owner"/>).</summary>
        public Guid Next(WDateTime when, HumanId? owner = null)
        {
            var key = $"{_salt}|{owner?.Value}|{when.WorldTicks}|{_count++}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return new Guid(hash.AsSpan(0, 16));
        }
    }
}
