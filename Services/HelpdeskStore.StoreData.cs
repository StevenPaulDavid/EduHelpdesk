using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Everything the store holds in memory: one StoreData, loaded from the database at startup and written back on every save.
public sealed partial class HelpdeskStore
{
    public sealed class StoreData
    {
        public List<UserRecord> Users { get; set; } = [];
        public List<TechnicianRecord> Technicians { get; set; } = [];
        public List<RoleRecord> Roles { get; set; } = [];
        // 0 = the original nine on/off permissions, 2 = one stacked level per module, 3 = independent ticks.
        // Read from Metadata; see MigrateRolePermissions.
        public int PermissionModelVersion { get; set; }
        public List<string> TechnicianTeams { get; set; } = [];
        public List<string> Departments { get; set; } = [];
        public List<string> Locations { get; set; } = [];
        public List<string> AssetTypes { get; set; } = [];
        public List<string> AssetMakes { get; set; } = [];
        public List<string> AssetModels { get; set; } = [];
        public Dictionary<string, string> AssetModelMakes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> AssetStatuses { get; set; } = [];
        public List<string> PartCategories { get; set; } = [];
        public List<string> PartLocations { get; set; } = [];
        public List<string> LoanReasons { get; set; } = [];
        public List<LoanKit> LoanKits { get; set; } = [];
        public List<KitLoan> KitLoans { get; set; } = [];
        // A borrower with this many loans inside this many days is flagged on the loan report.
        public int LoanRepeatCount { get; set; } = 3;
        public int LoanRepeatDays { get; set; } = 30;
        // Automatic backups (Settings → Backups). The folder is blank for the default, backups under the data folder.
        public bool BackupsEnabled { get; set; } = true;
        public string BackupFolderSetting { get; set; } = "";
        public int BackupKeepDays { get; set; } = 14;
        public int BackupHour { get; set; } = 2;
        // How the last backups went, shown in Settings and on the overview. Times are UTC.
        public DateTime? LastBackupAt { get; set; }
        public string LastBackupFile { get; set; } = "";
        public string LastBackupSummary { get; set; } = "";
        public DateTime? LastBackupAttemptAt { get; set; }
        public string LastBackupError { get; set; } = "";
        // The database health check (Settings → Database): when to warn, and one size reading a day for its growth figures.
        public int SlowSaveWarningMs { get; set; } = DefaultSlowSaveWarningMs;
        public int LowDiskWarningGb { get; set; } = DefaultLowDiskWarningGb;
        public List<SizeSample> SizeHistory { get; set; } = [];
        // Imports from Spiceworks, and which record each Spiceworks ticket, comment, change and person became - so a
        // second import updates rather than duplicates, and the last one can be undone.
        public List<SpiceworksImportRecord> SpiceworksImports { get; set; } = [];
        public List<SpiceworksLink> SpiceworksLinks { get; set; } = [];
        // What Spiceworks said about each imported ticket at the last import, and what undoing each import needs.
        public Dictionary<int, string[]> SpiceworksTicketStates { get; set; } = [];
        public List<SpiceworksUndoEntry> SpiceworksUndo { get; set; } = [];
    // Which days count for work-day and period SLAs, and the lesson periods in each of them (Settings → School day).
    public List<DayOfWeek> SchoolDays { get; set; } = [.. SlaClock.DefaultSchoolDays];
    public List<SchoolPeriod> Periods { get; set; } = [];
        // Expected life in years per asset type, used to work out replacement dates.
        public Dictionary<string, int> AssetTypeLifespans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        // Warranty ends and replacement dates inside this many days go on the overview review list.
        public int AssetReviewDays { get; set; } = 60;
        // The month the academic year starts in, for the finance and audit report. September for most schools.
        public int AcademicYearStartMonth { get; set; } = AcademicYear.DefaultStartMonth;
        // Open tickets due within this many hours count as "due soon" on the ticket list.
        public int TicketDueSoonHours { get; set; } = 24;
        // Settings → Sign-in security: every staff account must use two-step sign-in (HelpdeskStore.TwoFactor).
        public bool RequireTwoFactor { get; set; }
        // Settings → Sign-in security: technicians may reset a forgotten password with their recovery key
        // (HelpdeskStore.RecoveryKeys). On unless a school turns it off.
        public bool AllowRecoveryKeys { get; set; } = true;
        // Settings → Notifications: pings and pop-ups on at all, for staff, for requesters in the portal, and whether people
        // who haven't switched them on are reminded to (HelpdeskStore.NotificationSettings). All on unless a school turns them off.
        public bool NotificationsEnabled { get; set; } = true;
        public bool StaffNotificationsEnabled { get; set; } = true;
        public bool RequesterNotificationsEnabled { get; set; } = true;
        public bool NotificationPrompt { get; set; } = true;
        // Settings → Teams channel: the webhook address (a secret, "" until set), whether posting is on, and which events
        // post (HelpdeskStore.Teams). Posting is off until a school turns it on; the two events default to on.
        public string TeamsWebhookUrl { get; set; } = "";
        public bool TeamsEnabled { get; set; }
        public bool TeamsNewTicket { get; set; } = true;
        public bool TeamsOverdue { get; set; } = true;
        // Settings → Spending bands: project pages show amounts including VAT. Off by default, so they show ex VAT.
        public bool ProjectPageIncludesVat { get; set; }
        // The address staff type to reach the helpdesk, printed on quick start guides. Empty until set in Settings → Sign-in
        // security or by the installer; the guide then works one out from the address in use (HelpdeskStore.Accounts).
        public string SiteAddress { get; set; } = "";
        // Statuses that stop the SLA clock, and how long after closing a requester's reply still reopens a ticket.
        public List<string> SlaPauseStatuses { get; set; } = [];
        public int ReopenWindowDays { get; set; } = DefaultReopenWindowDays;
        // Retention (Settings → Data retention), in months; 0 keeps everything. See HelpdeskStore.Lifecycle.
        public int RetentionTicketMonths { get; set; }
        public int RetentionLeaverMonths { get; set; }
        public int RetentionAuditMonths { get; set; }
        public DateTime? LastRetentionRunAt { get; set; }
        public string LastRetentionSummary { get; set; } = "";
        // Default minimum stock level for a part with no ReorderThreshold of its own.
        public int PartsDefaultReorderThreshold { get; set; } = 5;
        public List<string> Categories { get; set; } = [];
        public List<string> Statuses { get; set; } = [];
        public Dictionary<string, string> StatusDescriptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ClosureCommentPriorities { get; set; } = [];
        public List<string> ClosureCommentCategories { get; set; } = [];
        public List<string> Priorities { get; set; } = [];
        public List<string> RequireCloseMessagePriorities { get; set; } = [];
        public List<string> RequireCloseMessageCategories { get; set; } = [];
        public List<AssetRecord> Assets { get; set; } = [];
        public List<SupplierRecord> Suppliers { get; set; } = [];
        public List<PartRecord> Parts { get; set; } = [];
        public List<TicketPartAssignment> TicketParts { get; set; } = [];
        public List<TicketTemplate> TicketTemplates { get; set; } = [];
        public List<ServiceItem> ServiceItems { get; set; } = [];
        // Only categories someone has chosen a look for; the rest use PortalLook.DefaultFor.
        public Dictionary<string, CategoryStyle> CategoryStyles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<TicketAttachment> TicketAttachments { get; set; } = [];
        public List<TicketLink> TicketLinks { get; set; } = [];
        public List<AssetAttributeDefinition> AssetAttributeDefinitions { get; set; } = [];
        public List<AssetAttributeValue> AssetAttributeValues { get; set; } = [];
        public List<SlaDefinition> Slas { get; set; } = [];
        public List<TicketAttributeDefinition> TicketAttributeDefinitions { get; set; } = [];
        public List<TicketAttributeValue> TicketAttributeValues { get; set; } = [];
        public List<TicketRecord> Tickets { get; set; } = [];
        // What SeedDemoData created, so the "Go live" button knows exactly what to remove. Empty on a system that has
        // already gone live, or one upgraded from before demo data existed.
        public List<DemoRecord> DemoRecords { get; set; } = [];
        public int LastTicketNumber { get; set; } = 1000;
        public List<ProjectRecord> Projects { get; set; } = [];
        // Kept in Metadata as well as derived from the table, so deleting the newest project never hands its number out
        // again - a proposal already printed with PRJ-0007 on it must not come to mean a different project.
        public int LastProjectNumber { get; set; }
        // The purchasing-requirement tick boxes offered on a new project (Settings → Purchasing requirements).
        public List<string> PurchasingRequirements { get; set; } = [];
        // 0 before the Projects module existed; 1 once its starting options have been put in place, 2 once the example
        // spending bands have been. See EnsureProjectDefaults.
        public int ProjectsVersion { get; set; }
        // The finance policy's spending bands (Settings → Spending bands), and whether a project's total is compared
        // with them including VAT or excluding it.
        public List<SpendingBand> SpendingBands { get; set; } = [];
        public bool SpendingBandsIncludeVat { get; set; }
        // Onboarding new staff (HelpdeskStore.Onboarding): the checklist templates, and one record per onboarding ticket.
        public List<OnboardingTemplate> OnboardingTemplates { get; set; } = [];
        public List<OnboardingRecord> Onboardings { get; set; } = [];
        // The welcome pack's extra PDFs, and the school's own IT information printed on its cover.
        public List<OnboardingDocument> OnboardingDocuments { get; set; } = [];
        public string OnboardingItInfo { get; set; } = "";
        // 0 before onboarding existed; 1 once the example Teacher template has been put in place; 2 once the Onboarding
        // officer role has been.
        public int OnboardingVersion { get; set; }
        public BrandingSettings Branding { get; set; } = new();
    }
}
