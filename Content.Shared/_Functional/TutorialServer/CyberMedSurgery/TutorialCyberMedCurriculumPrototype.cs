using Robust.Shared.Prototypes;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// Ordered CyberMed facade steps for a body part / layer (BPL Skin→Tissue→Organ flow).
/// </summary>
[Prototype]
public sealed partial class TutorialCyberMedCurriculumPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public string Part = "Torso";

    [DataField(required: true)]
    public List<TutorialCyberMedStepData> SkinOpen = new();

    [DataField(required: true)]
    public List<TutorialCyberMedStepData> TissueOpen = new();

    [DataField(required: true)]
    public List<TutorialCyberMedStepData> Organ = new();

    [DataField(required: true)]
    public List<TutorialCyberMedStepData> TissueClose = new();

    [DataField(required: true)]
    public List<TutorialCyberMedStepData> SkinClose = new();
}

[DataDefinition]
public sealed partial class TutorialCyberMedStepData
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField(required: true)]
    public TutorialCyberMedToolType Tool;

    [DataField]
    public float Duration = 0.8f;
}
