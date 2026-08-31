using Content.Shared._Functional.TutorialServer;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Player;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Activates HoP-line visitors when the matching sub-goal is current: speak + drop an ID on the desk.
/// </summary>
public sealed class TutorialHoPQueueSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TutorialServerRuleSystem _tutorial = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var visitors = EntityQueryEnumerator<TutorialHoPVisitorComponent, TransformComponent>();
        while (visitors.MoveNext(out var visitorUid, out var visitor, out var visitorXform))
        {
            if (visitor.Activated)
                continue;

            if (TryComp<MobStateComponent>(visitorUid, out var mobState) &&
                mobState.CurrentState is MobState.Dead or MobState.Critical)
                continue;

            var mapUid = visitorXform.MapUid;
            if (mapUid == null)
                continue;

            var participants = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
            while (participants.MoveNext(out var playerUid, out var part, out var playerXform))
            {
                if (playerXform.MapUid != mapUid)
                    continue;

                if (!_tutorial.TryGetCurrentSubGoal(playerUid, part, out var sub))
                    continue;

                if (!string.Equals(sub.Id, visitor.ActivateOnSubGoal, StringComparison.Ordinal))
                    continue;

                ActivateVisitor(visitorUid, visitor, playerUid);
                break;
            }
        }
    }

    private void ActivateVisitor(EntityUid visitorUid, TutorialHoPVisitorComponent visitor, EntityUid playerUid)
    {
        visitor.Activated = true;
        Dirty(visitorUid, visitor);

        if (!string.IsNullOrWhiteSpace(visitor.Dialogue) &&
            TryComp<ActorComponent>(playerUid, out var actor))
        {
            RaiseNetworkEvent(
                new TutorialCoachSpeechEvent
                {
                    Speaker = GetNetEntity(visitorUid),
                    LocId = visitor.Dialogue,
                },
                actor.PlayerSession.Channel);
        }

        var dropCoords = _transform.GetMoverCoordinates(visitorUid).Offset(visitor.DeskDropOffset);
        Spawn(visitor.IdCardProto, dropCoords);
    }
}
