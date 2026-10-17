namespace EduHelpdesk.Services;

// The ? beside a field that needs explaining before someone fills it in. Each tip is a sentence or two in plain
// English, with an example where one helps, and the help article (and section) that says more. Fixed text that comes
// with the app: the school's own wording goes in a note on the article instead.
//
// Drawn with <help-tip key="..." /> (Pages/Shared/HelpTipTagHelper.cs). A key that isn't here draws nothing, and the
// tests check every key used on a page is here and every article it points to exists.
public static class HelpTips
{
    public sealed record Tip(string Text, string Article, string? Section = null);

    public static readonly IReadOnlyDictionary<string, Tip> All = new Dictionary<string, Tip>(StringComparer.OrdinalIgnoreCase)
    {
        // Settings → Desks
        ["desk.prefix"] = new("Letters shown in front of this desk's ticket numbers, so SITE-14 is plainly a site job. Leave blank for plain numbers (#14); only one desk can do that.", "using-help", "tips-on-fields"),
        ["desk.hours"] = new("The hours this desk's SLA clocks count. With its own hours, a spill reported at 17:55 with a 30-minute SLA is due at 06:25 the next working morning, not 18:25.", "using-help", "tips-on-fields"),

        // Settings → Cleaning rounds
        ["round.cleaner"] = new("Filled in against every room when the supervisor ticks the round off. They can change it on the day if someone else covered.", "getting-started-cleaning", "setting-up-a-round"),
        ["round.counted-from"] = new("The first day this round is due. Days before it never show as missed, so set it to the day the cleaners start working from the sheet.", "getting-started-cleaning", "setting-up-a-round"),
        ["round.active"] = new("Untick to stop the round appearing on Rounds and counting as missed (over the summer, say) while keeping everything already recorded on it.", "getting-started-cleaning", "setting-up-a-round"),

        // Rounds (recording)
        ["round.spot-check"] = new("Optional. A score from 1 (poor) to 5 (excellent) when you inspect a room. The rounds report averages them by room, lowest first.", "getting-started-cleaning", "ticking-off-a-round"),
    };

    public static Tip? Find(string? key) => key is not null && All.TryGetValue(key, out var tip) ? tip : null;
}
