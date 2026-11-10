namespace Content.Server._Forge.Radio;

[RegisterComponent]
public sealed partial class StudioRadioTuningComponent : Component
{
    /// <summary>
    /// Exposes the station selector. The device's prototype still supplies its initial tuning.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool CanTune = true;
}
