using System.Text.RegularExpressions;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Bringing tickets across from Spiceworks Cloud Help Desk (Settings → Imports → From Spiceworks). The workbook is read by
// SpiceworksExport; this works out what it would become here. Planning changes nothing - it is the preview.
//
// Decided with the school (see the Technical Guide): requesters are matched on email and added to People when missing;
// tickets with no requester go under one placeholder; assignees are matched to technicians by email or name, otherwise
// left unassigned; statuses, priorities and categories are mapped in the preview; imported tickets get new numbers with
// the Spiceworks number kept; comments, history, merges and time entries come across; attachments aren't in the export.
public sealed partial class HelpdeskStore
{
    // A mapping target meaning "add the Spiceworks value to the list as it is".
    public const string SpiceworksNewValue = "+new";
    public const string SpiceworksNoCategory = "";
    public static readonly string[] SpiceworksValueKinds = ["Status", "Priority", "Category"];

    public sealed record SpiceworksValueChoice(string Kind, string Value, string Label, int Tickets, string Chosen, IReadOnlyList<string> Options)
    {
        public string Key => ChoiceKey(Kind, Value);
        public bool CanBeNew => Value.Length > 0;
    }

    public sealed record SpiceworksRequester(string Name, string Email, int Tickets, string? ExistingName);
    public sealed record SpiceworksTechnician(string Name, string Email, int Tickets, string? MatchedTo);
    public sealed record SpiceworksAttribute(string Label, int Values, bool Exists);

    public sealed record SpiceworksPlan(
        string? Organization,
        int Tickets, int OpenTickets, DateTimeOffset? From, DateTimeOffset? To,
        int Comments, int InternalNotes, int RequesterReplies, int EmptyComments,
        int HistoryLines, int Merges, int TimeEntries, int DueDates,
        IReadOnlyList<SpiceworksRequester> Requesters, int PlaceholderTickets,
        IReadOnlyList<SpiceworksTechnician> Technicians, int UnassignedTickets,
        IReadOnlyList<SpiceworksValueChoice> Values,
        IReadOnlyList<SpiceworksAttribute> Attributes,
        IReadOnlyList<string> Problems)
    {
        public int NewRequesters => Requesters.Count(x => x.ExistingName is null);
        public int MatchedRequesters => Requesters.Count(x => x.ExistingName is not null);
        public int NewValues => Values.Count(x => x.Chosen == SpiceworksNewValue);
    }

    public static string ChoiceKey(string kind, string value) => $"{kind}|{value}";

    // Spiceworks numbers its priorities; these are the names it shows for them.
    public static string SpiceworksPriorityName(string value) => value switch { "1" => "High", "2" => "Medium", "3" => "Low", _ => value };

