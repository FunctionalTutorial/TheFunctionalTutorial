using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>
/// Marks a mindless practice body for cuff / heal tutorial sensors.
/// Optional <see cref="SpawnDamage"/> is applied when spawned via practice kits.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TutorialPracticeMobComponent : Component
{
    /// <summary>
    /// Damage applied once after the entity is spawned into a tutorial map.
    /// </summary>
    [DataField]
    public DamageSpecifier SpawnDamage = new();

    /// <summary>
    /// Set after spawn damage has been applied so it is not reapplied.
    /// </summary>
    [DataField]
    public bool SpawnDamageApplied;
}
