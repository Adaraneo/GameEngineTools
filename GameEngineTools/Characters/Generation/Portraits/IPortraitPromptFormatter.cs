// IPortraitPromptFormatter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation.Portraits
{
    /// <summary>
    /// Exports a strict human-readable portrait prompt from a portrait specification.
    /// </summary>
    public interface IPortraitPromptFormatter
    {
        /// <summary>
        /// Formats a portrait prompt grounded in the supplied specification.
        /// </summary>
        /// <param name="spec">Portrait specification to export.</param>
        /// <returns>Human-readable portrait prompt.</returns>
        string Format(PortraitSpec spec);
    }
}
