namespace EduHelpdesk.AgentContract;

// What the EduHelpdesk Agent sends (agent/EduHelpdesk.Agent) and the helpdesk receives (Services/AgentEndpoints.cs). This
// one file is compiled into both projects, so the two can't drift apart. It is sent as JSON with the web defaults
// (camelCase names). Everything is nullable: an older agent leaves out what it doesn't know, and a value Windows
// wouldn't give is null rather than a guess.
//
// The agent sends what Windows says, raw - "Dell Inc.", "LENOVO", chassis type 10. Tidying that into a make, model and
// type is done by the helpdesk (Services/AgentFacts.cs), so improving it never needs every PC to update.
//
//   GET  /api/agent/v1/info      { serverVersion, apiVersion, enabled }
//   POST /api/agent/v1/enrol     EnrolRequest -> CheckInAnswer (with token)      - the school's enrolment key
//   POST /api/agent/v1/report    AgentReport  -> CheckInAnswer                   - "Authorization: Bearer eha_..."
public sealed record EnrolRequest(string? Key, AgentReport? Report);

// Status: "linked" (reporting into AssetTag), "waiting" (its serial matches an asset someone has to confirm first).
// NextReportHours is how long until the next full report, so the helpdesk can change it without updating every PC.
public sealed record CheckInAnswer(string? Token, string Status, string? AssetTag, int NextReportHours, string? Message);

public sealed record AgentReport
{
    public string? AgentVersion { get; init; }
    public DateTime? CollectedAt { get; init; }

    // ---- Who made it and what it is ----
    public string? Manufacturer { get; init; }      // Win32_ComputerSystem.Manufacturer
    public string? Model { get; init; }             // Win32_ComputerSystem.Model
    public string? SystemFamily { get; init; }      // Win32_ComputerSystem.SystemFamily
    public string? ProductVersion { get; init; }    // Win32_ComputerSystemProduct.Version: Lenovo's readable model name
    public string? SerialNumber { get; init; }      // Win32_BIOS.SerialNumber
    public string? Uuid { get; init; }              // Win32_ComputerSystemProduct.UUID: stays the same through a reinstall
    public string? BiosAssetTag { get; init; }      // Win32_SystemEnclosure.SMBIOSAssetTag
    public int[]? ChassisTypes { get; init; }       // Win32_SystemEnclosure.ChassisTypes
    public int? PcSystemType { get; init; }         // Win32_ComputerSystem.PCSystemType: 1 desktop, 2 mobile, ...
    public string? BiosVersion { get; init; }       // Win32_BIOS.SMBIOSBIOSVersion
    public DateTime? BiosDate { get; init; }

    // ---- Insides ----
    public string? Processor { get; init; }
    public int? ProcessorCores { get; init; }
    public int? ProcessorThreads { get; init; }
    public long? MemoryBytes { get; init; }         // installed sticks, summed (Win32_PhysicalMemory)
    public List<AgentDisk>? Disks { get; init; }    // internal physical disks
    public long? SystemDriveBytes { get; init; }    // C:
    public long? SystemDriveFreeBytes { get; init; }

    // ---- Windows ----
    public string? OsCaption { get; init; }         // "Microsoft Windows 11 Pro"
    public string? OsDisplayVersion { get; init; }  // "24H2"
    public string? OsBuild { get; init; }           // "26100"
    public int? OsRevision { get; init; }           // UBR: changes with every monthly update
    public string? OsArchitecture { get; init; }
    public DateTime? OsInstalledAt { get; init; }
    public DateTime? LastBootAt { get; init; }

    // ---- Security ----
    // TPM: null when Windows reports none. "2.0", "1.2".
    public string? TpmVersion { get; init; }
    public bool? TpmEnabled { get; init; }
    // Null on a PC started the old (legacy BIOS) way, which can't do Secure Boot at all.
    public bool? SecureBootOn { get; init; }
    public bool? Uefi { get; init; }
    // BitLocker on the system drive: true protected, false not, null unknown (no BitLocker on Home editions).
    public bool? BitLockerOn { get; init; }
    public bool? DefenderOn { get; init; }          // real-time protection
    public List<string>? Antivirus { get; init; }   // what Windows Security Center lists
    // Windows' own Windows 11 compatibility verdict (the appraiser's RedReason): "None" means it can upgrade. Null when
    // Windows hasn't worked it out, or the PC is already on Windows 11.
    public string? Windows11Blockers { get; init; }

    // ---- Network and people ----
    public string? ComputerName { get; init; }
    public string? Domain { get; init; }            // the AD domain, when PartOfDomain
    public bool? PartOfDomain { get; init; }
    public string? EntraTenant { get; init; }       // Microsoft Entra (Azure AD) joined: the tenant's name, else null
    public List<AgentNetwork>? Networks { get; init; }
    public string? CurrentUser { get; init; }       // signed in at the console now, DOMAIN\user
    public string? LastUser { get; init; }          // Windows' "last signed in" (LogonUI), DOMAIN\user or AzureAD\Name
    public string? LastUserDisplayName { get; init; }
}

public sealed record AgentDisk(string? Name, string? Kind, long? SizeBytes);

public sealed record AgentNetwork(string? Name, string? MacAddress, List<string>? Addresses);