    public SpiceworksPlan PlanSpiceworksImport(SpiceworksExport export, IReadOnlyDictionary<string, string> choices)
    {
        lock (_sync)
        {
            var problems = new List<string>(export.Problems);
            var ticketIds = export.Tickets.Select(x => x.Id).ToHashSet();
            var endUsers = export.EndUsers.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var technicians = export.Technicians.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var categories = export.Categories.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Name);

            // ---- Requesters: everyone a ticket or reply names, matched on email ----
            var usersByEmail = _data.Users.Where(x => !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var ticketsByEndUser = export.Tickets.Where(x => x.EndUserId is not null).GroupBy(x => x.EndUserId!.Value).ToDictionary(g => g.Key, g => g.Count());
            var named = ticketsByEndUser.Keys.Concat(export.Comments.Where(x => x.EndUserId is not null && ticketIds.Contains(x.TicketId)).Select(x => x.EndUserId!.Value)).Distinct().ToList();
            var requesters = new List<SpiceworksRequester>();
            var missingPeople = 0;
            foreach (var id in named)
            {
                if (!endUsers.TryGetValue(id, out var person) || string.IsNullOrWhiteSpace(person.Email)) { missingPeople++; continue; }
                usersByEmail.TryGetValue(person.Email.Trim(), out var existing);
                requesters.Add(new SpiceworksRequester(person.Name, person.Email.Trim(), ticketsByEndUser.GetValueOrDefault(id), existing?.Name));
            }
            if (missingPeople > 0) problems.Add($"{missingPeople} requester{(missingPeople == 1 ? " is" : "s are")} named on tickets or replies but {(missingPeople == 1 ? "isn't" : "aren't")} in the End Users sheet with an email address - probably removed from Spiceworks. Their tickets go under the placeholder requester.");

            // ---- Technicians: matched on email, then on name ----
            var techMatches = technicians.Values.ToDictionary(p => p.Id, p => MatchTechnician(p));
            var techs = technicians.Values
                .Select(p => new SpiceworksTechnician(p.Name, p.Email, export.Tickets.Count(x => x.AssigneeId == p.Id), techMatches[p.Id]?.Name)).ToList();
            var unassigned = export.Tickets.Count(x => x.AssigneeId is not { } assignee || techMatches.GetValueOrDefault(assignee) is null);

            // ---- Values to map ----
            string CategoryOf(SpiceworksExport.Ticket ticket) =>
                ticket.CategoryId is { } id && categories.TryGetValue(id, out var name) && name.Trim().Length > 0 ? name.Trim() : SpiceworksNoCategory;
            var unknownCategories = export.Tickets.Count(x => x.CategoryId is { } id && !categories.ContainsKey(id));
            if (unknownCategories > 0) problems.Add($"{unknownCategories} ticket{(unknownCategories == 1 ? " has" : "s have")} a category that isn't in the Ticket Categories sheet; {(unknownCategories == 1 ? "it is" : "they are")} treated as having no category.");

            var values = new List<SpiceworksValueChoice>();
            void Add(string kind, IEnumerable<string> used, IReadOnlyList<string> existing, Func<string, string> label)
            {
                foreach (var group in used.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
                {
                    var value = group.Key;
                    var suggested = SuggestFor(kind, value, existing);
                    var chosen = choices.TryGetValue(ChoiceKey(kind, value), out var picked)
                        && (existing.Contains(picked, StringComparer.OrdinalIgnoreCase) || (picked == SpiceworksNewValue && value.Length > 0))
                        ? picked : suggested;
                    values.Add(new SpiceworksValueChoice(kind, value, label(value), group.Count(), chosen, existing));
                }
            }
            Add("Status", export.Tickets.Select(x => x.Status.Trim()), _data.Statuses, x => x);
            Add("Priority", export.Tickets.Select(x => x.Priority.Trim()), _data.Priorities, x => x == SpiceworksPriorityName(x) ? x : $"{x} ({SpiceworksPriorityName(x)})");
            Add("Category", export.Tickets.Select(CategoryOf), _data.Categories, x => x.Length == 0 ? "(no category)" : x);

            // ---- Custom fields: kept as ticket custom attributes ----
            var attributes = export.Attributes.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
            var attributeValues = export.AttributeValues.Where(x => ticketIds.Contains(x.TicketId)).ToList();
            var orphaned = attributeValues.Count(x => !attributes.ContainsKey(x.AttributeId));
            if (orphaned > 0) problems.Add($"{orphaned} custom field value{(orphaned == 1 ? " belongs" : "s belong")} to fields since deleted in Spiceworks (they have no name), so {(orphaned == 1 ? "it is" : "they are")} left out.");
            var attributePlans = attributes.Values
                .Select(a => new SpiceworksAttribute(a.Label, attributeValues.Count(v => v.AttributeId == a.Id),
                    _data.TicketAttributeDefinitions.Any(d => string.Equals(d.Name, a.Label, StringComparison.OrdinalIgnoreCase))))
                .Where(x => x.Values > 0).OrderBy(x => x.Label).ToList();

            // ---- The rest ----
            var comments = export.Comments.Where(x => ticketIds.Contains(x.TicketId)).ToList();
            var strays = export.Comments.Count - comments.Count + export.Changes.Count(x => !ticketIds.Contains(x.TicketId));
            if (strays > 0) problems.Add($"{strays} comment{(strays == 1 ? " or history line belongs" : "s or history lines belong")} to tickets that aren't in the Tickets sheet, so {(strays == 1 ? "it is" : "they are")} left out.");
            var numbers = export.Tickets.Select(x => x.Number).ToHashSet();
            var lostMerges = export.Tickets.Count(x => x.MasterNumber is { } master && !numbers.Contains(master));
            if (lostMerges > 0) problems.Add($"{lostMerges} merged ticket{(lostMerges == 1 ? " points" : "s point")} at a ticket that isn't in the export; the merge is noted in {(lostMerges == 1 ? "its" : "their")} history but can't be linked.");

            return new SpiceworksPlan(export.OrganizationName,
                export.Tickets.Count, export.Tickets.Count(x => !string.Equals(x.Status, "Closed", StringComparison.OrdinalIgnoreCase)),
                export.Tickets.Count == 0 ? null : export.Tickets.Min(x => x.CreatedAt), export.Tickets.Count == 0 ? null : export.Tickets.Max(x => x.CreatedAt),
                comments.Count(x => x.Body.Length > 0), comments.Count(x => x.Body.Length > 0 && x.Private), comments.Count(x => x.Body.Length > 0 && x.EndUserId is not null),
                comments.Count(x => x.Body.Length == 0),
                export.Changes.Count(x => ticketIds.Contains(x.TicketId)), export.Tickets.Count(x => x.MasterNumber is not null),
                export.Labors.Count(x => ticketIds.Contains(x.TicketId)), export.Tickets.Count(x => x.DueAt is not null),
                requesters.OrderBy(x => x.ExistingName is not null).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                export.Tickets.Count(x => x.EndUserId is not { } id || !endUsers.TryGetValue(id, out var p) || string.IsNullOrWhiteSpace(p.Email)),
                techs.OrderBy(x => x.Name).ToList(), unassigned,
                values, attributePlans, problems);
        }
    }

    // A Spiceworks technician's EduHelpdesk account: by email, then by name. Called under the lock.
    private TechnicianRecord? MatchTechnician(SpiceworksExport.Person person) =>
        (person.Email.Trim().Length > 0 ? _data.Technicians.FirstOrDefault(x => string.Equals(x.Email.Trim(), person.Email.Trim(), StringComparison.OrdinalIgnoreCase)) : null)
        ?? _data.Technicians.FirstOrDefault(x => string.Equals(x.Name.Trim(), person.Name, StringComparison.OrdinalIgnoreCase));

    // The same name if the list has it; otherwise a close one for the values Spiceworks always uses; otherwise add it.
    private static string SuggestFor(string kind, string value, IReadOnlyList<string> existing)
    {
        if (existing.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) is { } same) return same;
        string? Like(string pattern) => existing.FirstOrDefault(x => Regex.IsMatch(x, pattern, RegexOptions.IgnoreCase));
        var close = kind switch
        {
            "Priority" => SpiceworksPriorityName(value) switch
            {
                "High" => Like("^high") ?? Like("urgent|critical"),
                "Medium" => Like("^(medium|normal)") ?? Like("medium|normal|standard"),
                "Low" => Like("^low"),
                _ => null
            },
            "Status" => value.ToLowerInvariant() switch
            {
                "waiting" => Like("wait|hold|pending"),
                "open" => Like("^open|^new"),
                _ => null
            },
            "Category" when value.Length == 0 => Like("^other") ?? existing.FirstOrDefault(),
            _ => null
        };
        return close ?? (value.Length == 0 ? existing.FirstOrDefault() ?? SpiceworksNewValue : SpiceworksNewValue);
    }
}
