using EduHelpdesk.Services;

namespace EduHelpdesk.Pages.Reports;

// Print view of /Reports/Projects, sharing the screen model's figures. BrandName and GeneratedAt come from the shared
// _ReportPrint layout.
public class ProjectsPrintModel(HelpdeskStore store) : ProjectReportsModel(store);
