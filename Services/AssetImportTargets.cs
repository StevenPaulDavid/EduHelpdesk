using System.Globalization;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

public sealed record AssetImportOptions(string DateFormat, bool CreateMissing, bool BlankClears, string? DefaultType, string? DefaultStatus);

public enum ImportAction { Create, Update, Unchanged, Error }

public sealed record ImportRowResult(int RowNumber, string Tag, ImportAction Action, IReadOnlyList<string> Changes, IReadOnlyList<string> Warnings, string? Error);

public sealed record AssetImportPlan(IReadOnlyList<ImportRowResult> Results, IReadOnlyList<(string List, string Value)> NewValues)
{
    public int Created => Results.Count(x => x.Action == ImportAction.Create);
    public int Updated => Results.Count(x => x.Action == ImportAction.Update);
    public int Unchanged => Results.Count(x => x.Action == ImportAction.Unchanged);
    public int Errors => Results.Count(x => x.Action == ImportAction.Error);
    public int Warnings => Results.Sum(x => x.Warnings.Count);
}

// The asset fields a spreadsheet column can be mapped to, and guesses at which column is which from its heading.
public static class AssetImportTargets
{
    public const string AttributePrefix = "attr:";

    public static readonly (string Key, string Label)[] Fields =
    [
        ("tag", "Asset tag (required)"), ("make", "Make"), ("model", "Model"), ("type", "Type"), ("serial", "Serial number"), ("status", "Status"),
        ("location", "Location"), ("holder", "Assigned to (name or email)"), ("loanDue", "Loan due back"), ("supplier", "Supplier"),
        ("purchaseDate", "Purchase date"), ("purchasePrice", "Purchase price"), ("purchaseOrder", "Purchase order"), ("quoteReference", "Quote reference"),
        ("warrantyEnd", "Warranty end"), ("replacementDate", "Replacement date"),
        ("building", "Building"), ("os", "Operating system"), ("condition", "Condition"), ("ownership", "Ownership (owned, leased or loaned)"),
        ("lastCheck", "Last check"), ("lastCheckBy", "Checked by"), ("nextCheck", "Next check"), ("endOfSupport", "End of support")
    ];

    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["tag"] = ["assettag", "tag", "assetid", "assetnumber", "assetno", "assettagnumber", "inventorynumber", "inventoryno", "inventoryid", "devicetag", "barcode"],
        ["serial"] = ["serial", "serialnumber", "serialno", "serialnum", "sn", "servicetag"],
        ["make"] = ["make", "manufacturer", "brand", "oem"],
        ["model"] = ["model", "modelname", "devicemodel", "productmodel"],
        ["type"] = ["type", "assettype", "devicetype", "category", "assetcategory", "class", "devicecategory"],
        ["status"] = ["status", "assetstatus", "devicestatus", "state", "lifecycle", "lifecyclestatus"],
        ["location"] = ["location", "room", "roomname", "assetlocation"],
        ["building"] = ["building", "buildingname", "site", "campus", "block"],
        ["os"] = ["os", "operatingsystem", "osversion", "operatingsystemversion"],
        ["condition"] = ["condition", "assetcondition"],
        ["ownership"] = ["ownership", "ownedbyschool", "ownedorleased", "leasedorloaned", "leased"],
        ["lastCheck"] = ["lastcheck", "lastcheckdate", "dateoflastcheck", "lastchecked", "dateoflastcheckorreplacement", "lastcheckorreplacement"],
        ["lastCheckBy"] = ["checkedby", "lastcheckby", "lastcheckedby"],
        ["nextCheck"] = ["nextcheck", "nextcheckdate", "dateofnextcheck", "nextcheckdue"],
        ["endOfSupport"] = ["endofsupport", "endofsupportdate", "supportends", "supportenddate", "unsupportedfrom", "expirydate", "dateofexpiry", "expiryorunsupported"],
        ["holder"] = ["assignedto", "assigneduser", "user", "owner", "primaryuser", "holder", "heldby", "assignee", "userprincipalname", "upn", "useremail", "owneremail", "primaryuseremail", "currentuser", "checkedoutto"],
        ["loanDue"] = ["loanduedate", "loandueback", "dueback", "loandue", "returndate"],
        ["supplier"] = ["supplier", "vendor", "reseller", "purchasedfrom"],
        ["purchaseDate"] = ["purchasedate", "dateofpurchase", "datepurchased", "purchased", "acquired", "acquisitiondate", "bought", "orderdate", "purchasedon"],
        ["purchasePrice"] = ["purchaseprice", "price", "cost", "purchasecost", "unitcost", "unitprice", "amount"],
        ["purchaseOrder"] = ["purchaseorder", "po", "ponumber", "pono", "purchaseordernumber", "purchaseorderno", "invoice", "invoicenumber"],
        ["quoteReference"] = ["quotereference", "quote", "quoteno", "quotenumber", "quoteref", "supplierquote"],
        ["warrantyEnd"] = ["warrantyend", "warrantyenddate", "warrantyexpiry", "warrantyexpirydate", "warrantyexpires", "warrantyexpiration", "warrantyexpirationdate", "warrantyuntil", "warranty"],
        ["replacementDate"] = ["replacementdate", "replaceby", "replacedate", "refreshdate", "endoflife", "eol", "replacementdue", "replaceon"]
    };

    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // One guess per column, never using the same field twice. Null means "don't import this column".
    public static string?[] Suggest(IReadOnlyList<string> headers, IReadOnlyList<AssetAttributeDefinition> attributes)
    {
        var used = new HashSet<string>();
        var result = new string?[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            var name = Normalize(headers[i]);
            if (name.Length == 0) continue;
            var target = Synonyms.FirstOrDefault(x => x.Value.Contains(name)).Key;
            if (target is null)
            {
                var attribute = attributes.FirstOrDefault(a => Normalize(a.Name) == name);
                if (attribute is not null) target = AttributePrefix + attribute.Id;
            }
            if (target is not null && used.Add(target)) result[i] = target;
        }
        return result;
    }

    public static bool IsKnown(string? target, IReadOnlyList<AssetAttributeDefinition> attributes) =>
        target is not null && (Fields.Any(x => x.Key == target) || (target.StartsWith(AttributePrefix) && Guid.TryParse(target[AttributePrefix.Length..], out var id) && attributes.Any(a => a.Id == id)));
}

