using PitVoice.Core.Settings;
using Xunit;

namespace PitVoice.Host.Tests;

/// <summary>"What is left to set up" (spec: Setup checklist).</summary>
public sealed class SetupChecklistTests
{
    [Fact]
    public async Task Fresh_install_lists_model_button_and_check()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru"), installedModels: [HostFixture.EnModel]);
        var host = await fixture.StartAsync();

        Assert.Equal(
            [SetupItem.DownloadModel, SetupItem.AssignButton, SetupItem.CheckPhrase],
            SetupChecklist.Pending(host.Status, host.Settings));
    }

    [Fact]
    public async Task Setup_complete_stays_complete_after_a_restart()
    {
        await using var fixture = new HostFixture();
        var host = await fixture.StartAsync();
        using (var check = new CommandCheck(host))
        {
            check.CheckText("залей тридцать литров");
        }

        Assert.Empty(SetupChecklist.Pending(host.Status, host.Settings));

        await using var restarted = new HostFixture(fixture.Store.Current);
        var again = await restarted.StartAsync();
        Assert.Empty(SetupChecklist.Pending(again.Status, again.Settings));
    }

    [Fact]
    public async Task Model_deleted_later_brings_the_model_item_back()
    {
        await using var fixture = new HostFixture(AppSettings.Defaults("ru") with { PttBinding = HostFixture.KeyBinding, SetupCheckDone = true });
        var host = await fixture.StartAsync();
        Assert.Empty(SetupChecklist.Pending(host.Status, host.Settings));

        fixture.Models.Delete(HostFixture.RuModel);
        await host.ReloadAsync().WaitAsync(HostFixture.Wait);

        Assert.Equal([SetupItem.DownloadModel], SetupChecklist.Pending(host.Status, host.Settings));
    }
}
