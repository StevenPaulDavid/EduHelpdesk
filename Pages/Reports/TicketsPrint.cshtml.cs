using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Tickets. Inherits the screen model so the SLA maths runs once.
public class TicketsPrintModel(HelpdeskStore store) : TicketReportsModel(store)
{
    // The overdue list is the one table a manager needs in full - the rest are top-15 rankings and stay that way.
    public override int OverdueLimit => int.MaxValue;
}
