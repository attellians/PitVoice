using PitVoice.Host;
using Velopack;

namespace PitVoice.App;

/// <summary>
/// Entry point (design decision 3): the installer's commands come first — before uninstall the user's data goes,
/// then the process ends — and only then the app itself starts.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main()
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                foreach (var failure in UninstallCleanup.Default.Run())
                {
                    Console.Error.WriteLine("not removed: " + failure);
                }
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
