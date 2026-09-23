using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Loans. Inherits the screen model so the loan figures are computed once - this used to carry
// its own copy of OnGet. BrandName and GeneratedAt come from the shared _ReportPrint layout.
public class LoansPrintModel(HelpdeskStore store) : LoanReportsModel(store);
