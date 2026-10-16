using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PitVoice.Models;

namespace PitVoice.App.ViewModels;

/// <summary>A third-party component or a model with its license.</summary>
internal sealed record LicensedItem(string Name, string License, string Url);

/// <summary>"About" (spec: About section): version, license, repository, components and models with their licenses.</summary>
internal sealed partial class AboutViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/attellians/PitVoice";

    public AboutViewModel(AppServices services)
    {
        Version = typeof(AboutViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? typeof(AboutViewModel).Assembly.GetName().Version?.ToString(3)
            ?? "?";
        Components = LoadComponents();
        Models = services.Models.Manifest?.Entries
            .Where(e => e.Type == ModelType.Asr)
            .Select(e => new LicensedItem(e.Id, e.License, e.Source))
            .ToArray() ?? [];
    }

    public string Version { get; }

    public IReadOnlyList<LicensedItem> Components { get; }

    public IReadOnlyList<LicensedItem> Models { get; }

    public static string Repository => RepositoryUrl;

    /// <summary>The address to copy when no browser opened (spec: Browser cannot be opened).</summary>
    [ObservableProperty]
    public partial string CopyAddress { get; set; } = string.Empty;

    [RelayCommand]
    private void Open(string url)
    {
        CopyAddress = string.Empty;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            CopyAddress = url;
        }
    }

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
