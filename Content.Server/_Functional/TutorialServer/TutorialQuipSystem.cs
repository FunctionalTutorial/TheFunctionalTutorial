using Content.Shared._Functional.TutorialServer;
using Content.Shared.Nutrition;
using Content.Server.Tools;
using Content.Shared.Tools.Components;
using Robust.Shared.Containers;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Lets the coach remark on things never asked for.
/// </summary>
/// <remarks>
/// Quips hang off the prop they are about wherever there is one to hang them off, so the remark
/// lives with the thing that provokes it. Where there is no prop — a floor tile is not an entity a
/// mapper places — they hang off the coach instead, which is who says them either way.
/// </remarks>
public sealed class TutorialQuipSystem : EntitySystem
{
    [Dependency] private TutorialServerRuleSystem _tutorial = default!;
    [Dependency] private TutorialTrainerSystem _trainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TutorialQuipComponent, IngestedEvent>(OnIngested);
        SubscribeLocalEvent<TutorialQuipComponent, EntInsertedIntoContainerMessage>(OnInserted);

        SubscribeLocalEvent<ToolComponent, TileToolDoAfterEvent>(
            OnTilePried,
            after: [typeof(ToolSystem)]);
    }

    private void OnIngested(Entity<TutorialQuipComponent> ent, ref IngestedEvent args)
    {
        TrySpeak(ent, args.User, TutorialQuipTrigger.Ingested);
    }

    private void OnInserted(Entity<TutorialQuipComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!HasComp<TutorialParticipantComponent>(args.Entity))
            return;

        TrySpeak(ent, args.Entity, TutorialQuipTrigger.PlayerInserted);
    }

    private void OnTilePried(Entity<ToolComponent> ent, ref TileToolDoAfterEvent args)
    {
        if (!args.Handled || args.Cancelled)
            return;

        TrySpeakAsCoach(args.User, TutorialQuipTrigger.TilePried);
    }

    /// <summary>
    /// Speaks a quip the coach carries herself, for a trigger with no prop behind it.
    /// </summary>
    private void TrySpeakAsCoach(EntityUid player, TutorialQuipTrigger trigger)
    {
        if (!TryResolveMentor(player, out var mentor))
            return;

        if (!TryComp<TutorialQuipComponent>(mentor, out var quips))
            return;

        Speak((mentor, quips), mentor, player, trigger);
    }

    private void TrySpeak(Entity<TutorialQuipComponent> ent, EntityUid player, TutorialQuipTrigger trigger)
    {
        if (!TryResolveMentor(player, out var mentor))
            return;

        Speak(ent, mentor, player, trigger);
    }

    private void Speak(
        Entity<TutorialQuipComponent> ent,
        EntityUid mentor,
        EntityUid player,
        TutorialQuipTrigger trigger)
    {
        foreach (var quip in ent.Comp.Quips)
        {
            if (quip.Spoken || quip.Trigger != trigger)
                continue;

            quip.Spoken = true;
            _trainer.TrySpeakInterjection(mentor, player, quip.Line);
        }
    }

    /// <summary>The coach this participant is being taught by, if they are in a session at all.</summary>
    private bool TryResolveMentor(EntityUid player, out EntityUid mentor)
    {
        mentor = EntityUid.Invalid;

        if (!HasComp<TutorialParticipantComponent>(player))
            return false;

        if (!_tutorial.TryGetSession(player, out var session))
            return false;

        mentor = session.MentorUid;
        return mentor != EntityUid.Invalid && !TerminatingOrDeleted(mentor);
    }
}
