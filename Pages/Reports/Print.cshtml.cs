using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// The print view of /Reports. It inherits the screen model outright, so the figures come from exactly one OnGet and
// the ?warranty= and ?period= filters bind the same way - nothing here recomputes anything.
public class PrintModel(HelpdeskStore store) : EduHelpdesk.Pages.ReportsModel(store)
{
    // On paper there is no "show the rest" link, so every row is printed.
    public override int RowLimit => int.MaxValue;
}
