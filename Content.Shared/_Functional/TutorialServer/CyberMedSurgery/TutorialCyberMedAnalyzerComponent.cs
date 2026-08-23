using Content.Shared._Functional.TutorialServer;
using Robust.Shared.GameStates;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// Tutorial-only CyberMed analyzer that hosts the BPL-style surgery Bound UI.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TutorialCyberMedAnalyzerComponent : Component
{
    [DataField, AutoNetworkedField]
    public string RequiredRoleId = TutorialSurgeryRoleLock.CyberMedRoleId;

    /// <summary>Patient currently scanned into the surgery UI.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? ScannedPatient;

    [DataField, AutoNetworkedField]
    public string SelectedPart = "Torso";

    [DataField, AutoNetworkedField]
    public TutorialCyberMedLayer SelectedLayer = TutorialCyberMedLayer.Skin;
}
