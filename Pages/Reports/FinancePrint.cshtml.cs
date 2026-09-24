using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Finance. Inherits the screen model, so the figures are computed once and the ?year= filter
// binds the same way. Gated by RequireSettings in Program.cs alongside the screen page.
public class FinancePrintModel(HelpdeskStore store) : FinanceReportsModel(store);
