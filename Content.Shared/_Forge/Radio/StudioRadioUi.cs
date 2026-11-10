using System.Collections.Immutable;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Radio;

public static class StudioRadioChannels
{
    public static ImmutableArray<ProtoId<RadioChannelPrototype>> Public { get; } =
        ImmutableArray.Create<ProtoId<RadioChannelPrototype>>("PBS", "Wasteland", "Bazar");
}

[Serializable, NetSerializable]
public enum StudioRadioUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class StudioRadioUiState(string channel, bool transmitter) : BoundUserInterfaceState
{
    public string Channel { get; } = channel;
    public bool Transmitter { get; } = transmitter;
}

[Serializable, NetSerializable]
public sealed class StudioRadioSelectChannelMessage(ProtoId<RadioChannelPrototype> channel) : BoundUserInterfaceMessage
{
    public ProtoId<RadioChannelPrototype> Channel { get; } = channel;
}
