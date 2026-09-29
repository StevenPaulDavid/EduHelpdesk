using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// The address printed on quick start guides (Pages/People/QuickStart), set in Settings → Sign-in security or by the
// installer (InstallSettings). Empty means "work it out from the address the guide is printed from".
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
