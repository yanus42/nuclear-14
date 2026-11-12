using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Audio.Jukebox;

public sealed partial class JukeboxComponent
{
    public const byte MaxVolumePercent = 100;
    public const int MaxQueueLength = 100;

    [DataField, AutoNetworkedField]
    public byte VolumePercent = MaxVolumePercent;

    [DataField, AutoNetworkedField]
    public List<ProtoId<JukeboxPrototype>> Queue = new();

    [AutoNetworkedField]
    public int QueueRevision;

    public byte? PendingVolumePercent;
    public TimeSpan NextVolumeChange;
    public TimeSpan TrackDuration;
}

[Serializable, NetSerializable]
public sealed class JukeboxSetVolumeMessage(byte volumePercent) : BoundUserInterfaceMessage
{
    public byte VolumePercent { get; } = volumePercent;
}

[Serializable, NetSerializable]
public sealed class JukeboxEnqueueMessage(ProtoId<JukeboxPrototype> song) : BoundUserInterfaceMessage
{
    public ProtoId<JukeboxPrototype> Song { get; } = song;
}

[Serializable, NetSerializable]
public sealed class JukeboxEditQueueMessage(int index, int direction, int revision) : BoundUserInterfaceMessage
{
    public int Index { get; } = index;
    // Zero removes the entry. Minus/plus one moves it within the queue.
    public int Direction { get; } = direction;
    public int Revision { get; } = revision;
}

[Serializable, NetSerializable]
public sealed class JukeboxNextMessage : BoundUserInterfaceMessage;
