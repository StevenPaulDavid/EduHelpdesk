using System.Globalization;
using EduHelpdesk.Models;
using Microsoft.Data.Sqlite;

namespace EduHelpdesk.Services;

// What a job cost (stage 3 of desks): any number of lines on a ticket - a contractor's call-out, the materials - each with
// its net amount and VAT typed separately, an optional supplier and a budget code (Settings → Budget codes: reactive or
// planned maintenance, say). Seeing and adding them takes the "See and add job costs" tick; the spend report its own.
public sealed partial class HelpdeskStore
{
    public const int MaxCostDescriptionLength = 200;
    private static readonly string[] StarterBudgetCodes = ["Reactive maintenance", "Planned maintenance (PPM)"];

    public IReadOnlyList<string> BudgetCodes { get { lock (_sync) return _data.BudgetCodes.ToList(); } }

    public IReadOnlyList<TicketCost> CostsFor(int number)
    {
        lock (_sync) return _data.TicketCosts.Where(x => x.TicketNumber == number).OrderBy(x => x.Date).ThenBy(x => x.AddedAt).ToList();
    }

    public IReadOnlyList<TicketCost> TicketCosts { get { lock (_sync) return _data.TicketCosts.ToList(); } }

    public sealed record CostInput(DateOnly? Date, string? Description, Guid? SupplierId, string? Net, string? Vat, string? BudgetCode);

