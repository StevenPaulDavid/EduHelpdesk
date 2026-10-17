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

        // Settings: tickets
        ["sla.unit"] = new("Minutes, hours and days are clock time, nights and weekends included. Work days skip non-school days; periods count lessons from the next one to start.", "slas", "units"),
        ["sla.applies"] = new("A new ticket gets an SLA that lists its category first, otherwise one that lists its priority. Tick none of either and the SLA is only picked by hand.", "slas", "which-sla-a-ticket-gets"),
        ["status.desk"] = new("Keep a status to one desk, such as Awaiting contractor for Site. Only that desk's tickets can use it; a ticket moved away goes back to the new desk's first status.", "statuses", "statuses-kept-to-one-desk"),
        ["status.pause"] = new("For waiting on someone else. While a ticket is in a paused status it isn't overdue, and its SLA due date moves on by the time it waited.", "statuses", "stopping-the-sla-clock"),
        ["rules.due-soon"] = new("How many hours before its due date a ticket joins the Overdue / due soon queue, e.g. 4. Overdue tickets are always in it.", "ticket-rules", "due-soon"),
        ["rules.reopen"] = new("A requester's portal reply within this many days of closing reopens the ticket. After that they're asked to report it as a new one. 0 = never reopen.", "ticket-rules", "replies-to-closed-tickets"),
        ["rules.closing"] = new("Ticked priorities and categories need a closing message before a ticket can be closed. The requester sees it in the portal.", "ticket-rules", "closing-requirements"),
        ["schoolday.periods"] = new("Lesson times, the same every school day. Leave gaps for break and lunch. A 1-period SLA is due at the end of the next full lesson.", "school-day", "periods"),
        ["catalogue.priority"] = new("What the portal's priority box starts on for this button. Staff can still change it. Blank starts on Normal.", "service-catalogue", "buttons"),
        ["catalogue.show"] = new("Untick to keep the item in the catalogue without offering it to staff - while a service is unavailable, say.", "service-catalogue", "buttons"),
        ["template.portal"] = new("Shows this template as a button in the staff portal under its category. Its name is the button; staff can change the description but not the title.", "ticket-templates", "offering-it-in-the-staff-portal"),
        ["recurring.requester"] = new("Who the logged tickets are for - often yourself or the team that does the job, so replies come back to you.", "recurring-tickets", "the-ticket-it-logs"),
        ["recurring.starts"] = new("The first date it can be logged. Every 2 weeks, 3 months and so on count from here, so pick a date on the right weekday.", "recurring-tickets", "the-schedule"),
        ["recurring.school-day"] = new("Ticked, a date that falls on a weekend or day off moves to the next school day. Unticked, it's logged on the day itself.", "recurring-tickets", "the-schedule"),
        ["attribute.type"] = new("Single-line or multi-line text, a dropdown of fixed choices (one per line), or a checkbox. A dropdown keeps answers consistent for reports.", "custom-fields", "adding-a-field"),

        // Settings: system
        ["branding.appearance"] = new("Light or dark for devices that haven't chosen. Anyone can pick their own from the Appearance switch at the foot of every page.", "branding", "default-appearance"),
        ["backup.folder"] = new("Blank keeps backups beside the data. Another drive, a network share or a OneDrive folder survives this computer's disk failing. Keep it private.", "backups", "automatic-backups"),
        ["backup.keep"] = new("Older backups are deleted automatically; the newest few are always kept. Longer gives more to go back to but uses more space.", "backups", "automatic-backups"),
        ["signin.two-step"] = new("A code from a phone app after the password. Required, anyone without it sets it up next time they use the helpdesk.", "sign-in-security", "two-step-sign-in"),
        ["signin.recovery"] = new("Lets a technician who forgets their password reset it once from the sign-in page, without an administrator.", "sign-in-security", "recovery-keys"),
        ["signin.address"] = new("The web address staff type to reach the helpdesk, printed on new accounts' quick start guides, e.g. https://helpdesk.school.org.uk/", "sign-in-security", "helpdesk-address"),
        ["notifications.reminder"] = new("A bar under the header for anyone who hasn't switched pings and pop-ups on. They can dismiss it for a week.", "notifications-setup", "who-gets-notifications"),
        ["teams.webhook"] = new("The address from a Teams workflow that posts to the channel. It's a secret - anyone with it can post - so it's never shown again once saved.", "teams-channel", "channels"),
        ["edunotify.ping"] = new("A notice on technicians' PCs when a ticket at a ticked priority arrives with nobody assigned. It comes down when someone is assigned.", "edunotify", "technician-ping"),
        ["retention.tickets"] = new("Closed tickets older than this are deleted for good, with their files and history, after the nightly backup. 0 keeps them forever.", "data-retention", "rules"),
        ["retention.leavers"] = new("People marked inactive longer than this lose their name and contact details everywhere. Their tickets stay, anonymised. 0 = never.", "data-retention", "rules"),
        ["retention.audit"] = new("Audit log entries older than this are deleted. There's a minimum, because the log shows who changed what.", "data-retention", "rules"),

        // People
        ["role.ticks"] = new("Access: open the list. View: read a record. New: add. Edit: change. Delete: remove. Each box is separate - tick exactly what the job needs.", "roles-and-permissions", "the-five-ticks-per-area"),
        ["account.desks"] = new("The desks they work on. They see only these desks' tickets, unless their role has See all desks.", "staff-accounts", "adding-one"),
        ["account.password"] = new("Leave blank for a temporary password they change at first sign-in, printed on their quick start guide. A password you type is theirs to keep.", "staff-accounts", "adding-one"),
        ["person.projects"] = new("Can raise projects: ask IT to source and quote for purchases from the portal. Project lead: also confirms priorities and assigns technicians.", "people", "a-requester-s-details"),
        ["import.passwords"] = new("Gives everyone imported a temporary password and opens their quick start guides to print. Untick to import without passwords.", "imports", "requesters-and-staff-accounts"),

        // Assets and inventory
        ["asset.ownership"] = new("Owned by the school, or leased or loaned from someone else. For leased and loaned assets, End of support becomes the date the lease ends.", "asset-page", "details"),
        ["asset.condition"] = new("New, used, refurbished or donated when it arrived - a column on the DfE asset register.", "asset-page", "details"),
        ["asset.replacement"] = new("When it's due for replacement, for budgeting. Leave blank to use the purchase date plus the type's lifespan (Settings → Asset types).", "inventory-rules", "asset-types"),
        ["asset.support"] = new("When it stops getting security updates, or a lease ends. Flagged ahead of time. Separate from the replacement date, which is for budgeting.", "asset-checks", "end-of-support"),
        ["asset.next-check"] = new("When its next DfE check is due. Recording a check sets this automatically, a set number of months later.", "asset-checks", "recording-a-check"),
        ["asset.disposal"] = new("For an asset leaving the school - sold, recycled, donated or written off. Keeps the record for finance and auditors instead of deleting it.", "dispose-an-asset"),
        ["assets.bulk"] = new("Tick assets in the list, choose what to do - change status, owner or room, record a check, or export - then Apply.", "asset-register", "changing-many-at-once"),
        ["import.blank-clears"] = new("Off (recommended): a blank cell leaves the existing value alone. On: a blank cell empties that field on an existing asset.", "import-assets", "2-match-the-columns"),
        ["import.add-missing"] = new("On: makes, models, rooms and so on that aren't in the lists yet are added. Off: a row with an unknown value is skipped instead.", "import-assets", "2-match-the-columns"),
        ["loan.borrower"] = new("Someone from the staff directory. For a kit only, choose Not listed and type a name for supply staff or visitors.", "loans", "issuing-a-loan"),
        ["loan.reason"] = new("Why they need it, from Settings → Loan reasons. The loan report uses it to spot patterns, such as repeated forgotten chargers.", "loans", "issuing-a-loan"),
        ["kit.retired"] = new("Stops offering the kit for new loans but keeps its loan history. Use it instead of deleting a kit that's been lent.", "loan-kits", "retiring-a-kit"),
        ["part.threshold"] = new("Flag the part as low once stock drops to this or below. Blank uses the default in Settings → Asset, part & loan rules.", "parts", "adding-a-part"),
        ["part.correct"] = new("Replaces the count rather than adding to it - for a stocktake, breakage or something gone missing. Deliveries use Add to stock.", "parts", "changing-the-stock"),
        ["rules.review-window"] = new("Assets whose warranty ends or replacement is due within this many days appear in the review list on the Overview.", "inventory-rules"),
        ["rules.check-months"] = new("Recording a DfE check sets the asset's next check this many months later, e.g. 12 for yearly.", "inventory-rules"),
        ["rules.academic-year"] = new("The finance report groups purchases and disposals by academic year starting this month, so a July purchase counts in the year that began last September.", "inventory-rules"),
        ["assettype.lifespan"] = new("How many years this type should last. An asset's replacement date is its purchase date plus this, unless one is typed on the asset.", "inventory-rules", "asset-types"),
        ["assettype.desk"] = new("Which desk this type belongs to: plant such as boilers to Site, so the asset list and report keep it apart from IT's kit.", "inventory-rules", "asset-types"),

        // Compliance
        ["contract.notice"] = new("Months of notice needed to renew or cancel, counted back from the renewal (or end) date. The register warns before that notice date.", "contracts-register", "adding-a-contract"),
        ["contract.cost-period"] = new("What the cost covers. Monthly, quarterly and yearly costs are turned into a yearly figure for the register's total; one-off costs are left out.", "contracts-register", "adding-a-contract"),
        ["contract.related-party"] = new("A contract with someone connected to the school or trust. The Academy Trust Handbook says report it to the DfE before it starts or renews.", "contracts-register", "compliance-ticks"),
        ["access.privileged"] = new("Administrator-level access. The cyber security standard expects SLT or a trustee to approve it first - record who under Approved by.", "access-register", "recording-access"),
        ["access.mfa"] = new("Needed for cloud or remote access and every administrator account. Not available means a complex password is used instead. Keys: not applicable.", "access-register", "recording-access"),
        ["system.mfa"] = new("Tick for anything reached from the cloud or from home. Anyone recorded with access to it but no MFA is then flagged.", "access-systems"),
        ["review.remove"] = new("Everything is kept unless ticked. Ticked access is removed when you record the review, with the reason you give.", "access-review", "doing-a-review"),
        ["review.every"] = new("How often access should be reviewed. About 120 days is once a term; access not reviewed within this shows as overdue.", "access-review", "how-often"),

        // Settings → Cleaning rounds
        ["round.cleaner"] = new("Filled in against every room when the supervisor ticks the round off. They can change it on the day if someone else covered.", "getting-started-cleaning", "setting-up-a-round"),
        ["round.counted-from"] = new("The first day this round is due. Days before it never show as missed, so set it to the day the cleaners start working from the sheet.", "getting-started-cleaning", "setting-up-a-round"),
        ["round.active"] = new("Untick to stop the round appearing on Rounds and counting as missed (over the summer, say) while keeping everything already recorded on it.", "getting-started-cleaning", "setting-up-a-round"),

        // Rounds (recording)
        ["round.spot-check"] = new("Optional. A score from 1 (poor) to 5 (excellent) when you inspect a room. The rounds report averages them by room, lowest first.", "getting-started-cleaning", "ticking-off-a-round"),
    };

    public static Tip? Find(string? key) => key is not null && All.TryGetValue(key, out var tip) ? tip : null;
}
