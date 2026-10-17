using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.Models;
using Velopack.Locators;

namespace PitVoice.App.ViewModels;

/// <summary>A third-party component or a model with its license.</summary>
internal sealed record LicensedItem(string Name, string Version, string License, string Url);

/// <summary>
/// "About" (spec: About section): version, license, repository, the notices file of this version, components and
/// models with their licenses.
/// </summary>
internal sealed partial class AboutViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/attellians/PitVoice";

    public AboutViewModel(AppServices services)
    {
        Version = CurrentVersion;
        Components = LoadComponents();
        Models = services.Models.Manifest?.Entries
            .Where(e => e.Type == ModelType.Asr)
            .Select(e => new LicensedItem(e.Id, string.Empty, e.License, e.Source))
            .ToArray() ?? [];
        NoticesUrl = NoticesAddress(Version, IsInstalled());
    }

    /// <summary>PitVoice version without the commit hash.</summary>
    public static string CurrentVersion { get; } =
        typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3)
        ?? "?";

    public string Version { get; }

    public IReadOnlyList<LicensedItem> Components { get; }

    public IReadOnlyList<LicensedItem> Models { get; }

    public static string Repository => RepositoryUrl;

    /// <summary>The notices file of this version; a build that was not installed has no tag, so it gets the main branch (design decision 9).</summary>
    public string NoticesUrl { get; }

    /// <summary>The address to copy when no browser opened (spec: Browser cannot be opened).</summary>
    [ObservableProperty]
    public partial string CopyAddress { get; set; } = string.Empty;

    internal static string NoticesAddress(string version, bool installed) =>
        $"{RepositoryUrl}/blob/{(installed ? "v" + version : "main")}/THIRD-PARTY-NOTICES.md";

    /// <summary>True when the installer put this copy in place; <see cref="Program"/> sets the locator at start.</summary>
    private static bool IsInstalled()
    {
        try
        {
            return VelopackLocator.IsCurrentSet && VelopackLocator.Current.CurrentlyInstalledVersion is not null;
        }
#pragma warning disable CA1031 // Not knowing only changes the link to the main branch.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    [RelayCommand]
    private void Open(string url) => CopyAddress = Links.TryOpen(url) ? string.Empty : url;

    internal static IReadOnlyList<LicensedItem> LoadComponents()
    {
        using var stream = typeof(AboutViewModel).Assembly.GetManifestResourceStream("PitVoice.App.ThirdParty.json");
        if (stream is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize<LicensedItem[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
