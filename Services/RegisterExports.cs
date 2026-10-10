using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// A table ready to write out: column headings and rows of typed cells. One shape feeds both the CSV and the Excel
// writer, so every register can be had either way and the two always match.
public sealed record ExportTable(string SheetName, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows);

// The three DfE registers, written in the DfE templates' own column order and headings (merged from EduInventory), so
// an export can be pasted into the template or handed to anyone who knows it - and the imports read the same headings
// back. Columns the templates don't have follow after theirs.
public static class RegisterExports
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");
    private static string YesNo(bool value) => value ? "Yes" : "No";

    // The asset template's headings, for finding the header row when its .xlsx is imported (Pages/Assets/Import).
    public static readonly string[] AssetHeaders =
    [
        "Asset type", "Asset number", "Make of asset", "Model of asset", "Serial number", "Operating system", "Condition of asset",
        "Asset status", "Date of last check or replacement", "Date of next check or replacement", "Location: Building", "Location: Room",
        "Purchase date", "Purchase price (£)", "Supplier of the asset", "Asset tag", "Make", "Model", "Type", "Status", "Location"
    ];

    // The DfE asset template's fictional examples, recognised by their serial numbers.
    public static readonly string[] ExampleAssetSerials = ["D12398765x", "AS987612435D", "IP34567654Z", "RAMP341290d", "HPE9876T543E", "VS7654321A"];

    public static ExportTable AssetRegister(HelpdeskStore store, DateOnly today)
    {
        var checks = store.AssetCheckSettings;
        var people = store.Users.ToDictionary(x => x.Id, x => x.Name);
        var suppliers = store.Suppliers.ToDictionary(x => x.Id, x => x.Name);
        var contracts = store.Contracts.ToDictionary(x => x.Id, x => x.Name);
        string[] headers =
        [
            "Asset type", "Asset number", "Make of asset", "Model of asset", "Serial number", "Operating system", "Condition of asset",
            "Asset status", "Who decommissioned/ disposed of this asset and when?", "Date of last check or replacement",
            "Person responsible for the last check or replacement", "Date of next check or replacement", "Overdue status",
            "When does the asset expire or become unsupported?", "Location: Building", "Location: Room", "Who is the asset assigned to?",
            "Purchase date", "Purchase price (£)", "Supplier of the asset", "Is the asset owned by your school or college?",
            "Is this asset leased or loaned?", "Person completing this entry", "Additional comments",
            "Disposal date", "Disposal method", "Data destruction / WEEE certificate", "Lease or loan contract", "Support status", "Warranty end", "Purchase order"
        ];
        var rows = store.Assets.OrderBy(x => x.AssetTag, NaturalComparer.Instance).Select(a =>
        {
            var live = !HelpdeskStore.IsDisposed(a);
            var check = live ? AssetChecks.CheckState(a, today, checks.DueSoonDays) : AssetChecks.State.None;
            var support = live ? AssetChecks.SupportState(a, today, checks.SupportWarningDays) : AssetChecks.State.None;
            var disposed = string.Join(", ", new[] { a.DisposedBy, a.DisposalDate is { } d ? d.ToString("d MMM yyyy", Uk) : "" }.Where(x => x.Length > 0));
            var leased = a.Ownership == AssetOwnership.Leased;
            return new object?[]
            {
                a.Type, a.AssetTag, a.Make, a.Model, a.SerialNumber, a.OperatingSystem, a.Condition, a.Status, disposed,
                a.LastCheckDate, a.LastCheckBy, a.NextCheckDate,
                check is AssetChecks.State.Overdue ? "Overdue" : check is AssetChecks.State.Soon ? "Due soon" : "",
                a.EndOfSupport, a.Building, a.Location, a.AssignedUserId is { } holder ? people.GetValueOrDefault(holder, "") : "",
                a.PurchaseDate,
                // DfE: "If the asset is leased, then enter 'leased'".
                a.PurchasePrice is { } price ? price : leased ? "Leased" : null,
                a.SupplierId is { } supplier ? suppliers.GetValueOrDefault(supplier, "") : "",
                YesNo(string.IsNullOrEmpty(a.Ownership)), a.Ownership, "", "",
                a.DisposalDate, a.DisposalMethod, a.DisposalCertificate,
                a.ContractId is { } contract ? contracts.GetValueOrDefault(contract, "") : "",
                support is AssetChecks.State.Overdue or AssetChecks.State.Soon ? AssetChecks.SupportLabel(a, support) : "",
                a.WarrantyEnd, a.PurchaseOrder
            };
        }).ToList();
        return new ExportTable("Digital technology assets", headers, rows);
    }

    public static ExportTable ContractsRegister(HelpdeskStore store, DateOnly today)
    {
        var suppliers = store.Suppliers.ToDictionary(x => x.Id, x => x.Name);
        string[] headers =
        [
            "Name of contract or service", "Description of contract or service", "Contract type", "Spend category", "Supplier contact",
            "Cost", "Duration", "Renewal frequency", "Contract start date", "Contract end date", "Next renewal date", "Renewal status",
            "Notice required (in months) for renewal or cancellation", "Notice required status",
            "Contract owner (within the school or college)", "Procurement approach used", "Contract status", "Additional comments",
            "Supplier", "Cost (£)", "Cost period", "Cost notes", "Annual cost (£)", "Notice date", "Approved app", "Processes personal data",
            "Related party transaction", "Reported to DfE on"
        ];
        var rows = store.Contracts.Select(c => new object?[]
        {
            // The Supplier column further along carries the company, so this one holds just the contact and re-imports as itself.
            c.Name, c.Description, c.ContractType, c.SpendCategory, c.SupplierContact,
            CostText(c), c.Duration, c.RenewalType, c.StartDate, c.EndDate, c.NextRenewalDate,
            ContractRules.Renewal(c, today)?.Text ?? "", c.NoticeMonths, ContractRules.Notice(c, today)?.Text ?? "",
            c.ContractOwner, c.ProcurementApproach, c.Status, c.Notes,
            c.SupplierId is { } supplier ? suppliers.GetValueOrDefault(supplier, "") : "", c.Cost, c.CostPeriod, c.CostNotes,
            ContractRules.AnnualCost(c), ContractRules.NoticeDate(c), YesNo(c.ApprovedApp), YesNo(c.ProcessesPersonalData),
            YesNo(c.RelatedParty), c.RelatedPartyReportedOn
        }).ToList();
        return new ExportTable("Contracts", headers, rows);
    }

    // "£10,500.00 pa" - the DfE column's free-text style, which the import reads back.
    public static string CostText(ContractRecord c)
    {
        if (c.Cost is not { } cost) return c.CostNotes;
        var period = c.CostPeriod switch
        {
            ContractCostPeriods.PerMonth => " pm", ContractCostPeriods.PerQuarter => " per quarter", ContractCostPeriods.PerYear => " pa",
            ContractCostPeriods.OneOff => " one-off", _ => " total"
        };
        return cost.ToString("C2", Uk) + period;
    }

    public static ExportTable AccessRegister(HelpdeskStore store, DateOnly today)
    {
        var people = store.Users.ToDictionary(x => x.Id);
        var resources = store.AccessResources.ToDictionary(x => x.Id);
        string[] headers =
        [
            "Person", "Email", "Type", "Department", "Leave date", "System or area", "System or physical", "Category", "Access level",
            "Account / key / fob number", "Privileged (administrator) access", "MFA", "Granted on", "Granted by", "Approved by (SLT or trustee)",
            "Approved on", "Last reviewed on", "Last reviewed by", "Revoked or returned on", "Revoked by", "Reason for removal",
            "Status", "Issues", "Notes"
        ];
        var rows = store.AccessGrants.Select(g =>
        {
            people.TryGetValue(g.PersonId, out var p);
            resources.TryGetValue(g.ResourceId, out var r);
            return new object?[]
            {
                p?.Name, p?.Email, p is null ? "" : AccessRules.PersonType(p), p?.Department, p is null ? null : AccessRules.LeaveDate(p),
                r?.Name, r is null ? "" : AccessKinds.Label(r.Kind), r?.Category, g.AccessLevel, g.Identifier, YesNo(g.Privileged), g.Mfa,
                g.GrantedOn, g.GrantedBy, g.ApprovedBy, g.ApprovedOn, g.LastReviewedOn, g.LastReviewedBy, g.RevokedOn, g.RevokedBy, g.RevokeReason,
                g.IsActive(today) ? "Active" : "Removed",
                string.Join("; ", AccessRules.Issues(g, p, r, today, store.AccessReviewDays).Select(x => x.Text)), g.Notes
            };
        }).OrderBy(x => x[0] as string, NaturalComparer.Instance).ToList();
        return new ExportTable("Access register", headers, rows);
    }

    // ---- Writers ----

    public static byte[] ToCsv(ExportTable table)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", table.Headers.Select(Escape))).Append("\r\n");
        foreach (var row in table.Rows) builder.Append(string.Join(",", row.Select(x => Escape(CsvValue(x))))).Append("\r\n");
        // With a BOM, so Excel opens it as UTF-8 and the £ signs survive.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static string CsvValue(object? value) => value switch
    {
        null => "",
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };

    private static string Escape(string value)
    {
        // A leading =, +, - or @ would be run as a formula when the file is opened in Excel.
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    // A plain workbook: a bold frozen header row with a filter, real dates and money (so Excel's date filters and sums
    // work), and sensible column widths.
    public static byte[] ToXlsx(ExportTable table)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
            styles.Stylesheet = BuildStylesheet();

            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            var columns = new Columns();
            for (var i = 0; i < table.Headers.Count; i++)
            {
                var widest = table.Rows.Select(r => CsvValue(i < r.Length ? r[i] : null).Length).DefaultIfEmpty(0).Max();
                var width = Math.Clamp(Math.Max(table.Headers[i].Length * 0.8, widest + 2), 10, 60);
                columns.Append(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = width, CustomWidth = true });
            }
            var header = new Row { RowIndex = 1 };
            foreach (var h in table.Headers) header.Append(TextCell(h, StyleHeader));
            data.Append(header);
            uint rowIndex = 2;
            foreach (var values in table.Rows)
            {
                var row = new Row { RowIndex = rowIndex++ };
                foreach (var value in values) row.Append(ValueCell(value));
                data.Append(row);
            }
            var sheetView = new SheetView { WorkbookViewId = 0 };
            sheetView.Append(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });
            var worksheet = new Worksheet(new SheetViews(sheetView), columns, data);
            if (table.Rows.Count > 0) worksheet.Append(new AutoFilter { Reference = $"A1:{ColumnName(table.Headers.Count)}{table.Rows.Count + 1}" });
            sheetPart.Worksheet = worksheet;

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = 1, Name = SafeSheetName(table.SheetName) });
            if (table.Rows.Count > 0)
            {
                var names = new DefinedNames();
                names.Append(new DefinedName($"'{SafeSheetName(table.SheetName)}'!$A$1:${ColumnName(table.Headers.Count)}${table.Rows.Count + 1}")
                    { Name = "_xlnm._FilterDatabase", LocalSheetId = 0, Hidden = true });
                workbookPart.Workbook.Append(names);
            }
            workbookPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    private const uint StyleHeader = 1, StyleDate = 2, StyleMoney = 3, StyleDateTime = 4;

    private static Stylesheet BuildStylesheet() => new(
        new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "dd/mm/yyyy" },
            new NumberingFormat { NumberFormatId = 165, FormatCode = "\"£\"#,##0.00" },
            new NumberingFormat { NumberFormatId = 166, FormatCode = "dd/mm/yyyy hh:mm" }) { Count = 3 },
        new Fonts(new Font(), new Font(new Bold(), new Color { Rgb = "FFFFFFFF" })) { Count = 2 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF067A78" }) { PatternType = PatternValues.Solid })) { Count = 3 },
        new Borders(new Border()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true, Alignment = new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top } },
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 165, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 166, ApplyNumberFormat = true }) { Count = 5 });

    private static Cell TextCell(string text, uint style = 0) =>
        new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }), StyleIndex = style };

    private static Cell ValueCell(object? value) => value switch
    {
        null => new Cell(),
        DateOnly d => new Cell { CellValue = new CellValue(d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)), StyleIndex = StyleDate },
        DateTime dt => new Cell { CellValue = new CellValue(dt.ToOADate().ToString(CultureInfo.InvariantCulture)), StyleIndex = StyleDateTime },
        decimal m => new Cell { CellValue = new CellValue(m.ToString(CultureInfo.InvariantCulture)), DataType = CellValues.Number, StyleIndex = StyleMoney },
        int i => new Cell { CellValue = new CellValue(i.ToString(CultureInfo.InvariantCulture)), DataType = CellValues.Number },
        bool b => TextCell(b ? "Yes" : "No"),
        _ => TextCell(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")
    };

    private static string ColumnName(int index)
    {
        var name = "";
        while (index > 0) { var m = (index - 1) % 26; name = (char)('A' + m) + name; index = (index - m) / 26; }
        return name;
    }

    private static string SafeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    public static string FileName(string register, string extension) => $"{register}-{DateTime.Now:yyyy-MM-dd}.{extension}";
}
