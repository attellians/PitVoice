using PitVoice.App.ViewModels;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>The decision of the leave dialog (design D8; spec: Leaving with unsaved changes).</summary>
public sealed class LeaveFlowTests
{
    private sealed class Guard(bool changed, bool saves) : ILeaveGuard
    {
        public bool HasUnsavedChanges { get; private set; } = changed;

        public int Saves { get; private set; }

        public int Discards { get; private set; }

        public Task<bool> SaveAsync()
        {
            Saves++;
            HasUnsavedChanges = !saves;
            return Task.FromResult(saves);
        }

        public void Discard()
        {
            Discards++;
            HasUnsavedChanges = false;
        }
    }

    private static Func<Task<LeaveChoice>> Answer(LeaveChoice choice, List<LeaveChoice> asked) => () =>
    {
        asked.Add(choice);
        return Task.FromResult(choice);
    };

    [Fact]
    public async Task Leave_and_save()
    {
        var guard = new Guard(changed: true, saves: true);
        var asked = new List<LeaveChoice>();

        Assert.True(await LeaveFlow.ConfirmAsync(guard, Answer(LeaveChoice.Save, asked)));

        Assert.Equal(1, guard.Saves);
        Assert.Single(asked);
    }

    [Fact]
    public async Task Leave_without_saving()
    {
        var guard = new Guard(changed: true, saves: true);

        Assert.True(await LeaveFlow.ConfirmAsync(guard, Answer(LeaveChoice.Discard, [])));

        Assert.Equal((0, 1), (guard.Saves, guard.Discards));
        Assert.False(guard.HasUnsavedChanges);
    }

    [Fact]
    public async Task Stay()
    {
        var guard = new Guard(changed: true, saves: true);

        Assert.False(await LeaveFlow.ConfirmAsync(guard, Answer(LeaveChoice.Stay, [])));

        Assert.Equal((0, 0), (guard.Saves, guard.Discards));
        Assert.True(guard.HasUnsavedChanges);
    }

    [Fact]
    public async Task Save_fails_in_the_dialog()
    {
        var guard = new Guard(changed: true, saves: false);

        Assert.False(await LeaveFlow.ConfirmAsync(guard, Answer(LeaveChoice.Save, [])));

        Assert.Equal(1, guard.Saves);
        Assert.True(guard.HasUnsavedChanges);
    }

    [Fact]
    public async Task No_changes_no_question()
    {
        var asked = new List<LeaveChoice>();

        Assert.True(await LeaveFlow.ConfirmAsync(new Guard(changed: false, saves: true), Answer(LeaveChoice.Stay, asked)));
        Assert.True(await LeaveFlow.ConfirmAsync(null, Answer(LeaveChoice.Stay, asked)));

        Assert.Empty(asked);
    }
}
