using Robust.Shared.GameStates;

namespace Content.Shared._Forge.WorldTime;

/// <summary>
/// Marks the map whose day/night phase is used as the round-wide time of day.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WorldTimeSourceComponent : Component
{
}
