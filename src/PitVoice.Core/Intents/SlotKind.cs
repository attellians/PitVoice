namespace PitVoice.Core.Intents;

/// <summary>Kinds of values a template can capture.</summary>
public enum SlotKind
{
    /// <summary>Integer 0..999, written as <c>{amount}</c> in templates.</summary>
    Amount,

    /// <summary>Fuel unit, written as <c>{unit}</c> (required) or <c>{unit?}</c> (optional) in templates.</summary>
    Unit,
}
