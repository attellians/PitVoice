using System.Collections.ObjectModel;
using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Host;
using PitVoice.Models;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>Rows and groups of the "Models" page (design D10 of simplify-window-screens; spec: Models in the window).</summary>
[Collection("Interface language")]
public sealed class ModelRowTests : IDisposable
{
    private static readonly DateTimeOffset Installed = new(2026, 10, 19, 10, 30, 0, TimeSpan.Zero);

    private static readonly ModelEntry Ru = Entry("vosk-model-small-ru-0.22", "ru");

    private static readonly ModelEntry En = Entry("vosk-model-small-en-us-0.15", "en");

    private readonly string _language = Loc.Instance.Language;

    public ModelRowTests() => Loc.Instance.SetLanguage("ru");

    public void Dispose() => Loc.Instance.SetLanguage(_language);

    private static ModelEntry Entry(string id, string language) => new(
        id,
        ModelType.Asr,
        language,
        new Uri("https://example.test/model.zip"),
        null,
        45_000_000,
        new string('0', 64),
        "Apache-2.0",
        "https://alphacephei.com/vosk/models");

    private static ModelRow Row(ModelItem item, string name = "Русский") => new(item, name, _ => Task.CompletedTask, _ => { }, _ => Task.CompletedTask);

    /// <summary>Everything a row shows in text, to check that the date, the id and the license are not there.</summary>
    private static string Shown(ModelRow row) => string.Join(" ", row.LanguageName, row.SizeText, row.ProgressText, row.Error);

    [Fact]
    public void In_use_row_is_installed_with_the_green_mark_and_a_delete_icon()
    {
        var row = Row(new ModelItem(Ru, ModelState.InUse, null, null, Installed));

        Assert.Equal("Русский", row.LanguageName);
        Assert.True(row.IsInUse);
        Assert.True(row.IsInstalled);
        Assert.True(row.CanDelete);
        Assert.False(row.CanDownload);
        Assert.Equal(string.Empty, row.SizeText);
        Assert.Equal("используется", Loc.T("ModelInUse"));
    }

    [Fact]
    public void Installed_but_not_in_use_has_no_mark()
    {
        var row = Row(new ModelItem(En, ModelState.Installed, null, null, Installed), "English");

        Assert.True(row.IsInstalled);
        Assert.False(row.IsInUse);
        Assert.True(row.CanDelete);
    }

    [Fact]
    public void Not_downloaded_row_has_the_size_and_a_download_icon_without_date_id_or_license()
    {
        var row = Row(new ModelItem(En, ModelState.NotInstalled, null, null), "English");

        Assert.False(row.IsInstalled);
        Assert.True(row.CanDownload);
        Assert.Equal("42.9 МБ", row.SizeText);
        var shown = Shown(row);
        Assert.DoesNotContain(En.Id, shown, StringComparison.Ordinal);
        Assert.DoesNotContain("Apache", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void Downloading_row_shows_the_progress_and_a_cancel_icon()
    {
        var progress = new DownloadProgress(DownloadStage.Downloading, 12_000_000, 45_000_000);
        var row = Row(new ModelItem(Ru, ModelState.Downloading, progress, null));

        Assert.True(row.IsDownloading);
        Assert.False(row.IsInstalled);
        Assert.False(row.CanDownload);
        Assert.Equal("11.4 из 42.9 МБ", row.ProgressText);
        Assert.InRange(row.Percent, 26, 27);
    }

    [Fact]
    public void Failed_download_shows_the_error_and_the_download_icon_again()
    {
        var row = Row(new ModelItem(Ru, ModelState.NotInstalled, null, "нет соединения"));

        Assert.Equal("Ошибка: нет соединения", row.Error);
        Assert.True(row.CanDownload);
    }

    [Fact]
    public void Download_from_the_window_moves_the_row_to_installed() => Sta.Run(() =>
    {
        var row = Row(new ModelItem(Ru, ModelState.NotInstalled, null, null));
        var rows = new ObservableCollection<ModelRow> { row };
        var groups = new ModelGroups(rows);
        Assert.Equal((false, true), (groups.HasInstalled, groups.HasAvailable));

        row.Update(new ModelItem(Ru, ModelState.Downloading, new DownloadProgress(DownloadStage.Downloading, 1, 2), null));
        groups.Refresh();
        Assert.Same(row, Assert.Single(groups.Available.Cast<ModelRow>()));

        row.Update(new ModelItem(Ru, ModelState.InUse, null, null, Installed));
        groups.Refresh();

        Assert.Same(row, Assert.Single(groups.Installed.Cast<ModelRow>()));
        Assert.Equal((true, false), (groups.HasInstalled, groups.HasAvailable));
    });

    [Fact]
    public void All_models_installed_hides_the_available_group() => Sta.Run(() =>
    {
        var rows = new ObservableCollection<ModelRow>
        {
            Row(new ModelItem(Ru, ModelState.InUse, null, null, Installed)),
            Row(new ModelItem(En, ModelState.Installed, null, null, Installed), "English"),
        };

        var groups = new ModelGroups(rows);

        Assert.Equal(2, groups.Installed.Cast<ModelRow>().Count());
        Assert.True(groups.HasInstalled);
        Assert.False(groups.HasAvailable);
    });

    /// <summary>Spec: Delete model in use — the row moves to "Можно скачать".</summary>
    [Fact]
    public void Deleting_the_model_in_use_moves_the_row_to_available() => Sta.Run(() =>
    {
        var row = Row(new ModelItem(Ru, ModelState.InUse, null, null, Installed));
        var groups = new ModelGroups([row]);

        row.Update(new ModelItem(Ru, ModelState.NotInstalled, null, null));
        groups.Refresh();

        Assert.Same(row, Assert.Single(groups.Available.Cast<ModelRow>()));
        Assert.False(groups.HasInstalled);
        Assert.True(row.CanDownload);
    });
}
