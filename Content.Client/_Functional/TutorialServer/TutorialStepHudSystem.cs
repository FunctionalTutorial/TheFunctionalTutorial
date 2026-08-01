namespace Content.Client._Functional.TutorialServer;

/// <summary>
/// Legacy overlay HUD removed — tutorial prompts are shown via the handheld Tutorial Bound UI.
/// Kept as an empty system so any lingering registrations resolve cleanly during transition.
/// </summary>
public sealed class TutorialStepHudSystem : EntitySystem;
