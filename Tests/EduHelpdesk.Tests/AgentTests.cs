using EduHelpdesk.AgentContract;
using Microsoft.AspNetCore.Http;

namespace EduHelpdesk.Tests;

// The EduHelpdesk Agent's side of the helpdesk (HelpdeskStore.Agent, AgentFacts): PCs enrolling and reporting into the
// asset register.
public class AgentTests
{
    private static AgentReport Pc(string serial = "5CD1234XYZ", string name = "SCI-LT-01", long memoryGb = 8) => new()
    {
        AgentVersion = "0.1.0",
        Manufacturer = "Dell Inc.",
        Model = "Latitude 5440",
        SerialNumber = serial,
        Uuid = Guid.NewGuid().ToString().ToUpperInvariant(),
        ChassisTypes = [10],
        Processor = "13th Gen Intel(R) Core(TM) i5-1345U",
        MemoryBytes = memoryGb * 1073741824L,
        Disks = [new AgentDisk("PC SN740 NVMe", "SSD", 512110190592)],
        OsCaption = "Microsoft Windows 11 Pro",
        OsDisplayVersion = "24H2",
        OsBuild = "26100",
        OsRevision = 4652,
        TpmVersion = "2.0",
        SecureBootOn = true,
        BitLockerOn = true,
        DefenderOn = true,
        ComputerName = name,
        LastUser = @"STAFF\jsmith",
        LastUserDisplayName = "Jane Smith"
    };

    private static string Enrol(TestStore test, AgentReport report)
    {
        var result = test.Store.EnrolAgent(test.Store.AgentEnrolmentKey, report, null);
        Assert.Equal(StatusCodes.Status200OK, result.Status);
        return result.Answer!.Token!;
    }

    private static TestStore WithAgentOn()
    {
        var test = new TestStore();
        test.Store.SetAgentEnabled(true);
        return test;
    }

