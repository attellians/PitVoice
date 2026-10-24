using PitVoice.Core.Vr;

namespace PitVoice.VrOverlay;

/// <summary>Why a call into OpenVR failed: the exit code of the helper and the word for the <c>error</c> line.</summary>
internal sealed record VrFailure(int ExitCode, string Code);

/// <summary>The part of OpenVR the helper uses; the tests replace it with a fake.</summary>
internal interface IOpenVr
{
    /// <summary>Connects to the running SteamVR without starting it; null on success.</summary>
    VrFailure? Init();

    /// <summary>Creates the hidden overlay of the indicator; null on success.</summary>
    VrFailure? CreateOverlay();

    /// <summary>Applies a frame, the visibility or the place; null on success.</summary>
    VrFailure? Apply(VrMessage message);

    /// <summary>Reads the pending events; true when SteamVR is closing (VREvent_Quit).</summary>
    bool PollQuit();

    /// <summary>Tells SteamVR the helper is exiting, so it does not wait for it.</summary>
    void AcknowledgeQuit();

    /// <summary>Removes the overlay and disconnects; safe after a failed <see cref="Init"/>.</summary>
    void Shutdown();
}
