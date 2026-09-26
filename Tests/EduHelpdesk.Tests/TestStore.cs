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

    public TestStore()
    {
        Directory.CreateDirectory(Root);
        Store = Open();
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
        // SQLite keeps pooled connections open; let go of them before deleting the folder.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
