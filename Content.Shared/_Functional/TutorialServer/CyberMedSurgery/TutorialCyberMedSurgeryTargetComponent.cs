using Content.Shared._Functional.TutorialServer;
using Robust.Shared.GameStates;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// Practice patient for the BPL CyberMed surgery tutorial facade.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TutorialCyberMedSurgeryTargetComponent : Component
{
    [DataField, AutoNetworkedField]
    public string RequiredRoleId = TutorialSurgeryRoleLock.CyberMedRoleId;

    [DataField, AutoNetworkedField]
    public List<string> Parts = new List<string> { "Torso" };

    [DataField, AutoNetworkedField]
    public HashSet<string> CompletedSteps = new();

    [DataField, AutoNetworkedField]
    public bool HasCyberHeart;

    [DataField, AutoNetworkedField]
    public bool ExampleSurgeryComplete;
}
