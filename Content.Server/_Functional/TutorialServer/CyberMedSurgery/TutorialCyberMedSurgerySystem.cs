using System.Linq;
using Content.Server.Popups;
using Content.Shared._Functional.TutorialServer;
using Content.Shared._Functional.TutorialServer.CyberMedSurgery;
using Content.Shared.DoAfter;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._Functional.TutorialServer.CyberMedSurgery;

/// <summary>
/// BPL CyberMed surgery facade: scan patient with tutorial analyzer → Skin/Tissue/Organ steps.
/// Hard-locked to <see cref="TutorialSurgeryRoleLock.CyberMedRoleId"/>.
/// </summary>
public sealed partial class TutorialCyberMedSurgerySystem : SharedTutorialCyberMedSurgerySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Open only via UtilityVerb so wrong-role players never see a selectable option.
        SubscribeLocalEvent<TutorialCyberMedAnalyzerComponent, GetVerbsEvent<UtilityVerb>>(OnAnalyzerGetVerbs);
        SubscribeLocalEvent<TutorialCyberMedSurgeryTargetComponent, TutorialCyberMedDoAfterEvent>(OnDoAfter);

        Subs.BuiEvents<TutorialCyberMedAnalyzerComponent>(TutorialCyberMedUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnAnalyzerUiOpened);
            subs.Event<TutorialCyberMedSelectPartBuiMsg>(OnSelectPart);
            subs.Event<TutorialCyberMedSelectLayerBuiMsg>(OnSelectLayer);
            subs.Event<TutorialCyberMedStepChosenBuiMsg>(OnStepChosen);
        });
    }

    private void OnAnalyzerUiOpened(Entity<TutorialCyberMedAnalyzerComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!TryComp<TutorialParticipantComponent>(args.Actor, out var part))
            return;

        if (part.StepComplete != TutorialStepComplete.CyberMedSurgeryUiOpened)
            return;

        _tutorial.AdvanceSubGoal(args.Actor);
    }

    private void OnAnalyzerGetVerbs(Entity<TutorialCyberMedAnalyzerComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Target == args.User)
            return;

        if (!TryComp<TutorialCyberMedSurgeryTargetComponent>(args.Target, out var surgeryTarget))
            return;

        // Verb is omitted entirely for the wrong tutorial role.
        if (!TutorialSurgeryRoleLock.IsInTutorialRole(EntityManager, args.User, ent.Comp.RequiredRoleId) ||
            surgeryTarget.RequiredRoleId != ent.Comp.RequiredRoleId)
            return;

        var analyzer = ent;
        var target = args.Target;
        var user = args.User;
        args.Verbs.Add(new UtilityVerb
        {
            Act = () => OpenAnalyzerOnPatient(analyzer, user, target, surgeryTarget),
            Text = Loc.GetString("tutorial-cybermed-surgery-verb"),
            Message = Loc.GetString("tutorial-cybermed-surgery-verb-message"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/settings.svg.192dpi.png")),
            IconEntity = GetNetEntity(ent),
            DoContactInteraction = true,
        });
    }

    private void OpenAnalyzerOnPatient(
        Entity<TutorialCyberMedAnalyzerComponent> analyzer,
        EntityUid user,
        EntityUid patient,
        TutorialCyberMedSurgeryTargetComponent surgeryTarget)
    {
        analyzer.Comp.ScannedPatient = patient;
        analyzer.Comp.SelectedPart = surgeryTarget.Parts.FirstOrDefault() ?? "Torso";
        analyzer.Comp.SelectedLayer = TutorialCyberMedLayer.Skin;
        Dirty(analyzer);

        _ui.OpenUi(analyzer.Owner, TutorialCyberMedUiKey.Key, user);
        RefreshUI(analyzer);
    }

    private void OnSelectPart(Entity<TutorialCyberMedAnalyzerComponent> ent, ref TutorialCyberMedSelectPartBuiMsg args)
    {
        if (!EnsureRole(ent, args.Actor))
            return;

        if (ent.Comp.ScannedPatient is not { } patient ||
            !TryComp<TutorialCyberMedSurgeryTargetComponent>(patient, out var target) ||
            !target.Parts.Contains(args.Part))
            return;

        ent.Comp.SelectedPart = args.Part;
        ent.Comp.SelectedLayer = TutorialCyberMedLayer.Skin;
        Dirty(ent);
        RefreshUI(ent);
    }

    private void OnSelectLayer(Entity<TutorialCyberMedAnalyzerComponent> ent, ref TutorialCyberMedSelectLayerBuiMsg args)
    {
        if (!EnsureRole(ent, args.Actor))
            return;

        ent.Comp.SelectedLayer = args.Layer;
        Dirty(ent);
        RefreshUI(ent);
    }

    private void OnStepChosen(Entity<TutorialCyberMedAnalyzerComponent> ent, ref TutorialCyberMedStepChosenBuiMsg args)
    {
        var user = args.Actor;
        if (!EnsureRole(ent, user))
            return;

        if (!TryResolveStep(ent, args.StepId, out var patient, out var step))
            return;

        if (!TryFindHeldTool(user, step.Tool, out _))
        {
            _popup.PopupEntity(Loc.GetString("tutorial-cybermed-missing-tool", ("tool", step.Tool.ToString())), user, user);
            return;
        }

        var doAfterArgs = new DoAfterArgs(
            EntityManager,
            user,
            TimeSpan.FromSeconds(step.Duration),
            new TutorialCyberMedDoAfterEvent { StepId = args.StepId },
            patient,
            patient)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = 2.5f,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfter(Entity<TutorialCyberMedSurgeryTargetComponent> patient, ref TutorialCyberMedDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        // Find analyzer scanning this patient for the acting user.
        Entity<TutorialCyberMedAnalyzerComponent>? analyzer = null;
        var query = EntityQueryEnumerator<TutorialCyberMedAnalyzerComponent>();
        while (query.MoveNext(out var uid, out var analyzerComp))
        {
            if (analyzerComp.ScannedPatient != patient.Owner)
                continue;

            analyzer = (uid, analyzerComp);
            break;
        }

        if (analyzer == null)
            return;

        if (!EnsureRole(analyzer.Value, args.User))
            return;

        if (!TryResolveStep(analyzer.Value, args.StepId, out _, out var step) ||
            !TryFindHeldTool(args.User, step.Tool, out var tool))
        {
            RefreshUI(analyzer.Value);
            return;
        }

        args.Handled = true;

        if (step.Tool == TutorialCyberMedToolType.CyberHeart)
        {
            QueueDel(tool);
            patient.Comp.HasCyberHeart = true;
        }

        patient.Comp.CompletedSteps.Add(step.Id);

        if (TryGetCurriculum(analyzer.Value.Comp.SelectedPart, out var curriculum) &&
            patient.Comp.HasCyberHeart &&
            curriculum.SkinClose.Count > 0 &&
            curriculum.SkinClose.All(s => patient.Comp.CompletedSteps.Contains(s.Id)) &&
            curriculum.TissueClose.All(s => patient.Comp.CompletedSteps.Contains(s.Id)))
        {
            patient.Comp.ExampleSurgeryComplete = true;
        }

        Dirty(patient);
        _popup.PopupEntity(Loc.GetString("tutorial-cybermed-step-done", ("step", step.Name)), args.User, args.User);
        RefreshUI(analyzer.Value);
    }

    private bool EnsureRole(Entity<TutorialCyberMedAnalyzerComponent> ent, EntityUid user)
    {
        if (TutorialSurgeryRoleLock.IsInTutorialRole(EntityManager, user, ent.Comp.RequiredRoleId))
            return true;

        _ui.CloseUi(ent.Owner, TutorialCyberMedUiKey.Key, user);
        return false;
    }

    private bool TryResolveStep(
        Entity<TutorialCyberMedAnalyzerComponent> analyzer,
        string stepId,
        out EntityUid patient,
        out TutorialCyberMedStepData step)
    {
        patient = default;
        step = default!;

        if (analyzer.Comp.ScannedPatient is not { } scanned ||
            !TryComp<TutorialCyberMedSurgeryTargetComponent>(scanned, out var target) ||
            !TryGetCurriculum(analyzer.Comp.SelectedPart, out var curriculum))
            return false;

        patient = scanned;
        var layerSteps = BuildLayerSteps(target, curriculum, analyzer.Comp.SelectedLayer);
        foreach (var (candidate, available, _) in layerSteps)
        {
            if (candidate.Id != stepId || !available)
                continue;

            step = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Completes the next matching available step on any layer (integration tests).
    /// </summary>
    public bool TryForceCompleteStep(EntityUid analyzerUid, EntityUid surgeon, string stepId, bool skipToolCheck = false)
    {
        if (!TryComp<TutorialCyberMedAnalyzerComponent>(analyzerUid, out var analyzerComp))
            return false;

        if (analyzerComp.ScannedPatient is not { } patient ||
            !TryComp<TutorialCyberMedSurgeryTargetComponent>(patient, out var target) ||
            !TryGetCurriculum(analyzerComp.SelectedPart, out var curriculum))
            return false;

        TutorialCyberMedStepData? step = null;
        foreach (var layer in new[]
                 {
                     TutorialCyberMedLayer.Skin,
                     TutorialCyberMedLayer.Tissue,
                     TutorialCyberMedLayer.Organ,
                 })
        {
            foreach (var (candidate, available, _) in BuildLayerSteps(target, curriculum, layer))
            {
                if (candidate.Id != stepId || !available)
                    continue;

                step = candidate;
                analyzerComp.SelectedLayer = layer;
                break;
            }

            if (step != null)
                break;
        }

        if (step == null)
            return false;

        if (!skipToolCheck)
        {
            if (!TryFindHeldTool(surgeon, step.Tool, out var tool))
                return false;

            if (step.Tool == TutorialCyberMedToolType.CyberHeart)
                QueueDel(tool);
        }

        if (step.Tool == TutorialCyberMedToolType.CyberHeart)
            target.HasCyberHeart = true;

        target.CompletedSteps.Add(step.Id);

        if (target.HasCyberHeart &&
            curriculum.SkinClose.All(s => target.CompletedSteps.Contains(s.Id)) &&
            curriculum.TissueClose.All(s => target.CompletedSteps.Contains(s.Id)))
        {
            target.ExampleSurgeryComplete = true;
        }

        Dirty(analyzerUid, analyzerComp);
        Dirty(patient, target);
        RefreshUI((analyzerUid, analyzerComp));
        return true;
    }

    public void RefreshUI(Entity<TutorialCyberMedAnalyzerComponent> analyzer)
    {
        if (analyzer.Comp.ScannedPatient is not { } patient ||
            !TryComp<TutorialCyberMedSurgeryTargetComponent>(patient, out var target) ||
            !TryGetCurriculum(analyzer.Comp.SelectedPart, out var curriculum))
        {
            return;
        }

        var steps = BuildLayerSteps(target, curriculum, analyzer.Comp.SelectedLayer)
            .Select(s => new TutorialCyberMedStepUiData
            {
                StepId = s.Step.Id,
                Name = s.Step.Name,
                Description = s.Step.Description,
                ToolLabel = s.Step.Tool.ToString(),
                Available = s.Available,
                Completed = s.Completed,
            })
            .ToList();

        _ui.SetUiState(analyzer.Owner, TutorialCyberMedUiKey.Key, new TutorialCyberMedBuiState
        {
            Patient = GetNetEntity(patient),
            PatientName = Name(patient),
            SelectedPart = analyzer.Comp.SelectedPart,
            SelectedLayer = analyzer.Comp.SelectedLayer,
            SkinOpen = IsSkinOpen(target, curriculum),
            TissueOpen = IsTissueOpen(target, curriculum),
            OrganInserted = target.HasCyberHeart,
            ExampleSurgeryComplete = target.ExampleSurgeryComplete,
            Parts = target.Parts.ToList(),
            Steps = steps,
        });
    }
}
