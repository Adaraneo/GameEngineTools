// AdjacencyPairResolver.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Conversation
{
    /// <summary>
    /// The expected second-pair-part of an adjacency pair: which relational act a listener is
    /// conversationally obligated to produce in response, and the dialogue dimension that marks it.
    /// </summary>
    /// <param name="Kind">Relational act kind of the response.</param>
    /// <param name="Dimensions">Dimension flags carried by the response (feedback / social obligation).</param>
    public sealed record AdjacencyResponse(RelationalActKind Kind, DialogueDimension Dimensions);

    /// <summary>
    /// Maps a received act to the response it invites (Sacks, Schegloff &amp; Jefferson 1974 adjacency
    /// pairs): a question wants an answer, a self-disclosure wants validation, a greeting wants a
    /// greeting back. Acts with no strong second-pair-part return <c>null</c> (free continuation).
    /// </summary>
    public static class AdjacencyPairResolver
    {
        /// <summary>Returns the expected response to <paramref name="received"/>, or <c>null</c> if none.</summary>
        public static AdjacencyResponse? ResponseTo(SpeechAct received)
        {
            System.ArgumentNullException.ThrowIfNull(received);

            return received.RelationalKind switch
            {
                // Greeting (small talk carrying a social obligation) ⇒ return the greeting.
                RelationalActKind.SmallTalk when received.Dimensions.HasFlag(DialogueDimension.SocialObligation)
                    => new AdjacencyResponse(RelationalActKind.SmallTalk, DialogueDimension.SocialObligation | DialogueDimension.Feedback),

                // Question ⇒ answer (given as ordinary talk, marked as feedback/uptake).
                RelationalActKind.Question => new AdjacencyResponse(RelationalActKind.SmallTalk, DialogueDimension.Feedback),

                // Self-disclosure ⇒ validation (reciprocity / support).
                RelationalActKind.SelfDisclosure => new AdjacencyResponse(RelationalActKind.Validation, DialogueDimension.Feedback),

                // Invite ⇒ a response turn (accept/decline is decided by the interaction engine;
                // here we only obligate a responding move).
                RelationalActKind.Invite => new AdjacencyResponse(RelationalActKind.Validation, DialogueDimension.Feedback),

                // Validation ⇒ acknowledgement.
                RelationalActKind.Validation => new AdjacencyResponse(RelationalActKind.SmallTalk, DialogueDimension.Feedback),

                _ => null,
            };
        }
    }
}
