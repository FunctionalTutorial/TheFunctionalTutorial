using Robust.Shared.GameStates;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TutorialCyberMedToolComponent : Component
{
    [DataField, AutoNetworkedField]
    public TutorialCyberMedToolType ToolType;
}
