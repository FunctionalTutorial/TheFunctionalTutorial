using Robust.Shared.Serialization;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// Bound UI key for the tutorial BPL CyberMed surgery facade (hosted on the analyzer).
/// </summary>
[Serializable, NetSerializable]
public enum TutorialCyberMedUiKey : byte
{
    Key,
}

public enum TutorialCyberMedLayer : byte
{
    Skin,
    Tissue,
    Organ,
}

[Serializable, NetSerializable]
public sealed class TutorialCyberMedBuiState : BoundUserInterfaceState
{
    public NetEntity Patient;
    public string PatientName = string.Empty;
    public string SelectedPart = "Torso";
    public TutorialCyberMedLayer SelectedLayer = TutorialCyberMedLayer.Skin;
    public bool SkinOpen;
    public bool TissueOpen;
    public bool OrganInserted;
    public bool ExampleSurgeryComplete;
    public List<string> Parts = new List<string> { "Torso" };
    public List<TutorialCyberMedStepUiData> Steps = new();
}

[Serializable, NetSerializable]
public sealed class TutorialCyberMedStepUiData
{
    public string StepId = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public string ToolLabel = string.Empty;
    public bool Available;
    public bool Completed;
}

[Serializable, NetSerializable]
public sealed class TutorialCyberMedSelectPartBuiMsg : BoundUserInterfaceMessage
{
    public string Part = "Torso";
}

[Serializable, NetSerializable]
public sealed class TutorialCyberMedSelectLayerBuiMsg : BoundUserInterfaceMessage
{
    public TutorialCyberMedLayer Layer;
}

[Serializable, NetSerializable]
public sealed class TutorialCyberMedStepChosenBuiMsg : BoundUserInterfaceMessage
{
    public string StepId = string.Empty;
}
