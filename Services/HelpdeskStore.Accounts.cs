using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Account housekeeping for quick start guides (Pages/People/QuickStart): the address printed on them, set in Settings →
// Sign-in security or by the installer (InstallSettings) - empty means "work it out from the address the guide is
// printed from" - and temporary passwords for many accounts at once.
public sealed partial class HelpdeskStore
{
    public string SiteAddress { get { lock (_sync) return _data.SiteAddress; } }

    public (bool Ok, string Message) SetSiteAddress(string? address)
    {
        string value;
        if (string.IsNullOrWhiteSpace(address)) value = "";
        else if (NormaliseSiteAddress(address) is { } normalised) value = normalised;
        else return (false, "Type the whole address, starting http:// or https://, such as https://helpdesk.school.org.uk/.");
        lock (_sync)
        {
            if (_data.SiteAddress == value) return (true, "Nothing changed.");
            var before = _data.SiteAddress;
            _data.SiteAddress = value;
            _pendingAudit.Add(new AuditEntry(DateTime.UtcNow, "Sign-in", null, null, "Helpdesk address", "Changed",
                $"{(before.Length == 0 ? "Worked out from the address in use" : before)} → {(value.Length == 0 ? "worked out from the address in use" : value)}"));
            Save();
            return (true, value.Length == 0 ? "Quick start guides will use the address they are printed from." : $"Quick start guides will give {value} as the address.");
        }
    }

    // Active requesters who can't sign in to the portal yet because nobody has given them a password - usually staff
    // imported from a CSV. The portal records made for technicians (PortalRequesterForTechnician) are left out: they are
    // reached through the technician's own sign-in and are meant to have no password.
    public IReadOnlyList<UserRecord> RequestersWithoutPassword()
    {
        lock (_sync)
        {
            var technicianEmails = _data.Technicians.Select(x => x.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _data.Users.Where(x => x.IsActive && x.AnonymisedAt is null && x.PasswordHash is null && !technicianEmails.Contains(x.Email))
                .OrderBy(x => x.Department, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public IReadOnlyList<TechnicianRecord> TechniciansWithoutPassword()
    {
        lock (_sync) return _data.Technicians.Where(x => x.IsActive && x.PasswordHash is null).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // New passwords for many accounts in one save, each to be changed at the next sign-in (TemporaryPasswords.Issue).
    // Returns how many accounts were found and changed.
    public int SetTemporaryPasswords(IReadOnlyDictionary<Guid, string> hashes, bool technicians)
    {
        lock (_sync)
        {
            var changed = 0;
            if (technicians)
            {
                for (var i = 0; i < _data.Technicians.Count; i++)
                    if (hashes.TryGetValue(_data.Technicians[i].Id, out var hash)) { _data.Technicians[i] = _data.Technicians[i] with { PasswordHash = hash, RequirePasswordChange = true }; changed++; }
            }
            else
            {
                for (var i = 0; i < _data.Users.Count; i++)
                    if (hashes.TryGetValue(_data.Users[i].Id, out var hash)) { _data.Users[i] = _data.Users[i] with { PasswordHash = hash, RequirePasswordChange = true }; changed++; }
            }
            if (changed > 0) Save();
            return changed;
        }
    }

    // "helpdesk.school.org.uk:8080/" style answers are refused rather than guessed at: a wrong address on a printed
    // sheet sends people nowhere.
    public static string? NormaliseSiteAddress(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host) || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0) return null;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }
}
