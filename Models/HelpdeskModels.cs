namespace EduHelpdesk.Models;

public record UserRecord(Guid Id, string Name, string Email, string Department, string Location, string? PasswordHash = null, bool IsActive = true)
{
    // Line managers and SLT, who may raise purchasing projects from the staff portal. Everyone else can only report
    // problems there, so a project request can't come from anyone who isn't meant to be asking for spending.
    public bool CanRaiseProjects { get; init; }
    // The project lead: confirms each project's priority and chooses the technician, from the staff portal - the lead
    // has no helpdesk login. Also lets them raise projects themselves, already assigned.
    public bool IsProjectLead { get; init; }
    public bool MayRaiseProjects => IsActive && (CanRaiseProjects || IsProjectLead);
    // Set when a technician gives the requester a portal password: someone else knows it, so the portal asks for a new
    // one before anything else (PasswordChangeFilter).
    public bool RequirePasswordChange { get; init; }
}
public record TechnicianRecord(Guid Id, string Name, string Email, string Team, string Role = "Technician", string? PasswordHash = null, bool RequirePasswordChange = false, bool IsActive = true);
// What a role can do with one module. Each is an independent tick rather than a rung on a ladder: a role can be given
// Delete without Edit, or New without Access, because which combinations make sense is the school's call, not ours.
// Flags so one value holds a module's whole set.
[Flags]
public enum ModulePermission
{
    None = 0,
    Access = 1,   // open the list
    View = 2,     // open a record and read it
    New = 4,      // create records
    Edit = 8,     // change existing records
    Delete = 16   // remove records
}

