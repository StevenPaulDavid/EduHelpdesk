namespace EduHelpdesk.AgentContract;

// What the EduHelpdesk Agent sends (agent/EduHelpdesk.Agent) and the helpdesk receives (Services/AgentEndpoints.cs). This
// one file is compiled into both projects, so the two can't drift apart. It is sent as JSON with the web defaults
// (camelCase names). Everything is nullable: an older agent leaves out what it doesn't know, and a value Windows
// wouldn't give is null rather than a guess.
//
// The agent sends what Windows says, raw - "Dell Inc.", "LENOVO", chassis type 10. Tidying that into a make, model and
// type is done by the helpdesk (Services/AgentFacts.cs), so improving it never needs every PC to update.
//
//   GET  /api/agent/v1/info      { serverVersion, apiVersion, enabled, agentVersion }
//   POST /api/agent/v1/enrol     EnrolRequest -> CheckInAnswer (with token)      - the school's enrolment key
//   POST /api/agent/v1/report    AgentReport  -> CheckInAnswer                   - "Authorization: Bearer eha_..."
//   GET  /api/agent/v1/agent     the agent exe this helpdesk hands out           - "Authorization: Bearer eha_..."
//   GET  /api/agent/v1/remote    waits up to ~50s for a remote support request   - token; 200 RemoteOffer or 204
//   POST /api/agent/v1/remote/{id}  RemoteStateReport: how the request is going  - token
public sealed record EnrolRequest(string? Key, AgentReport? Report);

// A technician wants to help through Quick Assist (from a ticket). Code is the Quick Assist security code they made on
// their own PC; the person at this PC types it in. It lasts 10 minutes (ExpiresAt), as Quick Assist's own code does.
public sealed record RemoteOffer(Guid Id, string Code, string TechnicianName, string TicketRef, DateTime ExpiresAt);

// What happened at the PC: one of RemoteStates.
public sealed record RemoteStateReport(string? State);

// Where a remote support request has got to. The helpdesk sets the first two; the agent reports the rest.
public static class RemoteStates
{
    public const string Requested = "requested";      // the technician sent it
    public const string Delivered = "delivered";      // the agent has collected it
    public const string Shown = "shown";              // the pop-up is on the screen
    public const string Accepted = "accepted";        // Open Quick Assist pressed
    public const string Declined = "declined";        // No thanks pressed
    public const string Nobody = "nobody";            // nobody signed in to show it to
    public const string Unavailable = "unavailable";  // Quick Assist isn't on the PC
    public const string Started = "started";          // Quick Assist is open
    public const string Ended = "ended";              // Quick Assist closed again
    public const string Expired = "expired";          // 10 minutes and no Quick Assist

    public static readonly string[] FromAgent = [Shown, Accepted, Declined, Nobody, Unavailable, Started, Ended, Expired];
    // Nothing more happens after these.
    public static bool IsFinished(string state) => state is Declined or Nobody or Unavailable or Ended or Expired;
}

// Status: "linked" (reporting into AssetTag), "waiting" (its serial matches an asset someone has to confirm first).
// NextReportHours is how long until the next full report, so the helpdesk can change it without updating every PC.
// AgentVersion and AgentSha256: the agent this helpdesk hands out (null when it has none). A PC running an older one
// downloads it from /agent and checks the hash before installing it.
public sealed record CheckInAnswer(string? Token, string Status, string? AssetTag, int NextReportHours, string? Message,
    string? AgentVersion = null, string? AgentSha256 = null);

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
