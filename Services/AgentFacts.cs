using System.Globalization;
using EduHelpdesk.AgentContract;

namespace EduHelpdesk.Services;

// Turning what Windows says about a PC (AgentContract.AgentReport, sent raw by the EduHelpdesk Agent) into what the asset
// register wants: "Dell Inc." into Dell, chassis type 10 into Laptop, and the specs the asset's Device tab shows. Kept on
// the helpdesk side so a better rule here reaches every PC at its next report, without updating the agent.
public static class AgentFacts
{
    private const int MaxText = 200;

    // Placeholders manufacturers leave in the firmware. A serial number like these is shared by every board of that
    // make, so it can't say which PC is which - matching on it would merge different machines.
    private static readonly HashSet<string> Junk = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "To be filled by O.E.M.", "To Be Filled By O.E.M.", "Default string", "System Serial Number", "System Product Name",
        "System manufacturer", "System Manufacturer", "System Version", "Chassis Serial Number", "Base Board Serial Number",
        "Not Applicable", "Not Specified", "Not Available", "None", "N/A", "NA", "Invalid", "INVALID", "O.E.M.", "OEM", "0",
        "00000000", "0000000000", "123456789", "1234567890", "Type1ProductConfigId", "SKU", "Unknown", "x.x"
    };

    // Text from the PC, trimmed and capped. Null for nothing or a placeholder.
    public static string? Clean(string? value)
    {
        var text = (value ?? string.Empty).Trim().Replace('\0', ' ').Trim();
        if (Junk.Contains(text)) return null;
        return text.Length > MaxText ? text[..MaxText] : text;
    }

    // The serial number, or "" when the firmware has none worth trusting (a self-build, many virtual machines).
    public static string Serial(AgentReport report)
    {
        var serial = Clean(report.SerialNumber) ?? string.Empty;
        // All one repeated character (00000, FFFFF, 11111) is a placeholder too.
        return serial.Length > 0 && serial.All(c => c == serial[0]) ? string.Empty : serial;
    }

    // The board's UUID, which a reinstall of Windows doesn't change. Upper case, "" when it is a placeholder.
    public static string Uuid(AgentReport report)
    {
        var uuid = (Clean(report.Uuid) ?? string.Empty).ToUpperInvariant();
        if (!Guid.TryParse(uuid, out var parsed)) return string.Empty;
        var digits = uuid.Replace("-", "");
        if (digits.All(c => c == digits[0])) return string.Empty;
        // The one firmware writers copy from the SMBIOS specification's example.
        if (parsed == Guid.Parse("03000200-0400-0500-0006-000700080009")) return string.Empty;
        return uuid;
    }

    // Long legal names to what schools call them. Anything not here is used as Windows gave it.
    private static readonly (string Starts, string Make)[] Makes =
    [
        ("Dell", "Dell"), ("Hewlett-Packard", "HP"), ("HP", "HP"), ("Lenovo", "Lenovo"), ("Microsoft", "Microsoft"),
        ("ASUSTeK", "ASUS"), ("ASUS", "ASUS"), ("Acer", "Acer"), ("Apple", "Apple"), ("Fujitsu", "Fujitsu"),
        ("TOSHIBA", "Dynabook"), ("Dynabook", "Dynabook"), ("Samsung", "Samsung"), ("Gigabyte", "Gigabyte"),
        ("Micro-Star", "MSI"), ("MSI", "MSI"), ("VMware", "VMware"), ("innotek", "VirtualBox"), ("Panasonic", "Panasonic"),
        ("Intel", "Intel"), ("Viglen", "Viglen"), ("RM Education", "RM"), ("Research Machines", "RM"), ("Getac", "Getac")
    ];

    public static string Make(AgentReport report)
    {
        var raw = Clean(report.Manufacturer) ?? string.Empty;
        foreach (var (starts, make) in Makes)
            if (raw.StartsWith(starts, StringComparison.OrdinalIgnoreCase)) return make;
        // "Some Company Inc." / "Ltd." without the suffix.
        foreach (var suffix in new[] { " Inc.", " Inc", " Corporation", " Corp.", " Ltd.", " Ltd", " Limited", " Co., Ltd.", " GmbH" })
            if (raw.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return raw[..^suffix.Length].TrimEnd(',', ' ');
        return raw;
    }

    // The model as people read it. Lenovo puts a part number (20XW0026UK) in Model and the name (ThinkPad X1 Carbon
    // Gen 9) in the product version, so that is used instead.
    public static string Model(AgentReport report)
    {
        var model = Clean(report.Model) ?? string.Empty;
        if (string.Equals(Make(report), "Lenovo", StringComparison.Ordinal) && Clean(report.ProductVersion) is { } name
            && !string.Equals(name, "Lenovo", StringComparison.OrdinalIgnoreCase))
            model = name;
        return model;
    }

    // Another way the same model might already be in the school's list: without the make in front ("HP ProBook 450 G8
    // Notebook PC" as "ProBook 450 G8 Notebook PC").
    public static IEnumerable<string> ModelSpellings(AgentReport report)
    {
        var model = Model(report);
        if (model.Length == 0) yield break;
        yield return model;
        var make = Make(report);
        if (make.Length > 0 && model.StartsWith(make + " ", StringComparison.OrdinalIgnoreCase)) yield return model[(make.Length + 1)..];
    }

    // SMBIOS chassis types, and Windows' own PC system type when the chassis says Other or Unknown.
    public const string Laptop = "Laptop";
    public const string Desktop = "Desktop";
    public const string Tablet = "Tablet";
    public const string Server = "Server";

    public static string Type(AgentReport report)
    {
        foreach (var chassis in report.ChassisTypes ?? [])
        {
            switch (chassis)
            {
                case 8 or 9 or 10 or 11 or 14 or 31 or 32: return Laptop;
                case 30: return Tablet;
                case 3 or 4 or 5 or 6 or 7 or 13 or 15 or 16 or 24 or 35 or 36: return Desktop;
                case 17 or 23 or 25 or 28: return Server;
            }
        }
        // 12 is a docking station - what a docked laptop sometimes reports - so it falls through to here.
        return report.PcSystemType switch
        {
            2 or 8 => Laptop,
            1 or 3 or 6 => Desktop,
            4 or 5 or 7 => Server,
            _ => (report.ChassisTypes ?? []).Contains(12) ? Laptop : string.Empty
        };
    }

    // The register's Operating system: "Windows 11 Pro 24H2". The monthly build number is on the Device tab, not here,
    // so every Patch Tuesday doesn't put a line in the asset's history.
    public static string OperatingSystem(AgentReport report)
    {
        var caption = Clean(report.OsCaption) ?? string.Empty;
        if (caption.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)) caption = caption["Microsoft ".Length..];
        var version = Clean(report.OsDisplayVersion);
        return version is null || caption.Length == 0 ? caption : $"{caption} {version}";
    }

    public static string Build(AgentReport report) =>
        Clean(report.OsBuild) is { } build ? report.OsRevision is { } revision ? $"{build}.{revision}" : build : string.Empty;

    // Memory in whole GB, as sold: 17,179,869,184 bytes is "16 GB".
    public static string Memory(long? bytes) =>
        bytes is > 0 ? $"{Math.Round(bytes.Value / 1073741824d).ToString(CultureInfo.InvariantCulture)} GB" : string.Empty;

    // Disk sizes the way the box says: decimal GB (512,110,190,592 bytes is "512 GB"), TB from 1000 GB.
    public static string Size(long? bytes)
    {
        if (bytes is not > 0) return string.Empty;
        var gb = bytes.Value / 1e9;
        return gb >= 999.5
            ? $"{Math.Round(gb / 1000, 1).ToString("0.#", CultureInfo.InvariantCulture)} TB"
            : $"{Math.Round(gb).ToString(CultureInfo.InvariantCulture)} GB";
    }

    public static string Storage(AgentReport report) =>
        string.Join(", ", (report.Disks ?? []).Where(x => x.SizeBytes > 0)
            .Select(x => Clean(x.Kind) is { } kind ? $"{Size(x.SizeBytes)} {kind}" : Size(x.SizeBytes)));

    public static string FreeSpace(AgentReport report)
    {
        if (report.SystemDriveBytes is not > 0 || report.SystemDriveFreeBytes is not >= 0) return string.Empty;
        var percent = (int)Math.Round(100d * report.SystemDriveFreeBytes.Value / report.SystemDriveBytes.Value);
        return $"{Size(report.SystemDriveFreeBytes)} free of {Size(report.SystemDriveBytes)} ({percent}%)";
    }

    public static string Processor(AgentReport report)
    {
        var name = Clean(report.Processor) ?? string.Empty;
        // Intel and AMD pad the name with runs of spaces.
        while (name.Contains("  ", StringComparison.Ordinal)) name = name.Replace("  ", " ", StringComparison.Ordinal);
        return name;
    }

    public static string Tpm(AgentReport report) => Clean(report.TpmVersion) is { } version
        ? report.TpmEnabled == false ? $"{version} (turned off)" : version
        : "None found";

    public static string SecureBoot(AgentReport report) => report.SecureBootOn switch
    {
        true => "On",
        false => "Off",
        null => report.Uefi == false ? "Not supported (legacy BIOS)" : "Not known"
    };

    public static string BitLocker(AgentReport report) => report.BitLockerOn switch
    {
        true => "On",
        false => "Off",
        null => "Not available"
    };

    public static string Antivirus(AgentReport report)
    {
        // Security Center lists Defender by name; whether its real-time protection is on comes separately.
        var listed = (report.Antivirus ?? []).Select(Clean).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var others = listed.Where(x => !x.Contains("Defender", StringComparison.OrdinalIgnoreCase)).ToList();
        if (report.DefenderOn is { } on) others.Insert(0, on ? "Microsoft Defender (on)" : "Microsoft Defender (off)");
        else if (others.Count < listed.Count) others.Insert(0, "Microsoft Defender");
        return others.Count == 0 ? "Not known" : string.Join(", ", others);
    }

    // Windows' own verdict, in words. Its codes (RedReason) are short names for what fails.
    private static readonly Dictionary<string, string> Blockers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TPM"] = "TPM 2.0", ["UefiSecureBoot"] = "Secure Boot", ["SecureBoot"] = "Secure Boot", ["CpuFms"] = "processor",
        ["CpuModel"] = "processor", ["CpuCores"] = "processor", ["CpuSse"] = "processor", ["CpuSse42"] = "processor",
        ["CpuPopCnt"] = "processor", ["Memory"] = "memory", ["SystemDriveSize"] = "disk size", ["Storage"] = "disk size",
        ["DirectX"] = "graphics", ["Wddm"] = "graphics"
    };

    public static string Windows11(AgentReport report)
    {
        var os = OperatingSystem(report);
        if (os.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)) return "Already on Windows 11";
        if (os.Contains("Server", StringComparison.OrdinalIgnoreCase)) return "Not applicable";
        // "None" is Windows' word for nothing in the way - checked before Clean, which reads it as a placeholder.
        if (string.Equals(report.Windows11Blockers?.Trim(), "None", StringComparison.OrdinalIgnoreCase)) return "Yes";
        var reasons = Clean(report.Windows11Blockers);
        if (reasons is null) return "Not known yet";
        var words = reasons.Split([' ', '|', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Blockers.TryGetValue(x, out var word) ? word : x).Distinct(StringComparer.OrdinalIgnoreCase);
        return $"No: {string.Join(", ", words)}";
    }

    public static string JoinedTo(AgentReport report)
    {
        var domain = report.PartOfDomain == true ? Clean(report.Domain) : null;
        var tenant = Clean(report.EntraTenant);
        return (domain, tenant) switch
        {
            ({ } d, { } t) => $"{d} and Microsoft Entra ({t})",
            ({ } d, null) => $"Domain {d}",
            (null, { } t) => $"Microsoft Entra ({t})",
            _ => "Workgroup"
        };
    }

    public static string Addresses(AgentReport report) =>
        string.Join(", ", (report.Networks ?? []).SelectMany(x => x.Addresses ?? []).Select(Clean).OfType<string>().Distinct());

    public static string MacAddresses(AgentReport report) =>
        string.Join(", ", (report.Networks ?? []).Select(x => Clean(x.MacAddress)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase));

    // The person Windows last saw sign in, as "Display Name (DOMAIN\user)" or whichever half is known.
    public static string LastUser(AgentReport report)
    {
        var account = Clean(report.CurrentUser) ?? Clean(report.LastUser);
        var name = UserDisplayName(report);
        return (name, account) switch
        {
            ({ } n, { } a) => $"{n} ({a})",
            ({ } n, null) => n,
            (null, { } a) => a,
            _ => string.Empty
        };
    }

    // The account without its domain (STAFF\jsmith -> jsmith, AzureAD\JaneSmith -> JaneSmith), for matching to People.
    public static string? UserAccount(AgentReport report)
    {
        var account = Clean(report.CurrentUser) ?? Clean(report.LastUser);
        return account is null ? null : Bare(account);
    }

    // The display name Windows keeps belongs with its last-signed-in account (LogonUI). If someone else is signed in
    // now, it isn't theirs. ".\steve" and "HERMES\steve" are the same local account.
    public static string? UserDisplayName(AgentReport report)
    {
        if (Clean(report.CurrentUser) is { } now && Clean(report.LastUser) is { } last && !string.Equals(Bare(now), Bare(last), StringComparison.OrdinalIgnoreCase)) return null;
        return Clean(report.LastUserDisplayName);
    }

    private static string Bare(string account) => account.LastIndexOf('\\') is var slash and >= 0 ? account[(slash + 1)..] : account;

    // What the Device tab shows and the asset's history follows. A change in any of these is written to History
    // ("Memory changed: 8 GB -> 16 GB"). Make, model, type, serial and operating system are the asset's own fields and
    // have their own history lines; addresses, free space and who signed in change all the time and are shown only.
    public static IReadOnlyList<(string Label, string Value)> Tracked(AgentReport report) =>
    [
        ("Computer name", Clean(report.ComputerName) ?? string.Empty),
        ("Processor", Processor(report)),
        ("Memory", Memory(report.MemoryBytes)),
        ("Storage", Storage(report)),
        ("BIOS version", Clean(report.BiosVersion) ?? string.Empty),
        ("TPM", Tpm(report)),
        ("Secure Boot", SecureBoot(report)),
        ("BitLocker", BitLocker(report)),
        ("Antivirus", Antivirus(report)),
        ("Windows 11 ready", Windows11(report)),
        ("Joined to", JoinedTo(report))
    ];

    // The changes between two reports from the same PC, as history lines. Nothing for a value that was blank before:
    // an older agent that didn't send it yet isn't a change to the machine.
    public static IEnumerable<(string Label, string From, string To)> Changes(AgentReport previous, AgentReport next)
    {
        var before = Tracked(previous).ToDictionary(x => x.Label, x => x.Value);
        foreach (var (label, value) in Tracked(next))
            if (before.TryGetValue(label, out var old) && old.Length > 0 && value.Length > 0 && !string.Equals(old, value, StringComparison.Ordinal))
                yield return (label, old, value);
    }
}
