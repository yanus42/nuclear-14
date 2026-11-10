using Content.Server._Forge.Radio;
using Content.Shared.Inventory.Events;

namespace Content.Server._Nuclear14.CartridgeLoader;

public sealed class PipBoyRadioWearerSystem : EntitySystem
{
    [Dependency] private readonly StudioRadioSystem _studio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PipBoyRadioWearerComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<PipBoyRadioWearerComponent, GotUnequippedEvent>(OnUnequipped);
    }

    private void OnEquipped(
        EntityUid uid,
        PipBoyRadioWearerComponent component,
        GotEquippedEvent args)
    {
        component.WearerUid = args.Equipee;
        _studio.RefreshReceiver(uid);
    }

    private void OnUnequipped(
        EntityUid uid,
        PipBoyRadioWearerComponent component,
        GotUnequippedEvent args)
    {
        component.WearerUid = null;
        _studio.RefreshReceiver(uid);
    }
}
