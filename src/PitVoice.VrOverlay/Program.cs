using PitVoice.Core.Vr;
using PitVoice.VrOverlay;

// The tray app starts this process only while SteamVR runs and talks to it over the standard streams (add-vr-overlay D1,
// D6). "--demo" shows a test picture in the headset without the app, until Enter (task 3.3, the stand without a headset).
try
{
    if (args is ["--demo"])
    {
        Console.Error.WriteLine("PitVoice VR helper demo: a test picture at the default place; Enter ends it.");
        using var demo = DemoInput.Create();
        return HelperLoop.Run(new OpenVrApi(), demo, Console.Out);
    }

    using var input = Console.OpenStandardInput();
    return HelperLoop.Run(new OpenVrApi(), input, Console.Out);
}
#pragma warning disable CA1031 // Any failure ends the helper with its own code; the app restarts it (D8).
catch (Exception e)
#pragma warning restore CA1031
{
    Console.Out.WriteLine(VrReply.Error("crash"));
    Console.Error.WriteLine(e);
    return VrExitCode.Crashed;
}
