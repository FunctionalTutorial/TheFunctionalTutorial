using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Antags.Vampires.Components;

/// <summary>
/// Minimal Starlight vampire data for tutorial fangs / drink / class select.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VampireComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? ChosenClassId;

    [DataField]
    public List<EntProtoId> BaseVampireActions = new()
    {
        "ActionVampireToggleFangs",
    };

    [DataField]
    public EntProtoId ClassSelectActionId = "ActionClassSelectId";

    [DataField, AutoNetworkedField]
    public int TotalBlood;

    [DataField, AutoNetworkedField]
    public int DrunkBlood;

    [DataField, AutoNetworkedField]
    public bool FangsExtended;

    /// <summary>Blood granted per successful tutorial drink doafter (shim).</summary>
    [DataField]
    public int TutorialSipBlood = 40;

    [DataField]
    public int ClassSelectThreshold = 150;

    [DataField]
    public float BiteDistanceThreshold = 1.5f;

    [DataField]
    public TimeSpan DrinkDoAfter = TimeSpan.FromSeconds(1.25);

    public bool IsDrinking;
}
