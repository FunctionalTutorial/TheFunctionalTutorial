using Content.Shared.Changeling.Components;

namespace Content.Shared.Changeling.Systems;

/// <summary>
/// Tutorial-server helpers with Access to <see cref="ChangelingDevourComponent"/>.
/// </summary>
public sealed partial class ChangelingDevourSystem
{
    /// <summary>
    /// Ensures devour component + action after MapInit (tutorial antag bootstrap).
    /// </summary>
    public void EnsureTutorialDevour(EntityUid uid)
    {
        var devour = EnsureComp<ChangelingDevourComponent>(uid);
        if (devour.ChangelingDevourActionEntity == null)
            _actionsSystem.AddAction(uid, ref devour.ChangelingDevourActionEntity, devour.ChangelingDevourAction);
    }
}
