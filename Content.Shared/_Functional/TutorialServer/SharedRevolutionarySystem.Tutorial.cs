using Content.Shared.Revolutionary.Components;

namespace Content.Shared.Revolutionary;

/// <summary>
/// Tutorial-server helpers that need Access to revolutionary components.
/// </summary>
public abstract partial class SharedRevolutionarySystem
{
    /// <summary>
    /// Grants Head Revolutionary conversion powers without starting a rev game rule.
    /// </summary>
    public void MakeTutorialHeadRevolutionary(EntityUid uid)
    {
        EnsureComp<RevolutionaryComponent>(uid);
        EnsureComp<HeadRevolutionaryComponent>(uid);
    }
}
