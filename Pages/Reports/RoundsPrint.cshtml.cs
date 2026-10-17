using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Rounds, for the business manager or the cleaning contractor's review. Inherits the screen model
// so the figures are worked out once.
public class RoundsPrintModel(HelpdeskStore store) : RoundsReportModel(store);
