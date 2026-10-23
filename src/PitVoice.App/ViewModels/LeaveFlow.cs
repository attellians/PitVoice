namespace PitVoice.App.ViewModels;

/// <summary>The answer of the leave dialog.</summary>
internal enum LeaveChoice
{
    Save,
    Discard,
    Stay,
}

/// <summary>
/// Leaving a page with unsaved changes (spec: Leaving with unsaved changes): "Сохранить" leaves only after a successful
/// save, "Не сохранять" drops the draft and leaves, "Остаться" keeps the page and the draft. The window shows the dialog;
/// this is the decision, so it is tested without one.
/// </summary>
internal static class LeaveFlow
{
    /// <summary>True when the leave may go on.</summary>
    /// <param name="guard">The page being left; null or without changes means no question.</param>
    /// <param name="ask">Shows the dialog.</param>
    public static async Task<bool> ConfirmAsync(ILeaveGuard? guard, Func<Task<LeaveChoice>> ask)
    {
        ArgumentNullException.ThrowIfNull(ask);
        if (guard is not { HasUnsavedChanges: true })
        {
            return true;
        }

        switch (await ask())
        {
            case LeaveChoice.Save:
                return await guard.SaveAsync();
            case LeaveChoice.Discard:
                guard.Discard();
                return true;
            default:
                return false;
        }
    }
}
