using Robust.Shared.GameStates;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// Marker on the held cybernetic heart for the Insert Organ step.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TutorialCyberMedCyberHeartComponent : Component;
