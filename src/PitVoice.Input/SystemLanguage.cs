using System.Runtime.InteropServices;

namespace PitVoice.Input;

/// <summary>
/// Default language of the settings from the Windows display language. The build uses invariant globalization, so
/// <c>CultureInfo</c> cannot tell; the Win32 call can (design decision 10).
/// </summary>
public static partial class SystemLanguage
{
    private const int PrimaryLanguageMask = 0x3FF;
    private const int LangRussian = 0x19;

    /// <summary><c>ru</c> when Windows is in Russian, otherwise <c>en</c>; <c>en</c> outside Windows.</summary>
    public static string Detect() => OperatingSystem.IsWindows() ? FromLangId(GetUserDefaultUILanguage()) : "en";

    /// <summary>Maps a Windows LANGID to a pack language by its primary language.</summary>
    public static string FromLangId(ushort langId) => (langId & PrimaryLanguageMask) == LangRussian ? "ru" : "en";

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
