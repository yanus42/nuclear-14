using Robust.Shared.Audio;

namespace Content.Server._Forge.Radio;

/// <summary>
/// Allows a tabletop radio to play music received on its normal speech frequency.
/// </summary>
[RegisterComponent]
public sealed partial class StudioRadioReceiverComponent : Component
{
    [DataField]
    public AudioParams MusicParams = AudioParams.Default.WithVolume(-6f).WithMaxDistance(8f);

    public EntityUid? SourceAudio;
    public EntityUid? ReceiverAudio;
    public EntityUid? Listener;
    public TimeSpan AudioStart;
    public TimeSpan? PauseTime;
}
