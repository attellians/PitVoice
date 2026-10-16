using PitVoice.Core.Settings;

namespace PitVoice.Host;

/// <summary>An item of "What is left to set up"; the window maps each to its page.</summary>
public enum SetupItem
{
    /// <summary>Download the model of the language ("Models").</summary>
    DownloadModel,

    /// <summary>Assign the PTT button ("Settings").</summary>
    AssignButton,

    /// <summary>Check a phrase ("Check").</summary>
    CheckPhrase,
}

/// <summary>
/// The setup checklist (spec: Setup checklist). It is computed from the current state each time, so it is gone when
/// the model is installed, the button assigned and a check done, and comes back when the model is deleted later.
/// </summary>
public static class SetupChecklist
{
    public static IReadOnlyList<SetupItem> Pending(HostStatus status, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(settings);
        var items = new List<SetupItem>(3);
        if (!status.Facts.ModelInstalled)
        {
            items.Add(SetupItem.DownloadModel);
        }

        if (settings.PttBinding is null)
        {
            items.Add(SetupItem.AssignButton);
        }

        if (!settings.SetupCheckDone)
        {
            items.Add(SetupItem.CheckPhrase);
        }

        return items;
    }
}
