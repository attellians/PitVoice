using PitVoice.Core.Pit;

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
    /// <summary>
    /// True for intents that change what happens in the pit stop. Their patterns must have at least two words: a single
    /// word is too easily heard in unrelated speech (spike of stage 2: "кто впереди меня" was recognized as "перед").
    /// </summary>
    public bool ChangesPitSetup { get; init; } = true;

    /// <summary>
    /// Wheels a tire intent works on; <see cref="Wheels.None"/> for other intents. Whether they are added to the
    /// selection or removed from it is given by <see cref="IsNegation"/> (see <see cref="AddsWheels"/>,
    /// <see cref="RemovesWheels"/>).
    /// </summary>
    public Wheels Wheels { get; init; }

    /// <summary>Wheels this intent adds to the selection in the black box.</summary>
    public Wheels AddsWheels => IsNegation ? Wheels.None : Wheels;

    /// <summary>Wheels this intent removes from the selection in the black box.</summary>
    public Wheels RemovesWheels => IsNegation ? Wheels : Wheels.None;

    public bool HasSlot(SlotKind kind) => Slots.Contains(kind);
}
