using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// A staged effect that fires when the player reaches a named sub-goal, so a curriculum can put
/// something in the world on cue rather than only asking the player for things.
/// </summary>
[RegisterComponent]
public sealed partial class TutorialCueComponent : Component
{
    /// <summary>Sub-goal id that sets this off when it becomes current.</summary>
    [DataField(required: true)]
    public string SubGoalId = string.Empty;

    /// <summary>
    /// Fire this long after the sub-goal starts. With <see cref="AfterLine"/> set it is only the
    /// backstop for a beat the coach never speaks: while she is working toward that line it is
    /// pushed back, so it can never go off partway through what it was meant to punctuate.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.Zero;

    /// <summary>
    /// Fire once the coach has spoken this many lines of <see cref="SubGoalId"/> (one-based), so
    /// rewording her script moves the effect with it instead of stranding it on the wrong line.
    /// </summary>
    [DataField]
    public int? AfterLine;

    /// <summary>Beat between <see cref="AfterLine"/> and the effect; small, so they read as one moment.</summary>
    [DataField]
    public TimeSpan LineDelay = TimeSpan.Zero;

    [DataField]
    public TutorialCueEffect Effect = TutorialCueEffect.Breach;

    /// <summary>Radius in tiles for the lighting effects. Sized to one chamber, not through a wall.</summary>
    [DataField]
    public float Radius = 8f;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>Cosmetic entity spawned where the cue fires.</summary>
    [DataField]
    public EntProtoId? Spawn;

    /// <summary>
    /// Charge <see cref="TutorialCueEffect.Breach"/> sets off. Defaults are C4's, enough to take
    /// out the window it is placed against and not much else.
    /// </summary>
    [DataField]
    public string ExplosionType = "DemolitionCharge";

    /// <inheritdoc cref="ExplosionType"/>
    [DataField]
    public float TotalIntensity = 60f;

    /// <inheritdoc cref="ExplosionType"/>
    [DataField]
    public float IntensitySlope = 5f;

    /// <inheritdoc cref="ExplosionType"/>
    [DataField]
    public float MaxIntensity = 30f;

    /// <summary>Set once this has gone off, so walking back into the chamber cannot repeat it.</summary>
    [ViewVariables]
    public bool Fired;

    /// <summary>When the armed cue goes off. Null while it is waiting for its sub-goal.</summary>
    [ViewVariables]
    public TimeSpan? FireAt;

    /// <summary>Participant who armed it, so the effect can be aimed at them.</summary>
    [ViewVariables]
    public EntityUid? ArmedBy;

    /// <summary>Set once <see cref="AfterLine"/> has pulled <see cref="FireAt"/> onto that line.</summary>
    [ViewVariables]
    public bool CuedOnLine;
}

[Serializable, NetSerializable]
public enum TutorialCueEffect : byte
{
    /// <summary>Kill every powered light in range, on the same grid.</summary>
    LightsOff,

    /// <summary>Bring them back.</summary>
    LightsOn,

    /// <summary>Blow this entity up. Used on a hull panel with space behind it, so the chamber vents.</summary>
    Breach,
}
