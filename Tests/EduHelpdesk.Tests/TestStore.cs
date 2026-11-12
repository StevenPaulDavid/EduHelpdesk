using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace EduHelpdesk.Tests;

// A HelpdeskStore on a brand-new database in its own temporary folder, deleted afterwards. The store is built
// directly, never through the app's startup, so nothing here can find the project's App_Data or a configured data
// folder: the real database is out of reach by construction.
public sealed class TestStore : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "eduhelpdesk-tests", Guid.NewGuid().ToString("N"));
    public HelpdeskStore Store { get; private set; }

    // A new install has two desks, IT and Facilities (HelpdeskStore.BuiltInDesks). Most tests are about something else
    // and are written against one desk, so Facilities starts switched off unless asked for.
    public TestStore(string? copyOf = null, bool facilities = false)
    {
        Directory.CreateDirectory(Root);
        // A copy of an existing database, taken through SQLite's own backup so the original is only ever read.
        if (copyOf is not null)
        {
            Directory.CreateDirectory(Path.Combine(Root, "App_Data"));
            using var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={copyOf};Mode=ReadOnly;Pooling=False");
            using var target = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            source.Open();
            target.Open();
            source.BackupDatabase(target);
        }
        Store = Open();
        if (copyOf is null && !facilities && Store.BuiltInDesk(DeskKinds.Facilities) is { } desk) Assert.True(Store.SwitchDeskOff(desk.Id).Ok);
    }

    // A second store on the same folder: what the app sees after a restart.
    public HelpdeskStore Reopen() => Store = Open();

    private HelpdeskStore Open() => new(new Environment(Root));

    public string DatabasePath => Path.Combine(Root, "App_Data", "helpdesk.db");

    public UserRecord AddRequester(string name, bool active = true, DateTime? leftAt = null)
    {
        var email = name.ToLowerInvariant().Replace(' ', '.') + "@test.example";
        var user = new UserRecord(Guid.NewGuid(), name, email, "Science", "Room 1", IsActive: active) { LeftAt = leftAt };
        Store.AddUser(user);
        return Store.Users.First(x => x.Id == user.Id);
    }

    public int AddTicket(Guid requesterId, string title = "Projector has no signal", string status = "Open", DateTime? createdAt = null, DateTime? closedAt = null) =>
        Store.AddTicket(new TicketRecord(0, title, "It flickers, then nothing.", requesterId, [], null, "Normal", status, Store.Categories[0],
            createdAt ?? DateTime.UtcNow, closedAt));

    public void Dispose()
    {
        // SQLite keeps pooled connections open; let go of this database's before deleting the folder. Only its own:
        // ClearAllPools here closed every other test's connections mid-use as test classes run in parallel, which
        // failed a random test now and then with "Cannot access a disposed object 'SQLitePCL.sqlite3'" - most often
        // in New-Release.ps1's run, on a busy machine. A pool is keyed by its connection string, so each one the
        // store opens this database with is cleared.
        foreach (var options in new[] { "", ";Mode=ReadOnly" })
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabasePath}{options}"));
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class Environment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "EduHelpdesk.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
