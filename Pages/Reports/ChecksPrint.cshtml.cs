using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Checks, for governors and insurers. Inherits the screen model so the log is worked out once.
public class ChecksPrintModel(HelpdeskStore store) : ChecksReportModel(store);
