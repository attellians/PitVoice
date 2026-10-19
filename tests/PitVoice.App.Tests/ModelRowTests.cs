using PitVoice.App.Localization;
using PitVoice.App.ViewModels;
using PitVoice.Host;
using PitVoice.Models;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>A row of the "Models" page (design D6; spec: Models in the window).</summary>
[Collection("Interface language")]
public sealed class ModelRowTests : IDisposable
{
    private static readonly DateTimeOffset Installed = new(2026, 10, 19, 10, 30, 0, TimeSpan.Zero);

    private static readonly ModelEntry Entry = new(
        "vosk-model-small-ru-0.22",
        ModelType.Asr,
        "ru",
        new Uri("https://example.test/ru.zip"),
        null,
        45_000_000,
        new string('0', 64),
        "Apache-2.0",
        "https://alphacephei.com/vosk/models");

    private readonly string _language = Loc.Instance.Language;

    public ModelRowTests() => Loc.Instance.SetLanguage("ru");

    public void Dispose() => Loc.Instance.SetLanguage(_language);

    private static ModelRow Row(ModelItem item) => new(item, "Русский", _ => Task.CompletedTask, _ => { }, _ => Task.CompletedTask);

    [Fact]
    public void In_use_row_has_the_code_the_date_and_the_green_state()
    {
        var row = Row(new ModelItem(Entry, ModelState.InUse, null, null, Installed));

        Assert.Equal("RU", row.Code);
        Assert.Equal("Русский", row.LanguageName);
        Assert.Equal("используется", row.StateText);
        Assert.True(row.IsInUse);
        Assert.Equal("42.9 МБ · установлена " + ModelRow.FormatDate(Installed) + " · vosk-model-small-ru-0.22 · лицензия Apache-2.0", row.Details);
        Assert.True(row.CanDelete);
        Assert.False(row.CanDownload);
    }

    [Fact]
    public void Installed_row_is_neutral()
    {
        var row = Row(new ModelItem(Entry, ModelState.Installed, null, null, Installed));

        Assert.Equal("установлена", row.StateText);
        Assert.False(row.IsInUse);
        Assert.False(row.IsNotInstalled);
        Assert.True(row.CanDelete);
    }

    [Fact]
    public void Not_installed_row_has_no_date_and_offers_download()
    {
        var row = Row(new ModelItem(Entry, ModelState.NotInstalled, null, null));

        Assert.Equal("не скачана", row.StateText);
        Assert.True(row.IsNotInstalled);
        Assert.DoesNotContain("установлена", row.Details, StringComparison.Ordinal);
        Assert.True(row.CanDownload);
        Assert.Equal(string.Empty, row.ProgressText);
    }

    [Fact]
    public void Downloading_row_shows_bytes_of_total()
    {
        var progress = new DownloadProgress(DownloadStage.Downloading, 12_000_000, 45_000_000);
        var row = Row(new ModelItem(Entry, ModelState.Downloading, progress, null));

        Assert.Equal("скачивается", row.StateText);
        Assert.True(row.IsDownloading);
        Assert.Equal("11.4 из 42.9 МБ", row.ProgressText);
        Assert.InRange(row.Percent, 26, 27);
    }

    [Fact]
    public void Error_is_shown_in_the_row()
    {
        var row = Row(new ModelItem(Entry, ModelState.NotInstalled, null, "no connection"));

        Assert.Equal("Ошибка: no connection", row.Error);
        Assert.True(row.CanDownload);
    }

    [Fact]
    public void Date_follows_the_interface_language()
    {
        var local = Installed.ToLocalTime();
        Assert.Equal(local.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture), ModelRow.FormatDate(Installed));

        Loc.Instance.SetLanguage("en");
        Assert.Equal(local.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture), ModelRow.FormatDate(Installed));
    }
}
