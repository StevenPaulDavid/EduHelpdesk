using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Parts. Inherits the screen model, so the low-stock maths stays in one place.
public class PartsPrintModel(HelpdeskStore store) : PartReportsModel(store);
