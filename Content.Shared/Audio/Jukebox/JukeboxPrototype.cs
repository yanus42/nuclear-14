using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared.Audio.Jukebox;

/// <summary>
/// Soundtrack that's visible on the jukebox list.
/// </summary>
[Prototype]
public sealed partial class JukeboxPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; set; } = string.Empty;

    /// <summary>
    /// User friendly name to use in UI.
    /// </summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField(required: true)]
    public SoundPathSpecifier Path = default!;

    // Forge-Change-Start
    /// <summary>
    /// Mono recording for the jukebox and nearby radios when the original track is stereo.
    /// </summary>
    [DataField]
    public SoundPathSpecifier? MonoPath;

    public SoundPathSpecifier PositionalPath => MonoPath ?? Path;
    // Forge-Change-End
}
