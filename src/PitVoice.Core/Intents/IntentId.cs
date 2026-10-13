namespace PitVoice.Core.Intents;

/// <summary>
/// Identifier of a pit command intent, e.g. <c>fuel.add</c>. The set of valid ids is fixed by <see cref="IntentCatalog"/>.
/// </summary>
public readonly record struct IntentId(string Value) : IComparable<IntentId>
{
    public int CompareTo(IntentId other) => string.CompareOrdinal(Value, other.Value);

    public override string ToString() => Value;
}
