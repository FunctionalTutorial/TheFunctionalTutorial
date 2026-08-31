using Robust.Shared.Localization;

namespace Content.Shared._Functional.TutorialServer;

/// <summary>Client-side Fluent resolve for tutorial LocIds (empty → empty).</summary>
public static class TutorialLoc
{
    public static string Get(string? locId)
    {
        if (string.IsNullOrEmpty(locId))
            return string.Empty;

        return Loc.GetString(locId);
    }

    /// <summary>
    /// Picker category/subcategory: YAML still stores English labels; map to Fluent when present.
    /// </summary>
    public static string GetCategory(string? category)
    {
        if (string.IsNullOrEmpty(category))
            return string.Empty;

        var slug = category.ToLowerInvariant().Replace(' ', '-');
        var id = $"tutorial-picker-category-{slug}";
        return Loc.TryGetString(id, out var localized) ? localized : category;
    }
}
