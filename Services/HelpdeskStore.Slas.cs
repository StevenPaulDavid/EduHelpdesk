using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// SLAs, and the school week and periods that work-day and period SLAs count against.
public sealed partial class HelpdeskStore
{
        public string AddSla(string name, int duration, string durationUnit, string? description, IEnumerable<string>? priorities, IEnumerable<string>? categories)
        {
            lock (_sync)
            {
                name = name.Trim();
                durationUnit = NormalizeDurationUnit(durationUnit);
                if (string.IsNullOrWhiteSpace(name)) return "SLA name is required.";
                if (duration < 1) return "SLA duration must be at least 1.";
                if (PeriodsMissing(durationUnit) is { } periodsError) return periodsError;
                if (_data.Slas.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return "That SLA already exists.";
                var validPriorities = (priorities ?? []).Where(x => _data.Priorities.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var validCategories = (categories ?? []).Where(x => _data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _data.Slas.Add(new(Guid.NewGuid(), name, duration, durationUnit, string.IsNullOrWhiteSpace(description) ? null : description.Trim()) { Priorities = validPriorities, Categories = validCategories });
                Save();
                return "SLA added.";
            }
        }
        public string UpdateSla(Guid id, string name, int duration, string durationUnit, string? description, IEnumerable<string>? priorities, IEnumerable<string>? categories)
        {
            lock (_sync)
            {
                var index = _data.Slas.FindIndex(x => x.Id == id);
                if (index < 0) return "SLA was not found.";
                name = name.Trim();
                durationUnit = NormalizeDurationUnit(durationUnit);
                if (string.IsNullOrWhiteSpace(name)) return "SLA name is required.";
                if (duration < 1) return "SLA duration must be at least 1.";
                if (PeriodsMissing(durationUnit) is { } periodsError) return periodsError;
                if (_data.Slas.Any(x => x.Id != id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return "That SLA already exists.";
                var validPriorities = (priorities ?? []).Where(x => _data.Priorities.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var validCategories = (categories ?? []).Where(x => _data.Categories.Contains(x, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _data.Slas[index] = new(id, name, duration, durationUnit, string.IsNullOrWhiteSpace(description) ? null : description.Trim()) { Priorities = validPriorities, Categories = validCategories };
                Save();
                return "SLA updated.";
            }
        }
        public string DeleteSla(Guid id)
        {
            lock (_sync)
            {
                if (_data.Tickets.Any(x => x.SlaId == id)) return "This SLA is used by tickets and cannot be deleted.";
                var index = _data.Slas.FindIndex(x => x.Id == id);
                if (index < 0) return "SLA was not found.";
                _data.Slas.RemoveAt(index);
                // A template that named this SLA goes back to working the SLA out from the priority and category.
                for (var i = 0; i < _data.TicketTemplates.Count; i++)
                    if (_data.TicketTemplates[i].SlaId == id) _data.TicketTemplates[i] = _data.TicketTemplates[i] with { SlaId = null };
                Save();
                return "SLA deleted.";
            }
        }
        // A periods SLA has nothing to count until the timetable exists, and would quietly give tickets no due date.
        private string? PeriodsMissing(string durationUnit) =>
            durationUnit == SlaUnits.Periods && _data.Periods.Count == 0
                ? "Set up the school's periods first (Settings → School day and periods), then add an SLA measured in periods."
                : null;

        public IReadOnlyList<DayOfWeek> SchoolDays { get { lock (_sync) return _data.SchoolDays.ToList(); } }
        public IReadOnlyList<SchoolPeriod> SchoolPeriods { get { lock (_sync) return _data.Periods.ToList(); } }

        public const int MaxSchoolPeriods = 20;

        // Which days of the week are school days. At least one is required: a week with none would push every
        // work-day and period SLA a year out.
        public (bool Ok, string Message) SaveSchoolDays(IEnumerable<DayOfWeek>? days)
        {
            lock (_sync)
            {
                var chosen = (days ?? []).Distinct().OrderBy(x => ((int)x + 6) % 7).ToList();
                if (chosen.Count == 0) return (false, "Choose at least one school day.");
                _data.SchoolDays = chosen;
                Save();
                return (true, "School days saved.");
            }
        }

        // Replaces the whole timetable. Periods are sorted by start time, and may not overlap - a minute can only belong
        // to one period, or "the next period" stops meaning anything. Existing tickets keep the due dates they were
        // given; only tickets logged (or re-worked out) from now on use the new timings.
        public (bool Ok, string Message) SaveSchoolPeriods(IEnumerable<SchoolPeriod>? periods)
        {
            lock (_sync)
            {
                var list = (periods ?? []).Select(x => x with { Name = (x.Name ?? string.Empty).Trim() }).OrderBy(x => x.Start).ToList();
                if (list.Count > MaxSchoolPeriods) return (false, $"A school day can have at most {MaxSchoolPeriods} periods.");
                if (list.FirstOrDefault(x => x.Name.Length == 0) is { } unnamed) return (false, $"Give the period starting {unnamed.Start:HH:mm} a name.");
                if (list.FirstOrDefault(x => x.End <= x.Start) is { } backwards) return (false, $"{backwards.Name} has to end after it starts.");
                var duplicate = list.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                if (duplicate is not null) return (false, $"There are two periods called {duplicate.Key}.");
                for (var i = 1; i < list.Count; i++)
                    if (list[i].Start < list[i - 1].End)
                        return (false, $"{list[i - 1].Name} ({list[i - 1].Start:HH:mm}–{list[i - 1].End:HH:mm}) overlaps {list[i].Name} ({list[i].Start:HH:mm}–{list[i].End:HH:mm}).");
                if (list.Count == 0 && _data.Slas.FirstOrDefault(x => x.DurationUnit == SlaUnits.Periods) is { } inUse)
                    return (false, $"The {inUse.Name} SLA is measured in periods, so at least one period is needed. Change that SLA first.");
                _data.Periods = list;
                Save();
                return (true, list.Count == 0 ? "Periods cleared." : $"{list.Count} period{(list.Count == 1 ? "" : "s")} saved.");
            }
        }

        // Work days and periods follow the school week and timetable - see SlaClock. Callers pass UTC; anything marked
        // local is converted first, since the clock works the school's wall-clock time out from UTC itself.
        public DateTime? CalculateDueDate(Guid? slaId, DateTime createdAt) =>
            slaId is Guid id && _data.Slas.FirstOrDefault(x => x.Id == id) is { } sla
                ? SlaClock.Due(createdAt.Kind == DateTimeKind.Local ? createdAt.ToUniversalTime() : createdAt, sla.Duration, sla.DurationUnit, _data.SchoolDays, _data.Periods)
                : null;
}
