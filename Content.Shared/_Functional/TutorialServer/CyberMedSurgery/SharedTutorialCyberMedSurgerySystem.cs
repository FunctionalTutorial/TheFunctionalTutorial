using System.Linq;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Shared._Functional.TutorialServer.CyberMedSurgery;

public abstract partial class SharedTutorialCyberMedSurgerySystem : EntitySystem
{
    [Dependency] protected IPrototypeManager Proto = default!;
    [Dependency] protected SharedHandsSystem Hands = default!;

    public const string DefaultCurriculumId = "TutorialCyberMedHeartImplant";

    public bool TryFindHeldTool(EntityUid surgeon, TutorialCyberMedToolType toolType, out EntityUid tool)
    {
        foreach (var held in Hands.EnumerateHeld(surgeon))
        {
            if (!TryComp<TutorialCyberMedToolComponent>(held, out var toolComp))
                continue;

            if (toolComp.ToolType != toolType)
                continue;

            if (toolType == TutorialCyberMedToolType.CyberHeart &&
                !HasComp<TutorialCyberMedCyberHeartComponent>(held))
                continue;

            tool = held;
            return true;
        }

        tool = default;
        return false;
    }

    public bool TryGetCurriculum(string part, out TutorialCyberMedCurriculumPrototype curriculum)
    {
        foreach (var proto in Proto.EnumeratePrototypes<TutorialCyberMedCurriculumPrototype>())
        {
            if (proto.Part != part)
                continue;

            curriculum = proto;
            return true;
        }

        curriculum = default!;
        return false;
    }

    public bool IsSkinOpen(TutorialCyberMedSurgeryTargetComponent target, TutorialCyberMedCurriculumPrototype curriculum)
        => curriculum.SkinOpen.Count > 0 && curriculum.SkinOpen.All(s => target.CompletedSteps.Contains(s.Id));

    public bool IsTissueOpen(TutorialCyberMedSurgeryTargetComponent target, TutorialCyberMedCurriculumPrototype curriculum)
        => curriculum.TissueOpen.Count > 0 && curriculum.TissueOpen.All(s => target.CompletedSteps.Contains(s.Id));

    public List<(TutorialCyberMedStepData Step, bool Available, bool Completed)> BuildLayerSteps(
        TutorialCyberMedSurgeryTargetComponent target,
        TutorialCyberMedCurriculumPrototype curriculum,
        TutorialCyberMedLayer layer)
    {
        var result = new List<(TutorialCyberMedStepData, bool, bool)>();
        var skinOpen = IsSkinOpen(target, curriculum);
        var tissueOpen = IsTissueOpen(target, curriculum);

        switch (layer)
        {
            case TutorialCyberMedLayer.Skin:
                AddOpenOrClose(result, target, curriculum.SkinOpen, curriculum.SkinClose, unlocked: true);
                break;
            case TutorialCyberMedLayer.Tissue:
                AddOpenOrClose(result, target, curriculum.TissueOpen, curriculum.TissueClose, unlocked: skinOpen);
                break;
            case TutorialCyberMedLayer.Organ:
                if (!tissueOpen)
                    break;

                for (var i = 0; i < curriculum.Organ.Count; i++)
                {
                    var step = curriculum.Organ[i];
                    var completed = target.CompletedSteps.Contains(step.Id);
                    var prevDone = i == 0 || target.CompletedSteps.Contains(curriculum.Organ[i - 1].Id);
                    result.Add((step, !completed && prevDone, completed));
                }

                break;
        }

        return result;
    }

    private static void AddOpenOrClose(
        List<(TutorialCyberMedStepData, bool, bool)> result,
        TutorialCyberMedSurgeryTargetComponent target,
        List<TutorialCyberMedStepData> open,
        List<TutorialCyberMedStepData> close,
        bool unlocked)
    {
        var openDone = open.Count > 0 && open.All(s => target.CompletedSteps.Contains(s.Id));

        if (!openDone)
        {
            for (var i = 0; i < open.Count; i++)
            {
                var step = open[i];
                var completed = target.CompletedSteps.Contains(step.Id);
                var prevDone = i == 0 || target.CompletedSteps.Contains(open[i - 1].Id);
                result.Add((step, unlocked && !completed && prevDone, completed));
            }

            return;
        }

        // Show close steps (reverse order of closing list as sequential).
        for (var i = 0; i < close.Count; i++)
        {
            var step = close[i];
            var completed = target.CompletedSteps.Contains(step.Id);
            var prevDone = i == 0 || target.CompletedSteps.Contains(close[i - 1].Id);
            // Closing tissue/skin only after organ implant for the tutorial example.
            var organDone = target.HasCyberHeart;
            result.Add((step, organDone && !completed && prevDone, completed));
        }
    }
}
