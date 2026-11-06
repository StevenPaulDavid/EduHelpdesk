using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using EduHelpdesk.AgentContract;
using Microsoft.Win32;

namespace EduHelpdesk.Agent;

// Reads what Windows knows about this PC into a report. Every part is read on its own: a PC without BitLocker or
// Security Center (Home editions, servers) still reports everything else. Values go as Windows gives them; the helpdesk
// tidies them (Services/AgentFacts.cs). Needs to run as SYSTEM or an administrator for TPM and BitLocker.
public static class Collector
{
    public static string Version { get; } = typeof(Collector).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static AgentReport Collect()
    {
        var report = new AgentReport { AgentVersion = Version, CollectedAt = DateTime.UtcNow, ComputerName = Environment.MachineName };

        Try("computer system", () =>
        {
            foreach (var cs in Query(@"root\cimv2", "SELECT Manufacturer, Model, SystemFamily, PCSystemType, Domain, PartOfDomain, UserName FROM Win32_ComputerSystem").Take(1))
                report = report with
                {
                    Manufacturer = Text(cs, "Manufacturer"), Model = Text(cs, "Model"), SystemFamily = Text(cs, "SystemFamily"),
                    PcSystemType = Int(cs, "PCSystemType"), Domain = Text(cs, "Domain"), PartOfDomain = cs["PartOfDomain"] as bool?,
                    CurrentUser = Text(cs, "UserName")
                };
        });
        Try("product", () =>
        {
            foreach (var product in Query(@"root\cimv2", "SELECT UUID, Version FROM Win32_ComputerSystemProduct").Take(1))
                report = report with { Uuid = Text(product, "UUID"), ProductVersion = Text(product, "Version") };
        });
        Try("BIOS", () =>
        {
            foreach (var bios in Query(@"root\cimv2", "SELECT SerialNumber, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").Take(1))
                report = report with { SerialNumber = Text(bios, "SerialNumber"), BiosVersion = Text(bios, "SMBIOSBIOSVersion"), BiosDate = Date(bios, "ReleaseDate") };
        });
        Try("enclosure", () =>
        {
            foreach (var enclosure in Query(@"root\cimv2", "SELECT ChassisTypes, SMBIOSAssetTag FROM Win32_SystemEnclosure").Take(1))
                report = report with
                {
                    ChassisTypes = (enclosure["ChassisTypes"] as ushort[])?.Select(x => (int)x).ToArray(),
                    BiosAssetTag = Text(enclosure, "SMBIOSAssetTag")
                };
        });
        Try("processor", () =>
        {
            var processors = Query(@"root\cimv2", "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").ToList();
            if (processors.Count > 0)
                report = report with
                {
                    Processor = Text(processors[0], "Name"),
                    ProcessorCores = processors.Sum(x => Int(x, "NumberOfCores") ?? 0),
                    ProcessorThreads = processors.Sum(x => Int(x, "NumberOfLogicalProcessors") ?? 0)
                };
        });
        Try("memory", () =>
        {
            var total = Query(@"root\cimv2", "SELECT Capacity FROM Win32_PhysicalMemory").Sum(x => (long)(Long(x, "Capacity") ?? 0));
            report = report with { MemoryBytes = total > 0 ? total : null };
        });
        Try("disks", () => report = report with { Disks = Disks() });
        Try("system drive", () =>
        {
            var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            foreach (var disk in Query(@"root\cimv2", $"SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = '{drive}'").Take(1))
                report = report with { SystemDriveBytes = Long(disk, "Size"), SystemDriveFreeBytes = Long(disk, "FreeSpace") };
        });
        Try("Windows", () =>
        {
            foreach (var os in Query(@"root\cimv2", "SELECT Caption, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").Take(1))
                report = report with
                {
                    OsCaption = Text(os, "Caption"), OsBuild = Text(os, "BuildNumber"), OsArchitecture = Text(os, "OSArchitecture"),
                    OsInstalledAt = Date(os, "InstallDate"), LastBootAt = Date(os, "LastBootUpTime")
                };
            using var current = Machine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            report = report with
            {
                OsDisplayVersion = current?.GetValue("DisplayVersion") as string ?? current?.GetValue("ReleaseId") as string,
                OsRevision = current?.GetValue("UBR") as int?
            };
        });
        Try("TPM", () =>
        {
            foreach (var tpm in Query(@"root\cimv2\Security\MicrosoftTpm", "SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm").Take(1))
                report = report with { TpmVersion = Text(tpm, "SpecVersion")?.Split(',')[0].Trim(), TpmEnabled = tpm["IsEnabled_InitialValue"] as bool? };
        });
        Try("Secure Boot", () =>
        {
            var uefi = FirmwareIsUefi();
            using var state = Machine(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            report = report with
            {
                Uefi = uefi,
                SecureBootOn = state?.GetValue("UEFISecureBootEnabled") is int on ? on == 1 : uefi == true ? false : null
            };
        });
        Try("BitLocker", () =>
        {
            var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            foreach (var volume in Query(@"root\cimv2\Security\MicrosoftVolumeEncryption", $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'").Take(1))
                report = report with { BitLockerOn = Int(volume, "ProtectionStatus") switch { 1 => true, 0 => false, _ => null } };
        });
        Try("Defender", () =>
        {
            foreach (var status in Query(@"root\Microsoft\Windows\Defender", "SELECT RealTimeProtectionEnabled FROM MSFT_MpComputerStatus").Take(1))
                report = report with { DefenderOn = status["RealTimeProtectionEnabled"] as bool? };
        });
        Try("antivirus", () =>
            report = report with { Antivirus = Query(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct").Select(x => Text(x, "displayName")).OfType<string>().ToList() });
        Try("Windows 11 check", () => report = report with { Windows11Blockers = Windows11Blockers() });
        Try("Entra", () => report = report with { EntraTenant = EntraTenant() });
        Try("network", () => report = report with { Networks = Networks() });
        Try("last user", () =>
        {
            using var logon = Machine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            report = report with
            {
                LastUser = logon?.GetValue("LastLoggedOnSAMUser") as string ?? logon?.GetValue("LastLoggedOnUser") as string,
                LastUserDisplayName = logon?.GetValue("LastLoggedOnDisplayName") as string
            };
        });
        return report;
    }

    // Who is signed in at the screen now, cheaply, so the service can report soon after someone signs in.
    public static string? CurrentUser()
    {
        try { return Query(@"root\cimv2", "SELECT UserName FROM Win32_ComputerSystem").Select(x => Text(x, "UserName")).FirstOrDefault(); }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException) { return null; }
    }

    // Internal disks: not USB sticks, SD cards or virtual disks. MSFT_PhysicalDisk knows SSD from HDD; older Windows
    // falls back to Win32_DiskDrive, which doesn't.
    private static List<AgentDisk> Disks()
    {
        try
        {
            var disks = Query(@"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk")
                .Where(x => Int(x, "BusType") is not (7 or 12 or 13 or 14 or 15))
                .Select(x => new AgentDisk(Text(x, "FriendlyName"), Int(x, "BusType") == 17 || Int(x, "MediaType") == 4 ? "SSD" : Int(x, "MediaType") == 3 ? "HDD" : null, Long(x, "Size")))
                .ToList();
            if (disks.Count > 0) return disks;
        }
        catch (Exception exception) when (exception is ManagementException or COMException) { }
        return Query(@"root\cimv2", "SELECT Model, InterfaceType, MediaType, Size FROM Win32_DiskDrive")
            .Where(x => !string.Equals(Text(x, "InterfaceType"), "USB", StringComparison.OrdinalIgnoreCase)
                && (Text(x, "MediaType") ?? "").StartsWith("Fixed", StringComparison.OrdinalIgnoreCase))
            .Select(x => new AgentDisk(Text(x, "Model"), null, Long(x, "Size")))
            .ToList();
    }

    // Windows' own Windows 11 verdict from its compatibility appraiser. UNV is its answer for the newest release.
    private static string? Windows11Blockers()
    {
        using var indicators = Machine(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\TargetVersionUpgradeExperienceIndicators");
        if (indicators is null) return null;
        var names = indicators.GetSubKeyNames();
        foreach (var name in names.Contains("UNV") ? new[] { "UNV" } : names.OrderDescending().ToArray())
        {
            using var target = indicators.OpenSubKey(name);
            if (target?.GetValue("RedReason") is string reason && reason.Length > 0) return reason;
        }
        return null;
    }

    private static string? EntraTenant()
    {
        using var join = Machine(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
        if (join is null || join.SubKeyCount == 0) return null;
        using var tenants = Machine(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\TenantInfo");
        foreach (var id in tenants?.GetSubKeyNames() ?? [])
        {
            using var tenant = tenants!.OpenSubKey(id);
            if (tenant?.GetValue("DisplayName") is string name && name.Length > 0) return name;
        }
        return "joined";
    }

    // Physical adapters that are switched on, with their IPv4 addresses (and global IPv6 ones).
    private static List<AgentNetwork> Networks()
    {
        var addresses = Query(@"root\cimv2", "SELECT Index, IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE")
            .ToDictionary(x => Long(x, "Index") ?? -1, x => ((x["IPAddress"] as string[]) ?? []).Where(Usable).ToList());
        var adapters = Query(@"root\cimv2", "SELECT Index, NetConnectionID, Name, MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND NetEnabled = TRUE")
            .Select(x => (Real: !Virtual(Text(x, "Name")), Network: new AgentNetwork(Text(x, "NetConnectionID") ?? Text(x, "Name"), Text(x, "MACAddress"), addresses.GetValueOrDefault(Long(x, "Index") ?? -2) ?? [])))
            .ToList();
        // Windows counts Hyper-V switches, VirtualBox and VPN adapters as physical. They're left out - unless the PC's
        // only addresses are on them (a Hyper-V host's are on its virtual switch).
        var real = adapters.Where(x => x.Real).Select(x => x.Network).ToList();
        return real.Any(x => x.Addresses is { Count: > 0 }) ? real : real.Concat(adapters.Where(x => !x.Real && x.Network.Addresses is { Count: > 0 }).Select(x => x.Network)).ToList();

        static bool Virtual(string? name) => name is not null && new[] { "Virtual", "VirtualBox", "VMware", "Hyper-V", "Bridge", "Multiplexor", "TAP-", "WireGuard", "Bluetooth", "VPN", "Fortinet", "Cisco AnyConnect" }
            .Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));

        static bool Usable(string text) => IPAddress.TryParse(text, out var address)
            && (address.AddressFamily == AddressFamily.InterNetwork || (!address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal));
    }

    // GetFirmwareType: 1 legacy BIOS, 2 UEFI.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out int firmwareType);

    private static bool? FirmwareIsUefi()
    {
        try { return GetFirmwareType(out var type) ? type == 2 : null; }
        catch (EntryPointNotFoundException) { return null; }
    }

    private static void Try(string part, Action read)
    {
        try { read(); }
        catch (Exception exception) when (exception is ManagementException or COMException or UnauthorizedAccessException or InvalidCastException or IOException)
        {
            AgentLog.Write($"Couldn't read the {part}: {exception.Message.Trim()}");
        }
    }

    private static List<ManagementBaseObject> Query(string scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    private static RegistryKey? Machine(string path)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return root.OpenSubKey(path);
    }

    private static string? Text(ManagementBaseObject item, string name) => item[name]?.ToString()?.Trim() is { Length: > 0 } text ? text : null;
    private static int? Int(ManagementBaseObject item, string name) => item[name] is { } value ? Convert.ToInt32(value) : null;
    private static long? Long(ManagementBaseObject item, string name) => item[name] is { } value ? Convert.ToInt64(value) : null;

    private static DateTime? Date(ManagementBaseObject item, string name)
    {
        if (Text(item, name) is not { } text) return null;
        try { return ManagementDateTimeConverter.ToDateTime(text).ToUniversalTime(); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
