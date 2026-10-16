using PitVoice.App.ViewModels;
using Xunit;

namespace PitVoice.App.Tests;

/// <summary>"About" lists third-party components from the embedded resource (spec: About section).</summary>
public sealed class AboutTests
{
    [Fact]
    public void Components_list_has_names_licenses_and_links()
    {
        var components = AboutViewModel.LoadComponents();

        Assert.Contains(components, c => c.Name == "Vosk" && c.License == "Apache-2.0");
        Assert.Contains(components, c => c.Name == "WPF-UI" && c.License == "MIT");
        Assert.Contains(components, c => c.Name == "Serilog" && c.License == "Apache-2.0");
        Assert.All(components, c => Assert.StartsWith("https://", c.Url, StringComparison.Ordinal));
    }
}