// A named set of permissions a technician account can hold. Roles are user-defined (see HelpdeskStore.Roles);
// Administrator is the one hardcoded, protected exception - see StaffRoles below and HelpdeskStore.UserCan.
// Grants are keyed by module (see Services/Modules.cs); Flags hold what is not an action on a record, such as which
// individual reports are readable.
public record RoleRecord(string Name, bool IsProtected = false)
{
    public Dictionary<string, ModulePermission> Grants { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public ModulePermission GrantsFor(string module) => Grants.GetValueOrDefault(module, ModulePermission.None);
    // Exact, not cumulative: asking for Edit means the Edit box is ticked, nothing else.
    public bool Allows(string module, ModulePermission action) => (GrantsFor(module) & action) == action && action != ModulePermission.None;
    public bool Has(string flag) => Flags.Contains(flag);
}
// The one role name that is hardcoded rather than data-driven: always has every permission, can't be edited or deleted,
// and is guaranteed to exist (see HelpdeskStore.EnsureBootstrapAdministrator) so there is always a way into the system.
public static class StaffRoles
{
    public const string Administrator = "Administrator";
    // Fallback used when a stored Role value doesn't match any known role (e.g. legacy data).
    public const string DefaultRole = "Technician";
}
// Who performed a change. Name is the display name captured at the time, so a history line still reads correctly after
// someone is renamed or their account is deleted - the same reasoning as KitLoan.BorrowerName. Id is the technician
// account behind it and is what reporting groups on; it is null for the staff portal, which identifies people by
// cookie rather than by login, and for anything the system did itself.
// Every record that carries one exposes it as a nullable "By": null means the change predates actor attribution.
public readonly record struct Actor(Guid? Id, string Name)
{
    public static readonly Actor System = new(null, "System");
    // What the views show. An em dash rather than a guess for anything recorded before attribution existed - the
    // system genuinely does not know who did it, and saying so is better than implying it was nobody or the system.
    public static string Label(Actor? actor) => actor?.Name ?? "—";
}
public record SupplierRecord(Guid Id, string Name, string? ContactName, string? Email, string? Phone, string? AddressLine1, string? AddressLine2, string? City, string? StateRegion, string? PostalCode, string? Country, string? Website, string? Notes, DateTime CreatedAt);
public record PartRecord(Guid Id, string Name, string? Sku, string? Category, int QuantityOnHand, DateTime CreatedAt)
{
    // Where it's physically kept (shelf, cupboard, room) - free text, not the shared building-level Locations list.
    public string Location { get; init; } = "";
    // Minimum stock level before this part is flagged low. Null means use the store-wide default (HelpdeskStore.PartsDefaultReorderThreshold).
    public int? ReorderThreshold { get; init; }
    public List<Guid> SupplierIds { get; init; } = [];
    // Optional: the asset type(s) this part is compatible with/used on. Empty means no compatibility recorded, not "fits everything".
    public List<string> AssetTypes { get; init; } = [];
    // Logged stock changes (via HelpdeskStore.AdjustPartStock) - separate from the plain field-change diffing every other
    // Part field gets, because the whole point is to keep the reason alongside the number.
    public List<PartActivity> History { get; init; } = [];
}
public record PartActivity(string Action, string Details, DateTime CreatedAt)
{
    public Actor? By { get; init; }
}
public record TicketPartAssignment(int TicketNumber, Guid PartId, int Quantity);
// A named loan kit handed out when someone has no device. The kit keeps its identity and its loan history while the
// equipment inside it can be swapped out, so AssetIds is "what is in it now", not a permanent bundle.
public record LoanKit(Guid Id, string Name, string Notes, DateTime CreatedAt)
{
    public List<Guid> AssetIds { get; init; } = [];
    // Retired kits stay in the history but can no longer be issued.
    public bool IsRetired { get; init; }
}
// One issue-and-return cycle. BorrowerName is always stored, so history still reads correctly if the user record is
// later deleted, and so one-off borrowers (supply staff, visitors) who are not in the directory can be recorded at all.
public record KitLoan(
    Guid Id,
    Guid KitId,
    Guid? BorrowerUserId,
    string BorrowerName,
    string Reason,
    DateTime IssuedAt,
    DateOnly DueBack,
    DateTime? ReturnedAt,
    string IssuedBy,
    string Notes)
{
    public bool IsOut => ReturnedAt is null;
    public bool IsOverdue(DateOnly today) => ReturnedAt is null && DueBack < today;
}
public record AssetRecord(Guid Id, string AssetTag, string Make, string Model, string Type, string SerialNumber, string Location, Guid? AssignedUserId, Guid? SupplierId = null)
{
    public List<AssetComment> Comments { get; init; } = [];
    public List<AssetActivity> History { get; init; } = [];
    public List<AssetAssignment> Assignments { get; init; } = [];
    public string Status { get; init; } = "In use";
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchasePrice { get; init; }
    public string PurchaseOrder { get; init; } = "";
    // The supplier's quote number. Orders go via the Trust, so this is often the only reference the school ever gets -
    // a PO may never arrive, or may turn up later, which is why both are kept rather than one field with a type.
    public string QuoteReference { get; init; } = "";
    // Set when the asset leaves the estate. It stays in the register afterwards: an auditor needs the record to
    // persist, which is the whole reason disposal is a status rather than a delete.
    public DateOnly? DisposalDate { get; init; }
    public string DisposalMethod { get; init; } = "";
    public decimal? DisposalProceeds { get; init; }
    public DateOnly? WarrantyEnd { get; init; }
    // A replacement date typed on the asset. When blank, the date comes from the lifespan of the asset type (see AssetInsights).
    public DateOnly? ReplacementDate { get; init; }
    // Set while the asset is on loan to AssignedUserId.
    public DateOnly? LoanDueDate { get; init; }
}
// One period in which an asset was held by someone. StartedAt is null for holders recorded before assignments were tracked.
// A period with a DueBack is a loan - something expected back. One without is a permanent allocation (a teacher's own
// laptop) and is deliberately kept out of every loan list and report.
public record AssetAssignment(Guid? UserId, string UserName, DateTime? StartedAt, DateTime? EndedAt, DateOnly? DueBack)
{
    // Why it went out, from the same list kit loans use. Null means not recorded: it pre-dates reasons being required,
    // and those rows are shown but never counted toward repeat-borrower flagging.
    public string? Reason { get; init; }
    // Set when issuing a kit created this period. Kit loans already appear in their own right, so without this marker
    // the unified loan list would count one kit loan again for every asset inside the kit.
    public Guid? KitLoanId { get; init; }
}
public record AssetComment(string Text, DateTime CreatedAt)
{
    public Actor? By { get; init; }
}
public record AssetActivity(string Action, string Details, DateTime CreatedAt)
{
    public Actor? By { get; init; }
}
public record AssetAttributeDefinition(Guid Id, string Name, string FieldType = "single-line", string Choices = "")
{
    public List<string> AssetTypes { get; init; } = [];
    public bool AppliesTo(string? assetType) => AssetTypes.Count == 0 || AssetTypes.Contains(assetType ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
public record AssetAttributeValue(Guid AssetId, Guid AttributeDefinitionId, string Value);
// One lesson period in the school day (Settings → School day). The same timings apply to every school day, and the
// gaps between periods are breaks, lunch and after school. Used by SLAs measured in periods - see SlaClock.
public record SchoolPeriod(string Name, TimeOnly Start, TimeOnly End);
public record SlaDefinition(Guid Id, string Name, int Duration, string DurationUnit, string? Description = null)
{
    public List<string> Priorities { get; init; } = [];
    public List<string> Categories { get; init; } = [];
}
public record TicketAttributeDefinition(Guid Id, string Name, string FieldType = "single-line", string Choices = "")
{
    public List<string> Categories { get; init; } = [];
    public bool AppliesTo(string? category) => Categories.Count == 0 || Categories.Contains(category ?? string.Empty, StringComparer.OrdinalIgnoreCase);
}
public record TicketAttributeValue(int TicketNumber, Guid AttributeDefinitionId, string Value);
// The two kinds of ticket: something that is broken, and something that is being asked for. Used to separate them in lists and reports.
public static class TicketTypes
{
    public const string Incident = "Incident";
    public const string Request = "Request";
    public static readonly string[] All = [Incident, Request];
    // The matching type, or Incident for anything unknown or blank.
    public static string Normalize(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Incident;
}
// A saved starting point for a ticket. A blank SlaId means the SLA is worked out from the priority and category as usual.
public record TicketTemplate(Guid Id, string Name, string Type, string Title, string Description, string Category, string Priority, Guid? SlaId)
{
    // Default answers for ticket custom attributes, by attribute definition id.
    public Dictionary<Guid, string> AttributeValues { get; init; } = [];
}
public record TicketRecord(
    int Number,
    string Title,
    string Description,
    Guid RequesterId,
    IReadOnlyList<Guid> AssetIds,
    Guid? TechnicianId,
    string Priority,
    string Status,
    string Category,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    Guid? SlaId = null,
    DateTime? DueDate = null,
    bool DueDateOverridden = false,
    bool SlaOverridden = false,
    string? TeamName = null,
    string? Location = null)
{
    public List<TicketComment> Comments { get; init; } = [];
    public List<TicketActivity> History { get; init; } = [];
    public string Type { get; init; } = TicketTypes.Incident;
    // Times the SLA clock was stopped (a status that pauses it, such as On Hold). The last one is open while the ticket
    // is still paused. Kept as periods rather than a running total, so a due date recalculated later - a priority
    // change, say - can still add them back in whatever units the new SLA counts. See HelpdeskStore.TicketProcess.
    public List<SlaPause> SlaPauses { get; init; } = [];
    public bool IsSlaPaused => SlaPauses.Count > 0 && SlaPauses[^1].EndedAt is null;
    // When the requester last opened the ticket in the portal, for its "New reply" flag. Null until they first do.
    public DateTime? RequesterSeenAt { get; init; }

    // The newest of creation, any history entry (field changes, parts, attributes, merges, asset links) and any comment.
    public DateTime LastModifiedAt => Comments.Select(x => x.CreatedAt).Concat(History.Select(x => x.CreatedAt)).Append(CreatedAt).Max();
}

// A record created by SeedDemoData, remembered so "Go live" can remove exactly what the system seeded. Matching on
// names instead would be guesswork: a school can rename a demo record, and a real one can legitimately contain "demo".
// EntityType uses the same names as AuditEntry.EntityType where they overlap.
public record DemoRecord(string EntityType, string EntityKey);

// One line of the system audit. EntityType and EntityKey identify the record it concerns (for linking); they are null for lists and settings.
public record AuditEntry(DateTime At, string Area, string? EntityType, string? EntityKey, string Entity, string Action, string Details)
{
    public Actor? By { get; init; }
}

// An internal note is for technicians: it is left off the printed ticket.
public record TicketComment(string Text, DateTime CreatedAt, bool IsInternal = false)
{
    public Actor? By { get; init; }
    // Written by the requester in the staff portal - the Tickets list flags a ticket whose last word is theirs.
    public bool FromRequester { get; init; }
}
public record SlaPause(DateTime StartedAt, DateTime? EndedAt);
public record TicketActivity(string Action, string Details, DateTime CreatedAt)
{
    public Actor? By { get; init; }
}
// A purchasing project: a line manager asks for something to be bought, a lead assigns a technician, and the technician
// gathers supplier quotes into a proposal. It is quote-focused - approval and ordering happen outside the system, and
// closing records how it ended.
public record ProjectRecord(int Number, string Title, Guid RequesterId, DateOnly DueDate, string ItemsWanted, DateTime CreatedAt)
{
    // Ticked from the list in Settings, kept as text so a project still says what was asked for after an option is
    // renamed away or deleted.
    public List<string> PurchasingRequirements { get; init; } = [];
    public string PurchasingOther { get; init; } = "";
    // 1 is the highest and 5 the lowest - its own scale, separate from ticket priorities. The requester suggests one;
    // the project lead confirms it when assigning, and until then the suggestion stands.
    public int SuggestedPriority { get; init; } = ProjectPriorities.Default;
    public int? Priority { get; init; }
    public Guid? TechnicianId { get; init; }
    public string Status { get; init; } = ProjectStatuses.New;
    public DateTime? ClosedAt { get; init; }
    public string? Outcome { get; init; }
    public string? OutcomeNote { get; init; }
    public List<ProjectNote> Notes { get; init; } = [];
    public List<ProjectActivity> History { get; init; } = [];
    // What the technician turned the free-text "items wanted" into: the main things being bought, each with its own
    // suppliers and quotes. ItemsWanted stays as the requester wrote it.
    public List<ProjectItem> Items { get; init; } = [];
    // Helpdesk tickets this project is linked to - the ticket it was started from, or ones raised about the same
    // purchase. Numbers only: a ticket merged away or deleted is repointed or dropped by the store.
    public List<int> TicketNumbers { get; init; } = [];

    public int EffectivePriority => Priority ?? SuggestedPriority;
    // What the chosen quotes come to across every item. Items without a chosen quote add nothing.
    public QuoteTotals ChosenTotals => Items.Select(x => x.Chosen).OfType<ItemSupplier>().Aggregate(new QuoteTotals(), (sum, x) => sum + QuoteTotals.Of(x.PaymentLines));
    public bool IsActive => Status != ProjectStatuses.Closed;
    public string Reference => $"PRJ-{Number:0000}";
    // "Proposal needed by" - it stops mattering once the proposal is ready.
    public bool IsOverdue(DateOnly today) => (Status is ProjectStatuses.New or ProjectStatuses.GatheringQuotes) && DueDate < today;
    public bool IsDueSoon(DateOnly today) => (Status is ProjectStatuses.New or ProjectStatuses.GatheringQuotes) && DueDate >= today && DueDate <= today.AddDays(ProjectPriorities.DueSoonDays);
    public DateTime LastModifiedAt => Notes.Select(x => x.CreatedAt).Concat(History.Select(x => x.CreatedAt)).Append(CreatedAt).Max();
}
// One main thing a project is buying - "30 iPads", "1 charging trolley". Sub-items are an unpriced checklist of what
// comes with it (keyboards, pencils); the item's price, from the suppliers' quotes, covers the bundle.
public record ProjectItem(Guid Id, string Name, int Quantity)
{
    public List<ProjectSubItem> SubItems { get; init; } = [];
    public List<ItemSupplier> Suppliers { get; init; } = [];
    // The quote the proposal's totals use for this item. Every received quote is still compared alongside it.
    public Guid? ChosenSupplierId { get; init; }
    public ItemSupplier? Chosen => ChosenSupplierId is { } id ? Suppliers.FirstOrDefault(x => x.SupplierId == id) : null;
}
public record ProjectSubItem(Guid Id, string Name, int Quantity);
// A supplier asked (or about to be asked) to quote for one item. StatusHistory is every status it has had, oldest
// first, so "requested on 3 Sept, received on 10 Sept" and "awaited 9 days" can both be read from it.
public record ItemSupplier(Guid SupplierId)
{
    public List<QuoteStatusChange> StatusHistory { get; init; } = [];
    // How long the supplier says the quote holds - often 30 days.
    public DateOnly? ValidUntil { get; init; }
    // The supplier's own quote number - orders placed through the Trust often only ever carry this.
    public string Reference { get; init; } = "";
    // The quote as it stands: its files and what it costs. An updated quote replaces both, optionally keeping the old
    // ones together in PreviousVersions so the change can be seen.
    public List<QuoteDocument> Documents { get; init; } = [];
    public List<PaymentLine> PaymentLines { get; init; } = [];
    public List<QuoteVersion> PreviousVersions { get; init; } = [];

    public string Status => StatusHistory.Count == 0 ? QuoteStatuses.NotRequested : StatusHistory[^1].Status;
    public DateTime? StatusSince => StatusHistory.Count == 0 ? null : StatusHistory[^1].At;
    public bool IsAwaited => Status is QuoteStatuses.Requested or QuoteStatuses.UpdateRequested;
    public bool HasQuote => Status is QuoteStatuses.Received or QuoteStatuses.UpdateReceived or QuoteStatuses.UpdateRequested;
    public bool IsExpired(DateOnly today) => HasQuote && ValidUntil is { } until && until < today;
    // Whole days since the quote was asked for, while it is still outstanding.
    public int? DaysAwaited(DateTime now) => IsAwaited && StatusSince is { } since ? Math.Max(0, (int)(now - since).TotalDays) : null;
}
public record QuoteStatusChange(string Status, DateTime At)
{
    public Actor? By { get; init; }
}
// One file of a supplier's quote. Kept on disk under App_Data/attachments, named by Id, like ticket attachments.
public record QuoteDocument(Guid Id, string FileName, string ContentType, long Size, DateTime UploadedAt)
{
    public Actor? By { get; init; }
}
// One way the quote is paid: "£8,000 once", "£400 a year for 3 years". Amounts are per payment and exclude VAT; each
// line says which VAT applies, because zero-rated items and suppliers who aren't VAT-registered sit alongside standard ones.
public record PaymentLine(Guid Id, string Description, decimal Amount, string Frequency, int TermYears, string Vat)
{
    public int PaymentCount => PaymentFrequencies.PerYear(Frequency) is var perYear and > 0 ? perYear * TermYears : 1;
    public decimal VatRate => VatTreatments.Rate(Vat);
    public decimal TermExVat => Amount * PaymentCount;
    // The first twelve months: a one-off payment falls in it whole; a recurring one for as many payments as a year holds.
    public decimal FirstYearExVat => Amount * (PaymentFrequencies.PerYear(Frequency) is var perYear and > 0 ? Math.Min(perYear, PaymentCount) : 1);
    public decimal TermVat => Math.Round(TermExVat * VatRate, 2, MidpointRounding.AwayFromZero);
    public decimal FirstYearVat => Math.Round(FirstYearExVat * VatRate, 2, MidpointRounding.AwayFromZero);
}
// A quote as it was before an update replaced it: its files and prices together, so what changed can still be seen.
public record QuoteVersion(Guid Id, DateTime ArchivedAt, string Reference, DateOnly? ValidUntil)
{
    public List<QuoteDocument> Documents { get; init; } = [];
    public List<PaymentLine> PaymentLines { get; init; } = [];
    public Actor? By { get; init; }
}
// Totals for a set of payment lines - one quote, or every chosen quote on a project.
public readonly record struct QuoteTotals(decimal FirstYearExVat, decimal FirstYearVat, decimal TermExVat, decimal TermVat)
{
    public decimal FirstYearIncVat => FirstYearExVat + FirstYearVat;
    public decimal TermIncVat => TermExVat + TermVat;
    public static QuoteTotals Of(IEnumerable<PaymentLine> lines) => lines.Aggregate(new QuoteTotals(), (sum, x) =>
        new QuoteTotals(sum.FirstYearExVat + x.FirstYearExVat, sum.FirstYearVat + x.FirstYearVat, sum.TermExVat + x.TermExVat, sum.TermVat + x.TermVat));
    public static QuoteTotals operator +(QuoteTotals a, QuoteTotals b) =>
        new(a.FirstYearExVat + b.FirstYearExVat, a.FirstYearVat + b.FirstYearVat, a.TermExVat + b.TermExVat, a.TermVat + b.TermVat);
}
// One band of the school's finance policy: spending in this range needs this many quotes and meets these requirements
// (Settings → Spending bands). Shown on a project for reference, against the whole-contract total of its chosen quotes.
// UpTo null means no upper limit; both ends are inclusive, to the penny.
public record SpendingBand(Guid Id, string Name, decimal From, decimal? UpTo, int QuotesNeeded, string Requirements)
{
    public bool Contains(decimal amount) => amount >= From && (UpTo is null || amount <= UpTo);
}
public static class PaymentFrequencies
{
    public const string OneOff = "One-off";
    public const string Monthly = "Monthly";
    public const string Quarterly = "Quarterly";
    public const string Annually = "Annually";
    public static readonly string[] All = [OneOff, Monthly, Quarterly, Annually];
    public static int PerYear(string? frequency) => frequency switch { Monthly => 12, Quarterly => 4, Annually => 1, _ => 0 };
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
public static class VatTreatments
{
    public const string Standard = "Standard (20%)";
    public const string Reduced = "Reduced (5%)";
    public const string Zero = "Zero-rated (0%)";
    public const string None = "No VAT";
    public static readonly string[] All = [Standard, Reduced, Zero, None];
    public static decimal Rate(string? vat) => vat switch { Standard => 0.20m, Reduced => 0.05m, _ => 0m };
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
public static class QuoteStatuses
{
    public const string NotRequested = "Not requested";
    public const string Requested = "Requested";
    public const string Received = "Received";
    public const string UpdateRequested = "Update requested";
    public const string UpdateReceived = "Update received";
    public const string Declined = "Declined / no response";
    public static readonly string[] All = [NotRequested, Requested, Received, UpdateRequested, UpdateReceived, Declined];
    // A quote still outstanding after this many days is flagged for chasing.
    public const int ChaseAfterDays = 7;
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
// A shared note is seen by the requester in the portal; an internal one is for the IT team only, as on tickets.
public record ProjectNote(string Text, DateTime CreatedAt, bool IsInternal = false)
{
    public Actor? By { get; init; }
}
public record ProjectActivity(string Action, string Details, DateTime CreatedAt)
{
    public Actor? By { get; init; }
}
public static class ProjectStatuses
{
    public const string New = "New";
    public const string GatheringQuotes = "Gathering quotes";
    public const string ProposalReady = "Proposal ready";
    public const string Closed = "Closed";
    public static readonly string[] All = [New, GatheringQuotes, ProposalReady, Closed];
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
public static class ProjectOutcomes
{
    public const string Approved = "Approved";
    public const string NotApproved = "Not approved";
    public const string Cancelled = "Cancelled";
    public static readonly string[] All = [Approved, NotApproved, Cancelled];
    public static string? Find(string? value) => All.FirstOrDefault(x => string.Equals(x, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
public static class ProjectPriorities
{
    public const int Highest = 1;
    public const int Lowest = 5;
    public const int Default = 3;
    // Projects still gathering quotes and due within this many days are flagged on the list.
    public const int DueSoonDays = 7;
    public static readonly int[] All = [1, 2, 3, 4, 5];
    public static bool IsValid(int value) => value is >= Highest and <= Lowest;
    public static string Label(int value) => value switch
    {
        1 => "P1 · Urgent",
        2 => "P2 · High",
        3 => "P3 · Normal",
        4 => "P4 · Low",
        5 => "P5 · When possible",
        _ => $"P{value}"
    };
    // What each level means, shown when suggesting and confirming one, so the numbers are used the same way by everyone.
    public static string Describe(int value) => value switch
    {
        1 => "Teaching, safety or statutory work is affected now",
        2 => "Needed this half term",
        3 => "Needed this term",
        4 => "Needed this academic year",
        5 => "Nice to have, no deadline pressure",
        _ => ""
    };
}
// A file uploaded to a ticket. The file itself is kept on disk under App_Data/attachments, named by Id.
public record TicketAttachment(Guid Id, int TicketNumber, string FileName, string ContentType, long Size, DateTime UploadedAt)
{
    // Shown in the staff portal: everything the requester uploaded, and anything a technician chose to share.
    public bool VisibleToRequester { get; init; }
    // Uploaded by the requester through the portal.
    public bool FromRequester { get; init; }
}
// Kind is "related" (either direction) or "follow-up" (TicketNumber is the original ticket, LinkedNumber the follow-up).
public record TicketLink(int TicketNumber, int LinkedNumber, string Kind);

public sealed class BrandingSettings
{
    public string BrandName { get; set; } = "EduHelpdesk";
    // Defaults for a fresh install and a factory reset. A school that has already saved its branding keeps its own
    // wording - these are only read when nothing is stored.
    public string DashboardEyebrow { get; set; } = "Keep every school device moving.";
    public string DashboardTitle { get; set; } = "Overview";
    public string DashboardDescription { get; set; } = "Log and track jobs, link them to assets, and keep a complete repair history. Staff can report problems and follow their own tickets through the staff portal.";
    public string PrimaryColor { get; set; } = "#067A78";
    public string AccentColor { get; set; } = "#E8F0EF";
    public string BackgroundColor { get; set; } = "#F5F8F8";
    // How the helpdesk looks on a device that hasn't picked for itself (the Appearance switch in the footer). Stored in
    // the old DarkMode column: 0 and 1 were off and on, so an upgraded install keeps the look it had.
    public Appearance DefaultAppearance { get; set; } = Appearance.Device;
}

public enum Appearance { Light = 0, Dark = 1, Device = 2 }
