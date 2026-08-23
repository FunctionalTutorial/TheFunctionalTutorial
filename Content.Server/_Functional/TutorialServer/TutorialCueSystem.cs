using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Light.EntitySystems;
using Content.Shared._Functional.TutorialServer;
using Content.Shared.Camera;
using Content.Shared.Light.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Functional.TutorialServer;

/// <summary>
/// Fires staged effects when a participant reaches the sub-goal that cues them. Placed in the map
/// rather than the curriculum, so the effect stays with the room it happens to.
/// </summary>
public sealed class TutorialCueSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedCameraRecoilSystem _recoil = default!;
    [Dependency] private ExplosionSystem _explosions = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PoweredLightSystem _lights = default!;
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private TutorialServerRuleSystem _tutorial = default!;
    [Dependency] private TutorialTrainerSystem _trainer = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Enough to feel structural, not enough to lose the player's cursor.</summary>
    private static readonly Vector2 BreachKick = new(0f, -0.6f);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var armed = EntityQueryEnumerator<TutorialCueComponent>();
        while (armed.MoveNext(out var uid, out var cue))
        {
            if (cue.Fired || cue.FireAt is not { } fireAt || now < fireAt)
                continue;

            cue.Fired = true;
            cue.FireAt = null;
            Fire((uid, cue));
        }

        TryArm(now);
    }

    /// <summary>
    /// Arms any cue whose sub-goal the player has just reached, and pulls an armed one onto the
    /// coach's line once she reaches it. Polled, because only one system may hold a directed
    /// subscription to <c>TutorialParticipantProgressChangedEvent</c> and the guide already does.
    /// </summary>
    private void TryArm(TimeSpan now)
    {
        var pending = EntityQueryEnumerator<TutorialCueComponent, TransformComponent>();
        while (pending.MoveNext(out var uid, out var cue, out var xform))
        {
            if (cue.Fired)
                continue;

            if (cue.FireAt == null)
            {
                Arm(now, (uid, cue), xform);
                continue;
            }

            TryTakeTheBeatFromTheCoach(now, (uid, cue));
        }
    }

    private void Arm(TimeSpan now, Entity<TutorialCueComponent> cue, TransformComponent xform)
    {
        var players = EntityQueryEnumerator<TutorialParticipantComponent, TransformComponent>();
        while (players.MoveNext(out var player, out var part, out var playerXform))
        {
            // Grid, not map: one player must not set off another player's copy of the facility.
            if (playerXform.GridUid != xform.GridUid)
                continue;

            if (!_tutorial.TryGetCurrentSubGoal(player, part, out var sub) || sub.Id != cue.Comp.SubGoalId)
                continue;

            cue.Comp.FireAt = now + cue.Comp.Delay;
            cue.Comp.ArmedBy = player;
            break;
        }
    }

    /// <summary>
    /// Moves an armed cue onto the line it was written for, and keeps the backstop out of the way
    /// until she gets there.
    /// </summary>
    private void TryTakeTheBeatFromTheCoach(TimeSpan now, Entity<TutorialCueComponent> cue)
    {
        if (cue.Comp.CuedOnLine || cue.Comp.AfterLine is not { } afterLine)
            return;

        if (cue.Comp.ArmedBy is not { } player || TerminatingOrDeleted(player))
            return;

        if (!_trainer.TryGetLinesSpoken(player, cue.Comp.SubGoalId, out var spoken))
            return;

        if (spoken < afterLine)
        {
            cue.Comp.FireAt = now + cue.Comp.Delay;
            return;
        }

        cue.Comp.CuedOnLine = true;

        var onCue = now + cue.Comp.LineDelay;
        if (onCue < cue.Comp.FireAt)
            cue.Comp.FireAt = onCue;
    }

    private void Fire(Entity<TutorialCueComponent> cue)
    {
        switch (cue.Comp.Effect)
        {
            case TutorialCueEffect.LightsOff:
                SetLightsInRange(cue, false);
                break;
            case TutorialCueEffect.LightsOn:
                SetLightsInRange(cue, true);
                break;
            case TutorialCueEffect.Breach:
                Breach(cue);
                break;
        }
    }

    /// <summary>
    /// Darkens or relights every fixture in range on this grid. Both component types, because
    /// fixtures like <c>AlwaysPoweredWallLight</c> carry a bare <c>PointLight</c> and no
    /// <c>PoweredLight</c> to switch.
    /// </summary>
    private void SetLightsInRange(Entity<TutorialCueComponent> cue, bool state)
    {
        PlayEffects(cue);

        var xform = Transform(cue);
        var origin = _transform.GetWorldPosition(xform);
        var radiusSquared = cue.Comp.Radius * cue.Comp.Radius;

        var powered = EntityQueryEnumerator<PoweredLightComponent, TransformComponent>();
        while (powered.MoveNext(out var uid, out var light, out var lightXform))
        {
            if (!InRange(lightXform, xform.GridUid, origin, radiusSquared))
                continue;

            _lights.SetState(uid, state, light);
        }

        var points = EntityQueryEnumerator<PointLightComponent, TransformComponent>();
        while (points.MoveNext(out var uid, out var point, out var lightXform))
        {
            if (!InRange(lightXform, xform.GridUid, origin, radiusSquared))
                continue;

            _pointLight.SetEnabled(uid, state, point);
        }
    }

    private bool InRange(TransformComponent lightXform, EntityUid? gridUid, Vector2 origin, float radiusSquared)
    {
        if (lightXform.GridUid != gridUid)
            return false;

        return Vector2.DistanceSquared(origin, _transform.GetWorldPosition(lightXform)) <= radiusSquared;
    }

    /// <summary>
    /// Sets off a real charge where the cue sits, so the hull is destroyed by the explosion system
    /// rather than deleted out from under it. <c>canCreateVacuum: false</c> keeps the floor intact,
    /// so the room vents through the hole where the window was and not through a pit of its own.
    /// </summary>
    private void Breach(Entity<TutorialCueComponent> cue)
    {
        PlayEffects(cue);

        if (cue.Comp.ArmedBy is { } player && !TerminatingOrDeleted(player))
            _recoil.KickCamera(player, BreachKick);

        _explosions.QueueExplosion(
            cue.Owner,
            cue.Comp.ExplosionType,
            cue.Comp.TotalIntensity,
            cue.Comp.IntensitySlope,
            cue.Comp.MaxIntensity,
            canCreateVacuum: false);

        QueueDel(cue);
    }

    private void PlayEffects(Entity<TutorialCueComponent> cue)
    {
        if (cue.Comp.Sound is { } sound)
            _audio.PlayPvs(sound, cue);

        if (cue.Comp.Spawn is { } spawn)
            Spawn(spawn, Transform(cue).Coordinates);
    }
}
