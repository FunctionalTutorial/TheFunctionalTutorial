using Content.Shared._Functional.TutorialServer.CyberMedSurgery;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// Client Bound UI for the BPL CyberMed surgery facade (analyzer-hosted).
/// </summary>
[UsedImplicitly]
public sealed partial class TutorialCyberMedBoundUserInterface : BoundUserInterface
{
    private TutorialCyberMedSurgeryWindow? _window;

    public TutorialCyberMedBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TutorialCyberMedSurgeryWindow>();
        _window.OnPartSelected += part => SendMessage(new TutorialCyberMedSelectPartBuiMsg { Part = part });
        _window.OnLayerSelected += layer => SendMessage(new TutorialCyberMedSelectLayerBuiMsg { Layer = layer });
        _window.OnStepSelected += stepId => SendMessage(new TutorialCyberMedStepChosenBuiMsg { StepId = stepId });
        UpdateState(State);
    }

    protected override void UpdateState(BoundUserInterfaceState? state)
    {
        if (_window == null || state is not TutorialCyberMedBuiState s)
            return;

        _window.Populate(s);
    }
}
