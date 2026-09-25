using System.Globalization;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// A supplier's quote for one item: its files, what it costs (payment lines), which quote the item goes with, and the
// versions an update replaced. Files use the ticket attachment rules and folder, so backups and factory reset already
// cover them.
public sealed partial class HelpdeskStore
{
    public const int MaxPaymentLines = 20;
    public const int MaxPaymentTermYears = 10;
    public const decimal MaxPaymentAmount = 10_000_000m;
    public const int MaxQuoteReferenceLength = 100;
    private static readonly CultureInfo Money = CultureInfo.GetCultureInfo("en-GB");

    public static string FormatMoney(decimal value) => value.ToString("C2", Money);

    // Adds a supplier's quote files. The first quote from a supplier marks them Received. When an update was asked for,
    // the files replace the ones held - prices too, because they belong to the old quote - and keepPrevious files the
    // old quote away, files and prices together, under Previous versions.
    public (bool Ok, string Message) UploadQuoteDocuments(int number, Guid itemId, Guid supplierId, IReadOnlyList<(string? FileName, Stream Content, long Length)> files, bool keepPrevious)
    {
        if (files.Count == 0) return (false, "Choose the quote files to upload.");
        if (files.Count > MaxAttachmentsPerUpload) return (false, $"Upload up to {MaxAttachmentsPerUpload} files at a time.");
        var checkedFiles = new List<(string Name, string ContentType, Stream Content)>();
        foreach (var file in files)
        {
            var name = CleanFileName(file.FileName);
            if (name.Length == 0) return (false, "A file with no name couldn't be added.");
            var extension = Path.GetExtension(name);
            if (!AttachmentTypes.TryGetValue(extension, out var contentType))
                return (false, $"Nothing was uploaded: {name} {(extension.Length == 0 ? "has no file type" : $"is a {extension} file, which isn't accepted")}.");
            if (file.Length <= 0) return (false, $"Nothing was uploaded: {name} is empty.");
            if (file.Length > MaxAttachmentBytes) return (false, $"Nothing was uploaded: {name} is larger than {MaxAttachmentBytes / (1024 * 1024)} MB.");
            checkedFiles.Add((name, contentType, file.Content));
        }

        // Written to disk before the record changes, and removed again if the change is refused or a write fails, so
        // a failed upload never leaves either a stray file or a record pointing at nothing.
        var saved = new List<QuoteDocument>();
        var actor = CurrentActor();
        try
        {
            Directory.CreateDirectory(AttachmentsPath);
            foreach (var (name, contentType, content) in checkedFiles)
            {
                var id = Guid.NewGuid();
                using var target = File.Create(AttachmentFile(id));
                content.CopyTo(target);
                if (target.Length > MaxAttachmentBytes)
                {
                    target.Dispose();
                    TryDelete(AttachmentFile(id));
                    foreach (var done in saved) TryDelete(AttachmentFile(done.Id));
                    return (false, $"Nothing was uploaded: {name} is larger than {MaxAttachmentBytes / (1024 * 1024)} MB.");
                }
                saved.Add(new QuoteDocument(id, name, contentType, target.Length, DateTime.UtcNow) { By = actor });
            }
        }
        catch (IOException ex)
        {
            foreach (var done in saved) TryDelete(AttachmentFile(done.Id));
            return (false, $"The files couldn't be saved ({ex.Message}).");
        }

        var replaced = new List<Guid>();
        var result = EditSupplier(number, itemId, supplierId, (item, row, supplierName) =>
        {
            var names = string.Join(", ", saved.Select(x => x.FileName));
            if (row.Status == QuoteStatuses.UpdateRequested)
            {
                var archive = keepPrevious && (row.Documents.Count > 0 || row.PaymentLines.Count > 0);
                if (!archive) replaced.AddRange(row.Documents.Select(x => x.Id));
                var updated = row with
                {
                    Documents = saved,
                    PaymentLines = [],
                    ValidUntil = null,
                    Reference = "",
                    PreviousVersions = archive
                        ? [.. row.PreviousVersions, new QuoteVersion(Guid.NewGuid(), DateTime.UtcNow, row.Reference, row.ValidUntil) { Documents = row.Documents, PaymentLines = row.PaymentLines, By = actor }]
                        : row.PreviousVersions,
                    StatusHistory = [.. row.StatusHistory, new QuoteStatusChange(QuoteStatuses.UpdateReceived, DateTime.UtcNow) { By = actor }]
                };
                return (updated,
                    $"Updated quote from {supplierName} saved. {(archive ? "The previous version is kept under Previous versions." : "The previous files were removed.")} Enter the new prices below.",
                    $"Updated quote from {supplierName} for {item.Name}: {names}. {(archive ? "Previous version kept." : "Previous version removed.")} Status: {row.Status} → {QuoteStatuses.UpdateReceived}.");
            }
            var markReceived = row.Status is not (QuoteStatuses.Received or QuoteStatuses.UpdateReceived);
            var added = row with
            {
                Documents = [.. row.Documents, .. saved],
                StatusHistory = markReceived ? [.. row.StatusHistory, new QuoteStatusChange(QuoteStatuses.Received, DateTime.UtcNow) { By = actor }] : row.StatusHistory
            };
            return (added,
                $"{saved.Count} {(saved.Count == 1 ? "file" : "files")} added to {supplierName}'s quote.{(markReceived ? " Marked as Received." : "")}",
                $"Quote files from {supplierName} for {item.Name}: {names}.{(markReceived ? $" Status: {row.Status} → {QuoteStatuses.Received}." : "")}");
        });
        if (!result.Ok) foreach (var done in saved) TryDelete(AttachmentFile(done.Id));
        else foreach (var id in replaced) TryDelete(AttachmentFile(id));
        return result;
    }

    public (bool Ok, string Message) RemoveQuoteDocument(int number, Guid itemId, Guid supplierId, Guid documentId)
    {
        var removed = false;
        var result = EditSupplier(number, itemId, supplierId, (item, row, name) =>
        {
            var document = row.Documents.FirstOrDefault(x => x.Id == documentId);
            if (document is null) return RefuseSupplier("That file couldn't be found.");
            removed = true;
            return (row with { Documents = row.Documents.Where(x => x.Id != documentId).ToList() }, $"{document.FileName} removed.",
                $"Quote file removed from {name}'s quote for {item.Name}: {document.FileName}.");
        });
        if (result.Ok && removed) TryDelete(AttachmentFile(documentId));
        return result;
    }

    // Drops an archived version altogether, files included - for one kept by mistake.
    public (bool Ok, string Message) DeleteQuoteVersion(int number, Guid itemId, Guid supplierId, Guid versionId)
    {
        var files = new List<Guid>();
        var result = EditSupplier(number, itemId, supplierId, (item, row, name) =>
        {
            var version = row.PreviousVersions.FirstOrDefault(x => x.Id == versionId);
            if (version is null) return RefuseSupplier("That previous version couldn't be found.");
            files.AddRange(version.Documents.Select(x => x.Id));
            return (row with { PreviousVersions = row.PreviousVersions.Where(x => x.Id != versionId).ToList() }, "Previous version deleted.",
                $"Previous version of {name}'s quote for {item.Name} (kept {Day(DateOnly.FromDateTime(version.ArchivedAt.ToLocalTime()))}) deleted.");
        });
        if (result.Ok) foreach (var id in files) TryDelete(AttachmentFile(id));
        return result;
    }

    public (bool Ok, string Message) SetQuoteReference(int number, Guid itemId, Guid supplierId, string? reference) => EditSupplier(number, itemId, supplierId, (item, row, name) =>
    {
        var value = (reference ?? string.Empty).Trim();
        if (value.Length > MaxQuoteReferenceLength) return RefuseSupplier($"Keep the quote reference under {MaxQuoteReferenceLength} characters.");
        if (value == row.Reference) return RefuseSupplier("Nothing had changed.");
        return (row with { Reference = value }, value.Length == 0 ? "Quote reference cleared." : $"Quote reference set to {value}.",
            $"{name}'s quote reference for {item.Name}: {(row.Reference.Length == 0 ? "none" : row.Reference)} → {(value.Length == 0 ? "none" : value)}.");
    });

    public (bool Ok, string Message) AddPaymentLine(int number, Guid itemId, Guid supplierId, string? description, string? amount, string? frequency, int termYears, string? vat) =>
        EditSupplier(number, itemId, supplierId, (item, row, name) =>
        {
            if (row.PaymentLines.Count >= MaxPaymentLines) return RefuseSupplier($"A quote can have up to {MaxPaymentLines} payment lines.");
            if (CheckPaymentLine(Guid.NewGuid(), description, amount, frequency, termYears, vat) is not { } line) return RefuseSupplier(PaymentLineError(description, amount, frequency, termYears, vat));
            return (row with { PaymentLines = [.. row.PaymentLines, line] }, "Payment line added.", $"{name}'s price for {item.Name}: added {DescribeLine(line)}.");
        });

    public (bool Ok, string Message) UpdatePaymentLine(int number, Guid itemId, Guid supplierId, Guid lineId, string? description, string? amount, string? frequency, int termYears, string? vat) =>
        EditSupplier(number, itemId, supplierId, (item, row, name) =>
        {
            var old = row.PaymentLines.FirstOrDefault(x => x.Id == lineId);
            if (old is null) return RefuseSupplier("That payment line couldn't be found.");
            if (CheckPaymentLine(lineId, description, amount, frequency, termYears, vat) is not { } line) return RefuseSupplier(PaymentLineError(description, amount, frequency, termYears, vat));
            if (line == old) return RefuseSupplier("Nothing had changed.");
            return (row with { PaymentLines = row.PaymentLines.Select(x => x.Id == lineId ? line : x).ToList() }, "Payment line updated.",
                $"{name}'s price for {item.Name}: {DescribeLine(old)} → {DescribeLine(line)}.");
        });

    public (bool Ok, string Message) DeletePaymentLine(int number, Guid itemId, Guid supplierId, Guid lineId) => EditSupplier(number, itemId, supplierId, (item, row, name) =>
    {
        var line = row.PaymentLines.FirstOrDefault(x => x.Id == lineId);
        if (line is null) return RefuseSupplier("That payment line couldn't be found.");
        return (row with { PaymentLines = row.PaymentLines.Where(x => x.Id != lineId).ToList() }, "Payment line removed.", $"{name}'s price for {item.Name}: removed {DescribeLine(line)}.");
    });

    // Which quote the item goes with. Only a supplier who has actually quoted can be chosen; null clears the choice.
    public (bool Ok, string Message) ChooseQuote(int number, Guid itemId, Guid? supplierId) => EditItem(number, itemId, item =>
    {
        if (supplierId == item.ChosenSupplierId) return RefuseItem("Nothing had changed.");
        if (supplierId is null)
            return (item with { ChosenSupplierId = null }, "No quote is chosen for this item now.", $"Chosen quote for {item.Name} cleared (was {SupplierName(item.ChosenSupplierId!.Value)}).");
        var row = item.Suppliers.FirstOrDefault(x => x.SupplierId == supplierId);
        if (row is null) return RefuseItem("That supplier isn't on this item.");
        if (!row.HasQuote) return RefuseItem($"{SupplierName(row.SupplierId)} hasn't quoted yet - add their quote first.");
        return (item with { ChosenSupplierId = supplierId }, $"{SupplierName(row.SupplierId)}'s quote chosen for {item.Name}.",
            $"Chosen quote for {item.Name}: {(item.ChosenSupplierId is { } was ? SupplierName(was) : "none")} → {SupplierName(row.SupplierId)} ({FormatMoney(QuoteTotals.Of(row.PaymentLines).TermIncVat)} over the term inc. VAT).");
    });

    // A quote file by id - current or archived - with where it is on disk, or null if it isn't on that supplier's quote
    // for that project or the file has gone missing.
    public (QuoteDocument Document, string Path)? FindQuoteDocument(int number, Guid itemId, Guid supplierId, Guid documentId)
    {
        lock (_sync)
        {
            var row = _data.Projects.FirstOrDefault(x => x.Number == number)?.Items.FirstOrDefault(x => x.Id == itemId)?.Suppliers.FirstOrDefault(x => x.SupplierId == supplierId);
            var document = row?.Documents.Concat(row.PreviousVersions.SelectMany(v => v.Documents)).FirstOrDefault(x => x.Id == documentId);
            if (document is null) return null;
            var path = AttachmentFile(documentId);
            return File.Exists(path) ? (document, path) : null;
        }
    }

    public static bool IsInlineImage(QuoteDocument document) => InlineImageTypes.Contains(document.ContentType);

    // Every file behind a set of supplier rows, archived versions included - for removing them with their record.
    private static IEnumerable<Guid> QuoteFiles(IEnumerable<ItemSupplier> rows) =>
        rows.SelectMany(x => x.Documents.Concat(x.PreviousVersions.SelectMany(v => v.Documents))).Select(x => x.Id);

    private static PaymentLine? CheckPaymentLine(Guid id, string? description, string? amount, string? frequency, int termYears, string? vat)
    {
        var text = (description ?? string.Empty).Trim();
        if (text.Length > MaxItemNameLength || ParseAmount(amount) is not { } value || PaymentFrequencies.Find(frequency) is not { } often || VatTreatments.Find(vat) is not { } tax) return null;
        var oneOff = often == PaymentFrequencies.OneOff;
        if (!oneOff && termYears is < 1 or > MaxPaymentTermYears) return null;
        return new PaymentLine(id, text, value, often, oneOff ? 1 : termYears, tax);
    }

    private static string PaymentLineError(string? description, string? amount, string? frequency, int termYears, string? vat) =>
        (description ?? string.Empty).Trim().Length > MaxItemNameLength ? $"Keep the description under {MaxItemNameLength} characters."
        : ParseAmount(amount) is null ? $"Enter the amount as a number of pounds, up to {FormatMoney(MaxPaymentAmount)} - e.g. 8400 or 8,400.00."
        : PaymentFrequencies.Find(frequency) is null ? "Choose how often it is paid."
        : VatTreatments.Find(vat) is null ? "Choose the VAT that applies."
        : $"Enter a term from 1 to {MaxPaymentTermYears} years.";

    // Pounds as typed: "8400", "8,400.00", "£8,400". Rounded to pence.
    private static decimal? ParseAmount(string? value)
    {
        var text = (value ?? string.Empty).Replace("£", "").Replace(",", "").Replace(" ", "").Trim();
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount is >= 0 and <= MaxPaymentAmount
            ? Math.Round(amount, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    public static string DescribeLine(PaymentLine line) =>
        (line.Description.Length > 0 ? line.Description + ": " : "") + FormatMoney(line.Amount)
        + (line.Frequency == PaymentFrequencies.OneOff ? " one-off" : $" {line.Frequency.ToLowerInvariant()} for {line.TermYears} {(line.TermYears == 1 ? "year" : "years")}")
        + $", {line.Vat}";
}
