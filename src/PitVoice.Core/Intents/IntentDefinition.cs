namespace PitVoice.Core.Intents;

/// <summary>
/// Catalog metadata of an intent: which slots it carries, which exclusion group it belongs to and whether it can be
/// combined with other intents in one phrase.
/// </summary>
/// <param name="Id">Intent id.</param>
/// <param name="Slots">Slots the intent accepts. Templates may only use these.</param>
/// <param name="ExclusionGroup">
/// Name of the group of mutually exclusive intents (<c>fuel</c>, <c>tires</c>, <c>windshield</c>, <c>repair</c>) or
/// <c>null</c>. Inside a group a negating intent conflicts with every non-negating one.
/// </param>
/// <param name="IsNegation">True for intents that cancel their group (<c>tires.none</c>, <c>fuel.none</c>, ...).</param>
/// <param name="IsStandalone">True for intents that cannot be combined with any other intent in one phrase.</param>
public sealed record IntentDefinition(
    IntentId Id,
    IReadOnlyList<SlotKind> Slots,
    string? ExclusionGroup,
    bool IsNegation,
    bool IsStandalone)
{
    public bool HasSlot(SlotKind kind) => Slots.Contains(kind);
}
