using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Whether connecting players are shown the rules acceptance popup.
    ///     Admins can still force the popup with the showrules command.
    /// </summary>
    public static readonly CVarDef<bool> RulesEnabled = //Wizden
        CVarDef.Create("rules.enabled", false, CVar.SERVERONLY); //Wizden: off by default (was always-on)

    /// <summary>
    ///     Time that players have to wait before rules can be accepted.
    /// </summary>
    public static readonly CVarDef<float> RulesWaitTime =
        CVarDef.Create("rules.time", 45f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Don't show rules to localhost/loopback interface.
    /// </summary>
    public static readonly CVarDef<bool> RulesExemptLocal =
        CVarDef.Create("rules.exempt_local", true, CVar.SERVERONLY);
}
