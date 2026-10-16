using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace PitVoice.App.ViewModels;

/// <summary>Links open in the default browser, only on the user's click (spec: About section).</summary>
internal static class Links
{
    /// <summary>False when no browser opened; the window then shows the address to copy (spec: Browser cannot be opened).</summary>
    public static bool TryOpen(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}
