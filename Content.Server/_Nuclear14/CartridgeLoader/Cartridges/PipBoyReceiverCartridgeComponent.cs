/// Forge-Change-Start
using Content.Shared._Forge.Radio;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._Nuclear14.CartridgeLoader.Cartridges;

[RegisterComponent]
public sealed partial class PipBoyReceiverCartridgeComponent : Component
{
    [DataField]
    public List<ProtoId<RadioChannelPrototype>> Channels = new(StudioRadioChannels.Public);
}
/// Forge-Change-End