// Reads dates and prices written the way different systems write them.
public static class ImportParsing
{
    public static readonly (string Key, string Label)[] DateFormats =
    [
        ("dmy", "Day / Month / Year (for example 31/01/2026)"),
        ("mdy", "Month / Day / Year (for example 01/31/2026)"),
        ("iso", "Year-Month-Day (for example 2026-01-31)")
    ];

    private static readonly string[] Iso = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd"];
    private static readonly string[] DayFirst = ["d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yy", "d-M-yy", "d MMM yyyy", "d MMMM yyyy", "dd-MMM-yyyy", "d-MMM-yyyy", "d MMM yy"];
    private static readonly string[] MonthFirst = ["M/d/yyyy", "M-d-yyyy", "M/d/yy", "MMM d, yyyy", "MMMM d, yyyy", "MMM d yyyy"];

    // ISO dates are always accepted; the chosen order decides how 03/04/2026 is read. A time after the date is ignored.
    public static bool TryParseDate(string? text, string format, out DateOnly date)
    {
        date = default;
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) return false;
        var split = value.IndexOfAny([' ', 'T']);
        if (split > 0 && value[(split + 1)..].Contains(':')) value = value[..split];
        var formats = Iso.Concat(format == "mdy" ? MonthFirst : format == "iso" ? [] : DayFirst).ToArray();
        if (!DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return false;
        return date.Year is >= 1990 and <= 2100;
    }

    // Accepts 1299.5, 1,299.50, 1.299,50 and amounts with a currency symbol. Blank is not a price; negatives are refused.
    public static bool TryParsePrice(string? text, out decimal price)
    {
        price = 0;
        var t = new string((text ?? string.Empty).Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        if (t.Length == 0 || t.Contains('-')) return false;
        var lastDot = t.LastIndexOf('.');
        var lastComma = t.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0) t = lastComma > lastDot ? t.Replace(".", "").Replace(',', '.') : t.Replace(",", "");
        else if (lastComma >= 0)
        {
            var parts = t.Split(',');
            t = parts.Length == 2 && parts[1].Length is 1 or 2 ? parts[0] + "." + parts[1] : t.Replace(",", "");
        }
        else if (t.Count(c => c == '.') > 1) t = t[..lastDot].Replace(".", "") + t[lastDot..];
        if (!decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out price)) return false;
        price = Math.Round(price, 2);
        return price is >= 0 and <= 100_000_000m;
    }

    public static bool TryParseBool(string text, out bool value)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "true": case "yes": case "y": case "1": case "x": case "checked": case "on": value = true; return true;
            case "false": case "no": case "n": case "0": case "off": case "unchecked": value = false; return true;
            default: value = false; return false;
        }
    }
}