    [Fact]
    public void A_new_PC_adds_itself_as_an_asset_with_the_next_number()
    {
        using var test = WithAgentOn();
        var next = test.Store.NextAssetTag;

        var token = Enrol(test, Pc());

        var asset = test.Store.Assets.Single(x => x.SerialNumber == "5CD1234XYZ");
        Assert.Equal(next, asset.AssetTag);
        Assert.Equal("Dell", asset.Make);
        Assert.Equal("Latitude 5440", asset.Model);
        Assert.Equal("Laptop", asset.Type);
        Assert.Equal("Windows 11 Pro 24H2", asset.OperatingSystem);
        Assert.Contains(test.Store.AssetModels, x => x == "Latitude 5440");
        Assert.Equal("Added by the device agent", asset.History[0].Action);
        Assert.StartsWith(HelpdeskStore.AgentTokenPrefix, token);
        Assert.Equal(asset.Id, test.Store.AgentDeviceFor(asset.Id)!.AssetId);
        // The next asset added any way at all gets the number after.
        Assert.NotEqual(next, test.Store.NextAssetTag);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void The_key_must_match_and_the_agent_must_be_on()
    {
        using var test = new TestStore();
        Assert.Equal(StatusCodes.Status403Forbidden, test.Store.EnrolAgent("anything", Pc(), null).Status);
        test.Store.SetAgentEnabled(true);
        Assert.Equal(StatusCodes.Status401Unauthorized, test.Store.EnrolAgent("ehe_wrong", Pc(), null).Status);
        Assert.Equal(StatusCodes.Status401Unauthorized, test.Store.AgentReportIn("eha_made-up", Pc(), null).Status);

        var oldKey = test.Store.AgentEnrolmentKey;
        var token = Enrol(test, Pc());
        test.Store.NewAgentEnrolmentKey();
        Assert.Equal(StatusCodes.Status401Unauthorized, test.Store.EnrolAgent(oldKey, Pc("OTHER123"), null).Status);
        // An enrolled PC carries on with its token.
        Assert.Equal(StatusCodes.Status200OK, test.Store.AgentReportIn(token, Pc(), null).Status);

        test.Store.SetAgentEnabled(false);
        Assert.Equal(StatusCodes.Status403Forbidden, test.Store.AgentReportIn(token, Pc(), null).Status);
    }

    [Fact]
    public void A_spec_change_goes_in_history_but_an_unchanged_report_adds_nothing()
    {
        using var test = WithAgentOn();
        var token = Enrol(test, Pc());
        var assetId = test.Store.Assets.Single(x => x.SerialNumber == "5CD1234XYZ").Id;
        var before = test.Store.Assets.Single(x => x.Id == assetId).History.Count;

        test.Store.AgentReportIn(token, Pc(), null);
        Assert.Equal(before, test.Store.Assets.Single(x => x.Id == assetId).History.Count);

        test.Store.AgentReportIn(token, Pc(memoryGb: 16) with { OsRevision = 4770 }, null);
        var history = test.Store.Assets.Single(x => x.Id == assetId).History;
        Assert.Equal(before + 1, history.Count);
        Assert.Equal("Memory changed", history[^1].Action);
        Assert.Equal("8 GB -> 16 GB", history[^1].Details);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_model_tidied_by_hand_is_kept_until_the_PC_itself_changes()
    {
        using var test = WithAgentOn();
        var token = Enrol(test, Pc());
        var asset = test.Store.Assets.Single(x => x.SerialNumber == "5CD1234XYZ");
        test.Store.UpdateAsset(asset with { Model = "Latitude 5440 (Science)", Location = "S12" });

        test.Store.AgentReportIn(token, Pc(), null);
        Assert.Equal("Latitude 5440 (Science)", test.Store.Assets.Single(x => x.Id == asset.Id).Model);

        test.Store.AgentReportIn(token, Pc() with { OsCaption = "Microsoft Windows 11 Education" }, null);
        var after = test.Store.Assets.Single(x => x.Id == asset.Id);
        Assert.Equal("Windows 11 Education 24H2", after.OperatingSystem);
        Assert.Equal("Latitude 5440 (Science)", after.Model);
        Assert.Equal("S12", after.Location);
    }

    [Fact]
    public void A_PC_whose_serial_is_already_on_the_register_waits_until_linked()
    {
        using var test = WithAgentOn();
        var existing = new AssetRecord(Guid.NewGuid(), "LT-0042", "", "", "", "5cd1234xyz", "Library", null);
        test.Store.AddAsset(existing);
        var count = test.Store.Assets.Count;

        var result = test.Store.EnrolAgent(test.Store.AgentEnrolmentKey, Pc(), null);
        Assert.Equal(HelpdeskStore.AgentDeviceStatus.Waiting, result.Answer!.Status);
        Assert.Contains("LT-0042", result.Answer.Message);
        Assert.Equal(count, test.Store.Assets.Count);
        var device = test.Store.AgentDevices.Single();
        Assert.Equal(existing.Id, device.MatchAssetId);

        // Reports while waiting change nothing on the asset.
        test.Store.AgentReportIn(result.Answer.Token, Pc(memoryGb: 16), null);
        Assert.Equal("", test.Store.Assets.Single(x => x.Id == existing.Id).Make);

        Assert.True(test.Store.LinkAgentDevice(device.Id).Ok);
        var linked = test.Store.Assets.Single(x => x.Id == existing.Id);
        Assert.Equal("Dell", linked.Make);
        Assert.Equal("Latitude 5440", linked.Model);
        Assert.Equal("Library", linked.Location);
        Assert.Equal("5cd1234xyz", linked.SerialNumber);
        Assert.Equal(existing.Id, test.Store.AgentDevices.Single().AssetId);
        Assert.Equal(HelpdeskStore.AgentDeviceStatus.Linked, test.Store.AgentReportIn(result.Answer.Token, Pc(memoryGb: 16), null).Answer!.Status);
        Assert.Null(test.Store.CompareDatabaseWithMemory());
    }

    [Fact]
    public void A_waiting_PC_can_be_added_as_an_asset_of_its_own()
    {
        using var test = WithAgentOn();
        test.Store.AddAsset(new AssetRecord(Guid.NewGuid(), "LT-0042", "HP", "ProBook", "Laptop", "5CD1234XYZ", "", null));
        Enrol(test, Pc());
        var device = test.Store.AgentDevices.Single();

        var (ok, message) = test.Store.AddAgentDeviceAsNew(device.Id);
        Assert.True(ok, message);
        Assert.Equal(2, test.Store.Assets.Count(x => x.SerialNumber.Equals("5CD1234XYZ", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(HelpdeskStore.AgentDeviceStatus.Linked, test.Store.AgentDevices.Single().Status);
    }

    [Fact]
    public void A_reinstalled_PC_carries_on_into_the_same_asset_and_its_old_token_stops()
    {
        using var test = WithAgentOn();
        var report = Pc();
        var oldToken = Enrol(test, report);
        var assetId = test.Store.Assets.Single(x => x.SerialNumber == "5CD1234XYZ").Id;
        var assets = test.Store.Assets.Count;

        var newToken = Enrol(test, report with { ComputerName = "SCI-LT-01-NEW" });

        Assert.Equal(assets, test.Store.Assets.Count);
        Assert.Equal(StatusCodes.Status401Unauthorized, test.Store.AgentReportIn(oldToken, report, null).Status);
        Assert.Equal(StatusCodes.Status200OK, test.Store.AgentReportIn(newToken, report, null).Status);
        var history = test.Store.Assets.Single(x => x.Id == assetId).History;
        Assert.Contains(history, x => x.Action == "Agent reinstalled");
        Assert.Contains(history, x => x.Action == "Computer name changed");
        Assert.Single(test.Store.AgentDevices);
    }

    [Fact]
    public void A_placeholder_serial_never_matches_but_the_board_UUID_still_finds_a_reinstall()
    {
        using var test = WithAgentOn();
        test.Store.AddAsset(new AssetRecord(Guid.NewGuid(), "PC-0001", "", "", "", "To be filled by O.E.M.", "", null));
        var report = Pc("To be filled by O.E.M.");
        Enrol(test, report);
        Assert.Equal(HelpdeskStore.AgentDeviceStatus.Linked, test.Store.AgentDevices.Single().Status);
        var assetId = test.Store.AgentDevices.Single().AssetId;
        Assert.Equal("", test.Store.Assets.Single(x => x.Id == assetId).SerialNumber);

        Enrol(test, report);
        Assert.Equal(assetId, test.Store.AgentDevices.Single().AssetId);
    }

    [Fact]
    public void PCs_survive_a_restart_and_a_deleted_asset_is_added_again()
    {
        using var test = WithAgentOn();
        var token = Enrol(test, Pc());
        var assetId = test.Store.AgentDevices.Single().AssetId!.Value;

        test.Reopen();
        Assert.True(test.Store.AgentEnabled);
        var device = test.Store.AgentDevices.Single();
        Assert.Equal(assetId, device.AssetId);
        Assert.Equal("Latitude 5440", device.Report.Model);

        Assert.Null(test.Store.DeleteAsset(assetId));
        var answer = test.Store.AgentReportIn(token, Pc(), null).Answer!;
        Assert.Equal(HelpdeskStore.AgentDeviceStatus.Linked, answer.Status);
        Assert.NotEqual(assetId, test.Store.AgentDevices.Single().AssetId);
        Assert.Single(test.Store.Assets, x => x.SerialNumber == "5CD1234XYZ");
    }

    [Fact]
    public void Removing_a_PC_stops_its_token()
    {
        using var test = WithAgentOn();
        var token = Enrol(test, Pc());
        Assert.True(test.Store.ForgetAgentDevice(test.Store.AgentDevices.Single().Id).Ok);
        Assert.Empty(test.Store.AgentDevices);
        Assert.Equal(StatusCodes.Status401Unauthorized, test.Store.AgentReportIn(token, Pc(), null).Status);
    }

    [Fact]
    public void Last_signed_in_matches_a_person_by_name_or_account()
    {
        using var test = WithAgentOn();
        var jane = test.AddRequester("Jane Smith");
        Assert.Equal(jane.Id, test.Store.AgentDeviceUser(Pc())!.Id);
        // Someone else signed in now: Windows' display name was for the last person, so match on the account instead.
        var bob = test.AddRequester("Bob Jones");
        var other = Pc() with { CurrentUser = @"STAFF\bob.jones" };
        Assert.Equal(bob.Id, test.Store.AgentDeviceUser(other)!.Id);
        Assert.Equal(@"STAFF\bob.jones", AgentFacts.LastUser(other));
        Assert.Equal(@"Jane Smith (STAFF\jsmith)", AgentFacts.LastUser(Pc()));
        // The same local account written two ways keeps its name.
        Assert.Equal(@"Steve (HERMES\steve)", AgentFacts.LastUser(new AgentReport { CurrentUser = @"HERMES\steve", LastUser = @".\steve", LastUserDisplayName = "Steve" }));
    }

    [Theory]
    [InlineData("Dell Inc.", "Latitude 5440", null, "Dell", "Latitude 5440")]
    [InlineData("LENOVO", "20XW0026UK", "ThinkPad X1 Carbon Gen 9", "Lenovo", "ThinkPad X1 Carbon Gen 9")]
    [InlineData("HP", "HP ProBook 450 G8 Notebook PC", null, "HP", "HP ProBook 450 G8 Notebook PC")]
    [InlineData("Microsoft Corporation", "Surface Laptop 5", null, "Microsoft", "Surface Laptop 5")]
    [InlineData("Viglen Ltd", "Genie", null, "Viglen", "Genie")]
    [InlineData("Some Maker Ltd.", "Box", null, "Some Maker", "Box")]
    public void Makes_and_models_are_tidied(string manufacturer, string model, string? version, string make, string expected)
    {
        var report = new AgentReport { Manufacturer = manufacturer, Model = model, ProductVersion = version };
        Assert.Equal(make, AgentFacts.Make(report));
        Assert.Equal(expected, AgentFacts.Model(report));
    }

    [Fact]
    public void Facts_read_the_way_people_say_them()
    {
        Assert.Equal("Laptop", AgentFacts.Type(new AgentReport { ChassisTypes = [10] }));
        Assert.Equal("Desktop", AgentFacts.Type(new AgentReport { ChassisTypes = [3] }));
        Assert.Equal("Laptop", AgentFacts.Type(new AgentReport { ChassisTypes = [12], PcSystemType = 2 }));
        Assert.Equal("Desktop", AgentFacts.Type(new AgentReport { ChassisTypes = [2], PcSystemType = 1 }));
        Assert.Equal("16 GB", AgentFacts.Memory(17179869184));
        Assert.Equal("512 GB", AgentFacts.Size(512110190592));
        Assert.Equal("1 TB", AgentFacts.Size(1000204886016));
        Assert.Equal("", AgentFacts.Serial(new AgentReport { SerialNumber = "0000000" }));
        Assert.Equal("", AgentFacts.Uuid(new AgentReport { Uuid = "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" }));
        Assert.Equal("Yes", AgentFacts.Windows11(new AgentReport { OsCaption = "Microsoft Windows 10 Pro", Windows11Blockers = "None" }));
        Assert.Equal("No: TPM 2.0, processor", AgentFacts.Windows11(new AgentReport { OsCaption = "Microsoft Windows 10 Pro", Windows11Blockers = "TPM CpuFms" }));
        Assert.Equal("Already on Windows 11", AgentFacts.Windows11(new AgentReport { OsCaption = "Microsoft Windows 11 Pro" }));
        Assert.Equal("Microsoft Defender (on), Sophos Anti-Virus", AgentFacts.Antivirus(new AgentReport { DefenderOn = true, Antivirus = ["Windows Defender", "Sophos Anti-Virus"] }));
        Assert.Equal("STAFF.LOCAL and Microsoft Entra (Oakwood Academy)", AgentFacts.JoinedTo(new AgentReport { PartOfDomain = true, Domain = "STAFF.LOCAL", EntraTenant = "Oakwood Academy" }));
    }
}
