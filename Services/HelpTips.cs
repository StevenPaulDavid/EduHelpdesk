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
        ["desk.prefix"] = new("Letters shown in front of this desk's ticket numbers, so SITE-14 is plainly a site job. Leave blank for plain numbers (#14); only one desk can do that.", "desks", "ticket-numbers"),
        ["desk.hours"] = new("The hours this desk's SLA clocks count. With its own hours, a spill reported at 17:55 with a 30-minute SLA is due at 06:25 the next working morning, not 18:25.", "desks", "working-hours"),

        // New ticket
        ["ticket.category"] = new("What the ticket is about. It also decides which desk the ticket is on, and so its number (IT-12, SITE-4) and who can see it.", "desks", "which-desk-a-ticket-is-on"),
        ["ticket.team"] = new("The group responsible. Choosing a team narrows the technician list to its people. Leave team and technician blank to put the ticket in Unassigned.", "new-ticket", "assignment-scheduling"),
        ["ticket.sla"] = new("How long the ticket has, e.g. 4 hours or 2 work days. The due date is worked out from it. No SLA means no due date, so it's never overdue.", "due-dates-and-slas"),
        ["ticket.due-override"] = new("Only to set a date by hand. Leave blank and the due date is worked out from the SLA, and moves if the priority or category changes.", "due-dates-and-slas", "changing-the-due-date"),

        // The ticket page
        ["ticket.reply-kind"] = new("Reply goes to the requester and is printed on the ticket. Internal note is for the helpdesk only - never shown to the requester or printed.", "replies-and-internal-notes", "reply-or-internal-note"),
        ["ticket.notify"] = new("Sends the requester a pop-up in the staff portal about this reply, if they've switched pop-ups on. Leave it off for small updates.", "replies-and-internal-notes", "notifying-the-requester"),
        ["ticket.status"] = new("Saved with your reply. Some statuses, such as On Hold, stop the SLA clock while the ticket waits on someone else.", "due-dates-and-slas", "statuses-that-pause-the-clock"),
        ["ticket.closing-message"] = new("Shown to the requester in the staff portal, so write it for them: what was wrong and what was done.", "close-a-ticket", "when-a-closing-message-is-needed"),
        ["ticket.share-file"] = new("Ticked, the requester can see and download the file in the staff portal. Unticked, it's kept for the helpdesk only. You can change it later.", "replies-and-internal-notes", "sharing-files"),
        ["ticket.due-date"] = new("Set a date by hand and it's marked Override and stays put. To go back to the SLA's date, clear the date and save, then choose the SLA again.", "due-dates-and-slas", "changing-the-due-date"),

        // The ticket list
        ["tickets.working-as"] = new("Whose tickets My tickets shows. Pick a colleague to cover their queue while they're off. Remembered in this browser only.", "ticket-list", "working-as"),
        ["tickets.bulk"] = new("Tick tickets in the list, choose what to do, then Apply. Tickets an action can't apply to are skipped, and the message says why.", "update-many-tickets"),

        // Log a job already done
        ["quicklog.when"] = new("When the job was done. It starts at now - change it if you're catching up on jobs from earlier.", "quick-log", "the-form"),
        ["quicklog.who"] = new("The person who did the job. Only people on the job's desk are listed; it's saved as their closed ticket.", "quick-log", "the-form"),

        // Costs tab
        ["cost.net"] = new("The amount before VAT, as on the invoice, e.g. 120.00. Type the VAT in its own box, so reports can show net, VAT and gross.", "job-costs", "adding-a-cost"),
        ["cost.budget-code"] = new("What the cost is charged to, such as Reactive maintenance. The Job spend report totals by budget code. The list is in Settings → Budget codes.", "job-costs", "budget-codes"),

        // Premises checks
        ["check.result"] = new("Choose pass or fail, or type the reading. A reading outside its limits counts as a fail. The ticket can't be closed until every line is answered.", "premises-checks", "filling-in-the-checklist"),
        ["check.kind"] = new("Pass or fail: a yes/no check. Reading with limits: a measured number, such as a water temperature. Every asset of a type: one line per asset on the register. Note: free text, never required.", "set-up-premises-checks", "2-add-the-checklist"),
        ["check.limits"] = new("Readings only. Lowest allowed for 'at least' (hot water: 50), highest allowed for 'no more than' (cold water: 20), or both. Outside them is a fail.", "set-up-premises-checks", "2-add-the-checklist"),
        ["check.asset-type"] = new("For 'every asset of a type': one pass/fail line for each asset of this type on the register when the ticket is logged, e.g. every extinguisher.", "set-up-premises-checks", "what-changes-affect"),

        // Settings → Cleaning rounds
        ["round.cleaner"] = new("Filled in against every room when the supervisor ticks the round off. They can change it on the day if someone else covered.", "getting-started-cleaning", "setting-up-a-round"),
        ["round.counted-from"] = new("The first day this round is due. Days before it never show as missed, so set it to the day the cleaners start working from the sheet.", "getting-started-cleaning", "setting-up-a-round"),
        ["round.active"] = new("Untick to stop the round appearing on Rounds and counting as missed (over the summer, say) while keeping everything already recorded on it.", "getting-started-cleaning", "setting-up-a-round"),

        // Rounds (recording)
        ["round.spot-check"] = new("Optional. A score from 1 (poor) to 5 (excellent) when you inspect a room. The rounds report averages them by room, lowest first.", "getting-started-cleaning", "ticking-off-a-round"),
    };

    public static Tip? Find(string? key) => key is not null && All.TryGetValue(key, out var tip) ? tip : null;
}