    public (bool Ok, string Message) AddTicketCost(int number, CostInput input)
    {
        lock (_sync)
        {
            var index = _data.Tickets.FindIndex(x => x.Number == number);
            if (index < 0) return (false, "Ticket was not found.");
            var description = (input.Description ?? "").Trim();
            if (description.Length == 0) return (false, "Say what the cost was for.");
            if (description.Length > MaxCostDescriptionLength) return (false, $"Keep the description to {MaxCostDescriptionLength} characters.");
            if (ParseMoney(input.Net) is not { } net) return (false, "Enter the amount before VAT, such as 120.00.");
            decimal vat = 0;
            if (!string.IsNullOrWhiteSpace(input.Vat))
            {
                if (ParseMoney(input.Vat) is not { } typedVat) return (false, "Enter the VAT as an amount, such as 24.00, or leave it blank for none.");
                vat = typedVat;
            }
            if (net < 0 || vat < 0) return (false, "Amounts can't be negative. Record a refund as a note instead.");
            Guid? supplier = null;
            if (input.SupplierId is { } supplierId)
            {
                if (!_data.Suppliers.Any(x => x.Id == supplierId)) return (false, "Choose a supplier from the list.");
                supplier = supplierId;
            }
            var code = "";
            if (!string.IsNullOrWhiteSpace(input.BudgetCode))
            {
                code = _data.BudgetCodes.FirstOrDefault(x => string.Equals(x, input.BudgetCode.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
                if (code.Length == 0) return (false, "Choose a budget code from the list.");
            }
            var date = input.Date ?? DateOnly.FromDateTime(DateTime.Now);
            var cost = new TicketCost(Guid.NewGuid(), number, date, description, supplier, net, vat, code) { AddedAt = DateTime.UtcNow, By = CurrentActor() };
            _data.TicketCosts.Add(cost);
            AddCostHistory(index, "Cost added", cost);
            Save();
            return (true, $"Cost of {FormatMoney(cost.Gross)} added.");
        }
    }

    public (bool Ok, string Message) RemoveTicketCost(int number, Guid id)
    {
        lock (_sync)
        {
            var cost = _data.TicketCosts.FirstOrDefault(x => x.Id == id && x.TicketNumber == number);
            if (cost is null) return (false, "That cost has already been removed.");
            _data.TicketCosts.Remove(cost);
            AddCostHistory(_data.Tickets.FindIndex(x => x.Number == number), "Cost removed", cost);
            Save();
            return (true, "Cost removed.");
        }
    }

    // Who can see what it cost is a separate question from who works the ticket, so the history says only that a cost
    // line came or went, not how much.
    private void AddCostHistory(int index, string action, TicketCost cost)
    {
        if (index < 0) return;
        var history = _data.Tickets[index].History.ToList();
        history.Add(new TicketActivity(action, $"A cost line was {(action == "Cost added" ? "added" : "removed")}: {cost.Description}.", DateTime.UtcNow) { By = CurrentActor() });
        _data.Tickets[index] = _data.Tickets[index] with { History = history };
    }

    // "120", "120.5", "£1,200.00" - in pounds, to the penny.
    public static decimal? ParseMoney(string? text)
    {
        var cleaned = (text ?? "").Trim().Replace("£", "").Replace(",", "").Trim();
        return cleaned.Length > 0 && decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value < 100_000_000
            ? Math.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    // A starting list the first time costs exist, so a school isn't faced with an empty box; theirs to rename or remove.
    private void EnsureBudgetCodes()
    {
        if (_data.BudgetCodesSeeded) return;
        EnsureOptions(_data.BudgetCodes, StarterBudgetCodes);
        _data.BudgetCodesSeeded = true;
    }

    // ---- Storage ----

    private static void EnsureCostSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS BudgetCodes (Name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS TicketCosts (Id TEXT PRIMARY KEY, TicketNumber INTEGER NOT NULL, CostDate TEXT NOT NULL, Description TEXT NOT NULL,
                SupplierId TEXT NULL, Net TEXT NOT NULL, Vat TEXT NOT NULL, BudgetCode TEXT NOT NULL DEFAULT '', AddedAt TEXT NOT NULL, Actor TEXT NULL, ActorId TEXT NULL,
                FOREIGN KEY (TicketNumber) REFERENCES Tickets(Number) ON DELETE CASCADE);
            """;
        command.ExecuteNonQuery();
    }

    private static void ReadCosts(SqliteConnection connection, StoreData data)
    {
        ReadStrings(connection, "BudgetCodes", data.BudgetCodes);
        data.BudgetCodesSeeded = ExecuteScalar(connection, "SELECT Value FROM Metadata WHERE Key = 'BudgetCodesSeeded';") as string == "1";
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, TicketNumber, CostDate, Description, SupplierId, Net, Vat, BudgetCode, AddedAt, Actor, ActorId FROM TicketCosts ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            data.TicketCosts.Add(new TicketCost(Guid.Parse(reader.GetString(0)), reader.GetInt32(1),
                DateOnly.TryParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : DateOnly.FromDateTime(DateTime.Today),
                reader.GetString(3), NullableGuid(reader, 4),
                decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture), decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture), reader.GetString(7))
            {
                AddedAt = Date(reader, 8),
                By = ReadActor(reader, 9, 10)
            });
    }

    // After the Tickets loop, which TicketCosts has a foreign key to.
    private static void WriteCosts(SqliteConnection connection, SqliteTransaction transaction, StoreData data, HashSet<int> ticketNumbers)
    {
        SetMetadata(connection, transaction, "BudgetCodesSeeded", data.BudgetCodesSeeded ? "1" : "0");
        InsertStrings(connection, transaction, "BudgetCodes", data.BudgetCodes);
        foreach (var cost in data.TicketCosts.Where(x => ticketNumbers.Contains(x.TicketNumber)))
            Execute(connection, transaction, "INSERT INTO TicketCosts (Id, TicketNumber, CostDate, Description, SupplierId, Net, Vat, BudgetCode, AddedAt, Actor, ActorId) VALUES ($id,$ticket,$date,$description,$supplier,$net,$vat,$code,$added,$actor,$actorid);",
                ("$id", cost.Id.ToString()), ("$ticket", cost.TicketNumber), ("$date", cost.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$description", cost.Description),
                ("$supplier", cost.SupplierId?.ToString()), ("$net", cost.Net.ToString(CultureInfo.InvariantCulture)), ("$vat", cost.Vat.ToString(CultureInfo.InvariantCulture)),
                ("$code", cost.BudgetCode), ("$added", Iso(cost.AddedAt)), ("$actor", cost.By?.Name), ("$actorid", cost.By?.Id?.ToString()));
    }
}
