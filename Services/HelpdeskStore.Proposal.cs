using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// What the proposal PDF is made from, gathered in one go under the lock so the document is one consistent moment of
// the project. ProposalPdf does the drawing; it never reaches back into the store.
public sealed partial class HelpdeskStore
{
    public ProposalInput? ProposalFor(int number)
    {
        lock (_sync)
        {
            var project = _data.Projects.FirstOrDefault(x => x.Number == number);
            if (project is null) return null;
            var suppliers = project.Items.SelectMany(x => x.Suppliers).Select(x => x.SupplierId).Distinct().ToDictionary(x => x, SupplierName);
            var files = project.Items.SelectMany(x => x.Suppliers).SelectMany(x => x.Documents)
                .Select(x => (x.Id, Path: AttachmentFile(x.Id))).Where(x => File.Exists(x.Path)).ToDictionary(x => x.Id, x => x.Path);
            var total = BandTotal(project);
            return new ProposalInput(
                project,
                _data.Branding.BrandName,
                _data.Branding.PrimaryColor,
                LogoFile,
                _data.Users.FirstOrDefault(x => x.Id == project.RequesterId)?.Name ?? "Unknown",
                project.TechnicianId is { } tech ? _data.Technicians.FirstOrDefault(x => x.Id == tech)?.Name : null,
                suppliers,
                files,
                SpendingBands,
                SpendingBandsIncludeVat,
                total,
                // Nothing chosen is "no band yet", not the bottom band that £0.00 happens to fall in.
                project.Items.Any(x => x.Chosen is not null) ? BandFor(total) : null,
                DateTime.Now);
        }
    }

    // The requester and the project lead can download the proposal from the portal once the technician has marked it
    // ready - and still after it is closed, as long as it had been ready: approved or not, it is the paper the decision
    // was made on. A project cancelled before then never had a proposal to share.
    public static bool ProposalOpenToPortal(ProjectRecord project) =>
        project.Status == ProjectStatuses.ProposalReady
        || (project.Status == ProjectStatuses.Closed && project.History.Any(x =>
            x.Details.Contains($"→ {ProjectStatuses.ProposalReady}.", StringComparison.Ordinal)
            || x.Details.StartsWith($"Reopened as {ProjectStatuses.ProposalReady}.", StringComparison.Ordinal)));
}

public sealed record ProposalInput(
    ProjectRecord Project,
    string OrganisationName,
    string PrimaryColor,
    string? LogoPath,
    string RequesterName,
    string? TechnicianName,
    IReadOnlyDictionary<Guid, string> SupplierNames,
    IReadOnlyDictionary<Guid, string> FilePaths,
    IReadOnlyList<SpendingBand> Bands,
    bool BandsIncludeVat,
    decimal BandTotal,
    SpendingBand? Band,
    DateTime PreparedAt);
