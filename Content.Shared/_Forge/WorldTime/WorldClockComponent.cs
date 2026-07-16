using Robust.Shared.GameStates;

namespace Content.Shared._Forge.WorldTime;

/// <summary>
/// Makes an entity display the current time of day when examined.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WorldClockComponent : Component
{
}
