using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Requesters and technician accounts: adding, editing, deleting and importing them.
public sealed partial class HelpdeskStore
{
    public void AddUser(UserRecord item) { lock (_sync) { _data.Users.Add(WithLeaverDates(item, null)); Save(); } }
    public void AddTechnician(TechnicianRecord item) { lock (_sync) { _data.Technicians.Add(item); Save(); } }
    // Null when the email is free to use as a technician login; otherwise the reason it can't be. Email doubles as the sign-in username.
    public string? CheckTechnicianEmail(string? email, Guid? excludeTechnicianId)
    {
        lock (_sync)
        {
            var value = (email ?? string.Empty).Trim();
            if (value.Length == 0) return "Email is required.";
            return _data.Technicians.Any(x => x.Id != excludeTechnicianId && string.Equals(x.Email, value, StringComparison.OrdinalIgnoreCase))
                ? $"{value} is already used by another technician."
                : null;
        }
    }
    // Null when the email is free to use as a portal login; otherwise the reason it can't be. Email doubles as the portal sign-in username.
    public string? CheckUserEmail(string? email, Guid? excludeUserId)
    {
        lock (_sync)
        {
            var value = (email ?? string.Empty).Trim();
            if (value.Length == 0) return "Email is required.";
            return _data.Users.Any(x => x.Id != excludeUserId && string.Equals(x.Email, value, StringComparison.OrdinalIgnoreCase))
                ? $"{value} is already used by another user."
                : null;
        }
    }
    // The requester record a technician uses in the staff portal, so "Open staff portal" can sign them straight in.
    // Portal tickets belong to a requester, and technicians are kept separately from requesters, so the two are matched
    // by email. A technician with no requester record gets one on first use - with no portal password, so the only way
    // into it is from their technician login. An inactive record is left inactive: someone turned it off on purpose.
    public (UserRecord? User, string? Error) PortalRequesterForTechnician(Guid technicianId)
    {
        lock (_sync)
        {
            var technician = _data.Technicians.FirstOrDefault(x => x.Id == technicianId);
            if (technician is null || !technician.IsActive) return (null, "Your technician account couldn't be found.");
            var email = technician.Email.Trim();
            if (_data.Users.FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)) is { } existing)
                return existing.IsActive
                    ? (existing, null)
                    : (null, $"Your staff record ({existing.Email}) is marked inactive, so it can't be used in the portal. Ask someone who manages requesters to reactivate it.");
            var created = new UserRecord(Guid.NewGuid(), technician.Name, email, string.Empty, string.Empty);
            _data.Users.Add(created);
            Save();
            return (created, null);
        }
    }
    private string UserName(Guid userId) => _data.Users.FirstOrDefault(x => x.Id == userId)?.Name ?? "Unknown user";
    public bool UpdateUser(UserRecord item)
    {
        lock (_sync)
        {
            var index = _data.Users.FindIndex(x => x.Id == item.Id);
            if (index < 0) return false;
            _data.Users[index] = WithLeaverDates(item, _data.Users[index]);
            Save();
            return true;
        }
    }
    public bool UpdateUserAndTickets(UserRecord user, IEnumerable<int> selectedTicketNumbers)
    {
        lock (_sync)
        {
            var userIndex = _data.Users.FindIndex(x => x.Id == user.Id);
            if (userIndex < 0) return false;
            _data.Users[userIndex] = WithLeaverDates(user, _data.Users[userIndex]);
            var selected = selectedTicketNumbers.ToHashSet();
            for (var i = 0; i < _data.Tickets.Count; i++)
            {
                var ticket = _data.Tickets[i];
                if (selected.Contains(ticket.Number))
                    _data.Tickets[i] = ticket with { RequesterId = user.Id };
                else if (ticket.RequesterId == user.Id)
                    continue;
            }
            Save();
            return true;
        }
    }
    // Two-step sign-in belongs to the store (HelpdeskStore.TwoFactor): the edit form builds a new record without it, so it
    // is carried over rather than wiped by every save of someone's name or team.
    public bool UpdateTechnician(TechnicianRecord item)
    {
        lock (_sync)
        {
            var previous = _data.Technicians.FirstOrDefault(x => x.Id == item.Id);
            return Update(previous is null ? item : item with { TwoFactor = previous.TwoFactor, RecoveryKey = previous.RecoveryKey }, _data.Technicians, x => x.Id == item.Id);
        }
    }

    public string? DeleteUser(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.RequesterId == id) || _data.Assets.Any(x => x.AssignedUserId == id))
                return "This user is linked to a ticket or asset and cannot be deleted.";
            if (_data.Projects.Any(x => x.RequesterId == id))
                return "This user raised a project and cannot be deleted. Mark them inactive instead.";
            // Deleting them would delete the record that they ever had access - which is what the register is for.
            if (_data.AccessGrants.Any(x => x.PersonId == id))
                return "This person is on the access control register and cannot be deleted. Mark them inactive and remove their access instead, so the history is kept.";
            var item = _data.Users.FirstOrDefault(x => x.Id == id);
            if (item is null) return "User was not found.";
            _data.Users.Remove(item);
            Save();
            return null;
        }
    }

    public string? DeleteTechnician(Guid id)
    {
        lock (_sync)
        {
            if (_data.Tickets.Any(x => x.TechnicianId == id))
                return "This technician is assigned to a ticket and cannot be deleted.";
            if (_data.Projects.Any(x => x.IsActive && x.TechnicianId == id))
                return "This technician is assigned to an open project and cannot be deleted. Reassign it first.";
            var item = _data.Technicians.FirstOrDefault(x => x.Id == id);
            if (item is null) return "Technician was not found.";
            if (item.Role == StaffRoles.Administrator && !_data.Technicians.Any(x => x.Id != id && x.Role == StaffRoles.Administrator && x.IsActive))
                return "At least one active Administrator must remain.";
            // Closed projects keep their history, which already names who worked them; only the live pointer goes.
            for (var i = 0; i < _data.Projects.Count; i++)
                if (_data.Projects[i].TechnicianId == id) _data.Projects[i] = _data.Projects[i] with { TechnicianId = null };
            _data.Technicians.Remove(item);
            Save();
            return null;
        }
    }
    public (int Imported, int Skipped) ImportUsers(IEnumerable<UserRecord> users)
    {
        lock (_sync)
        {
            var imported = 0;
            var skipped = 0;
            var existingEmails = _data.Users.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var user in users)
            {
                var name = (user.Name ?? string.Empty).Trim();
                var email = (user.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !existingEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                var department = (user.Department ?? string.Empty).Trim();
                var location = (user.Location ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(department) && !_data.Departments.Contains(department, StringComparer.OrdinalIgnoreCase))
                    _data.Departments.Add(department);
                if (!string.IsNullOrWhiteSpace(location) && !_data.Locations.Contains(location, StringComparer.OrdinalIgnoreCase))
                    _data.Locations.Add(location);
                _data.Users.Add(user with { Name = name, Email = email, Department = department, Location = location });
                imported++;
            }
            Save();
            return (imported, skipped);
        }
    }
    public (int Imported, int Skipped) ImportTechnicians(IEnumerable<TechnicianRecord> technicians)
    {
        lock (_sync)
        {
            var imported = 0;
            var skipped = 0;
            var existingEmails = _data.Technicians.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var technician in technicians)
            {
                var name = (technician.Name ?? string.Empty).Trim();
                var email = (technician.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || !existingEmails.Add(email))
                {
                    skipped++;
                    continue;
                }
                var team = (technician.Team ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(team) && !_data.TechnicianTeams.Contains(team, StringComparer.OrdinalIgnoreCase))
                    _data.TechnicianTeams.Add(team);
                _data.Technicians.Add(technician with { Name = name, Email = email, Team = team });
                imported++;
            }
            Save();
            return (imported, skipped);
        }
    }
}
