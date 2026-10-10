using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using EduHelpdesk.Models;

namespace EduHelpdesk.Services;

// Turns HelpdeskStore.GatherSubjectAccess into a zip a DPO can review and hand over:
//   README.txt   - what is in it, and what to check before it goes to the person
//   report.html  - the same, readable in any browser and printable to PDF
//   data.json    - everything, as data
//   files/       - the files attached to their tickets
// Internal notes are included and clearly marked, because they are about the person too; whether any of it is
// withheld under an exemption is the DPO's decision, not the software's.
public static class SubjectAccessExport
{
    public static string FileName(UserRecord person, DateTime utc) =>
        $"subject-access-{Slug(person.Name)}-{utc.ToLocalTime():yyyy-MM-dd}.zip";

    public static byte[] Build(HelpdeskStore.SubjectAccessData data, string schoolName)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = new List<(HelpdeskStore.SubjectAccessAttachment Item, string? ZipPath)>();
            foreach (var item in data.Attachments)
            {
                string? path = null;
                if (File.Exists(item.FilePath))
                {
                    path = $"files/ticket-{item.Attachment.TicketNumber}/{item.Attachment.Id.ToString("N")[..8]}-{SafeFileName(item.Attachment.FileName)}";
                    zip.CreateEntryFromFile(item.FilePath, path, CompressionLevel.Optimal);
                }
                files.Add((item, path));
            }
            Write(zip, "README.txt", Readme(data, schoolName, files.Count(x => x.ZipPath is null)));
            Write(zip, "report.html", Report(data, schoolName, files));
            Write(zip, "data.json", Json(data, files));
        }
        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        var bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes);
    }

    private static string Readme(HelpdeskStore.SubjectAccessData d, string school, int missingFiles)
    {
        var text = new StringBuilder();
        text.AppendLine($"Subject access export - {d.Person.Name}");
        text.AppendLine($"From the {school} helpdesk, {Local(d.GeneratedAt)}, downloaded by {d.GeneratedBy.Name}.");
        text.AppendLine();
        text.AppendLine("What is in this folder");
        text.AppendLine("  report.html  Everything below, readable in any web browser. Print it to PDF to send it.");
        text.AppendLine("  data.json    The same information as data.");
        text.AppendLine("  files/       Files attached to the tickets they raised.");
        text.AppendLine();
        text.AppendLine("What it covers");
        text.AppendLine($"  - Their record: name, email, department, location, account status and portal permissions. (Passwords are never stored in a readable form, so none is included.)");
        text.AppendLine($"  - {d.Tickets.Count} ticket(s) they raised, with every update, internal note and history line, and {d.Attachments.Count} attached file(s).");
        text.AppendLine($"  - {d.Assets.Count} asset(s) they hold or have held, and {d.KitLoans.Count} loan kit loan(s).");
        text.AppendLine($"  - {d.Projects.Count} project(s) they asked for, with notes and history.");
        text.AppendLine($"  - {d.Access.Count} line(s) on the access control register: systems and areas they have or had access to.");
        text.AppendLine($"  - {d.Mentions.Count} other ticket(s) that mention their full name or email - listed, not copied.");
        text.AppendLine($"  - {d.AuditEntries.Count} audit log line(s) about their record or their use of the staff portal.");
        text.AppendLine();
        text.AppendLine("Before you send it");
        text.AppendLine("  - Internal notes are included and marked INTERNAL. They were written by IT staff and may name other people");
        text.AppendLine("    or contain opinions: check them, and redact or withhold anything an exemption applies to.");
        text.AppendLine("  - Tickets they raised can mention other people. Check for third-party information.");
        text.AppendLine("  - The \"Mentioned in\" list points at other people's tickets. Look at those in the helpdesk and decide what,");
        text.AppendLine("    if anything, from them belongs in the response.");
        if (missingFiles > 0) text.AppendLine($"  - {missingFiles} attached file(s) were listed on tickets but missing from the attachments folder, so aren't included.");
        text.AppendLine("  - Backups also hold copies of this data until they age out (Settings > Backups & data).");
        return text.ToString();
    }

    private static string Report(HelpdeskStore.SubjectAccessData d, string school, IReadOnlyList<(HelpdeskStore.SubjectAccessAttachment Item, string? ZipPath)> files)
    {
        var p = d.Person;
        var html = new StringBuilder();
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        string Tech(Guid? id) => id is { } t ? d.TechnicianNames.GetValueOrDefault(t, "Unknown") : "Unassigned";
        void Row(string label, string? value) => html.Append($"<tr><th>{E(label)}</th><td>{E(string.IsNullOrWhiteSpace(value) ? "-" : value)}</td></tr>");

        html.Append($$"""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>Subject access export - {{E(p.Name)}}</title>
            <style>
            body{font:14px/1.5 system-ui,-apple-system,"Segoe UI",sans-serif;color:#17252f;max-width:900px;margin:32px auto;padding:0 20px}
            h1{font-size:26px;margin:0 0 4px} h2{font-size:18px;margin:32px 0 10px;border-bottom:2px solid #17252f;padding-bottom:4px}
            h3{font-size:15px;margin:22px 0 6px} .muted{color:#5b6b72}
            table{border-collapse:collapse;width:100%;margin:6px 0 12px} th,td{text-align:left;vertical-align:top;padding:5px 8px;border-bottom:1px solid #dce5e7}
            th{width:28%;color:#3d4e56;font-weight:600}
            .entry{border-left:3px solid #dce5e7;padding:6px 10px;margin:6px 0;white-space:pre-wrap}
            .internal{border-left-color:#d97706;background:#fff7ed} .tag{font-size:11px;font-weight:700;letter-spacing:.05em;color:#b45309}
            .note{background:#f5f8f8;border:1px solid #dce5e7;padding:10px 14px;border-radius:6px}
            @media print{body{margin:0} h2{break-after:avoid} .entry{break-inside:avoid} }
            </style></head><body>
            <h1>{{E(p.Name)}}</h1>
            <p class="muted">Everything the {{E(school)}} IT helpdesk holds about them, as of {{E(Local(d.GeneratedAt))}}.</p>
            <p class="note">Entries marked <span class="tag">INTERNAL</span> are notes IT staff kept for themselves; they were never shown in the staff portal.</p>
            """);

        html.Append("<h2>Their record</h2><table>");
        Row("Name", p.Name); Row("Email", p.Email); Row("Department", p.Department); Row("Location", p.Location);
        Row("Account", p.IsActive ? "Active" : p.LeftAt is { } left ? $"Inactive since {Local(left)}" : "Inactive");
        Row("Staff portal password", p.PasswordHash is null ? "None set" : "Set (stored scrambled, so it can't be shown)");
        Row("Can ask for projects", p.CanRaiseProjects ? "Yes" : "No");
        Row("Project lead", p.IsProjectLead ? "Yes" : "No");
        Row("Type", AccessRules.PersonType(p));
        Row("Start date", p.StartDate?.ToString("d MMM yyyy", CultureInfo.CurrentCulture));
        html.Append("</table>");

        html.Append($"<h2>Access control register ({d.Access.Count})</h2>");
        if (d.Access.Count == 0) html.Append("<p class=\"muted\">None.</p>");
        else
        {
            html.Append("<table>");
            foreach (var (g, resource) in d.Access)
            {
                static string Day(DateOnly? x) => x?.ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? "";
                var line = $"{(g.AccessLevel.Length > 0 ? g.AccessLevel + ". " : "")}{(g.Identifier.Length > 0 ? $"Account or key: {g.Identifier}. " : "")}{(g.Privileged ? "Privileged. " : "")}"
                    + $"Granted {Day(g.GrantedOn)}{(g.GrantedBy.Length > 0 ? " by " + g.GrantedBy : "")}. "
                    + (g.RevokedOn is { } off ? $"Removed {Day(off)}{(g.RevokeReason.Length > 0 ? " - " + g.RevokeReason : "")}." : "Still active.")
                    + (g.Notes.Length > 0 ? $" Notes: {g.Notes}" : "");
                html.Append($"<tr><th>{E(resource)}</th><td>{E(line)}</td></tr>");
            }
            html.Append("</table>");
        }

        html.Append($"<h2>Tickets they raised ({d.Tickets.Count})</h2>");
        if (d.Tickets.Count == 0) html.Append("<p class=\"muted\">None.</p>");
        foreach (var t in d.Tickets)
        {
            html.Append($"<h3>#{t.Number} {E(t.Title)}</h3><table>");
            Row("Raised", Local(t.CreatedAt)); Row("Status", t.Status); Row("Type", t.Type); Row("Priority", t.Priority); Row("Category", t.Category);
            Row("Location", t.Location); Row("Technician", Tech(t.TechnicianId)); Row("Team", t.TeamName);
            Row("SLA", t.SlaId is { } sla ? d.SlaNames.GetValueOrDefault(sla, "") : ""); Row("Due", t.DueDate is { } due ? Local(due) : "");
            Row("Closed", TicketReports.ClosedTime(t) is { } closed ? Local(closed) : "");
            foreach (var (field, value) in d.TicketFields.GetValueOrDefault(t.Number, [])) Row(field, value);
            html.Append("</table>");
            if (!string.IsNullOrWhiteSpace(t.Description)) html.Append($"<div class=\"entry\">{E(t.Description)}</div>");
            foreach (var c in t.Comments)
                html.Append($"<div class=\"entry{(c.IsInternal ? " internal" : "")}\">{(c.IsInternal ? "<span class=\"tag\">INTERNAL</span> " : "")}<span class=\"muted\">{E(Local(c.CreatedAt))} - {E(Actor.Label(c.By))}{(c.FromRequester ? " (from them, in the staff portal)" : "")}</span>\n{E(c.Text)}</div>");
            var mine = files.Where(x => x.Item.Attachment.TicketNumber == t.Number).ToList();
            if (mine.Count > 0)
            {
                html.Append("<p><strong>Files</strong></p><ul>");
                foreach (var (item, path) in mine)
                    html.Append($"<li>{(path is null ? E(item.Attachment.FileName) + " <span class=\"muted\">(missing from storage)</span>" : $"<a href=\"{E(path)}\">{E(item.Attachment.FileName)}</a>")} <span class=\"muted\">- {E(Local(item.Attachment.UploadedAt))}{(item.Attachment.FromRequester ? ", sent by them" : "")}</span></li>");
                html.Append("</ul>");
            }
            if (t.History.Count > 0)
            {
                html.Append("<details><summary>History</summary><table>");
                foreach (var h in t.History) html.Append($"<tr><th>{E(Local(h.CreatedAt))}<br><span class=\"muted\">{E(Actor.Label(h.By))}</span></th><td><strong>{E(h.Action)}</strong><br>{E(h.Details)}</td></tr>");
                html.Append("</table></details>");
            }
        }

        html.Append($"<h2>Equipment ({d.Assets.Count} asset(s), {d.KitLoans.Count} kit loan(s))</h2>");
        if (d.Assets.Count == 0 && d.KitLoans.Count == 0) html.Append("<p class=\"muted\">None.</p>");
        if (d.Assets.Count > 0)
        {
            html.Append("<table><tr><th>Asset</th><td><strong>Periods they held it</strong></td></tr>");
            foreach (var a in d.Assets)
            {
                var periods = string.Join("; ", a.Periods.Select(x => $"{(x.StartedAt is { } s ? Local(s) : "before records began")} to {(x.EndedAt is { } e ? Local(e) : "now")}{(x.DueBack is { } due ? $" (loan, due back {due:d MMM yyyy})" : "")}{(x.Reason is { Length: > 0 } r ? $" - {r}" : "")}"));
                html.Append($"<tr><th>{E(a.Asset.AssetTag)}<br><span class=\"muted\">{E($"{a.Asset.Make} {a.Asset.Model} ({a.Asset.Type})")}</span></th><td>{E(a.HeldNow ? "Holds it now. " : "")}{E(periods)}</td></tr>");
            }
            html.Append("</table>");
        }
        if (d.KitLoans.Count > 0)
        {
            html.Append("<table><tr><th>Loan kit</th><td><strong>Loan</strong></td></tr>");
            foreach (var (loan, kit) in d.KitLoans)
                html.Append($"<tr><th>{E(kit)}</th><td>{E($"Issued {Local(loan.IssuedAt)} by {loan.IssuedBy}, due back {loan.DueBack:d MMM yyyy}, {(loan.ReturnedAt is { } r ? $"returned {Local(r)}" : "still out")}. Reason: {loan.Reason}.{(loan.Notes.Length > 0 ? $" Notes: {loan.Notes}" : "")}")}</td></tr>");
            html.Append("</table>");
        }

        html.Append($"<h2>Projects they asked for ({d.Projects.Count})</h2>");
        if (d.Projects.Count == 0) html.Append("<p class=\"muted\">None.</p>");
        foreach (var pr in d.Projects)
        {
            html.Append($"<h3>{E(pr.Reference)} {E(pr.Title)}</h3><table>");
            Row("Asked for", Local(pr.CreatedAt)); Row("Status", pr.Outcome is { } o ? $"{pr.Status} - {o}" : pr.Status); Row("Needed by", pr.DueDate.ToString("d MMM yyyy", CultureInfo.CurrentCulture));
            Row("Technician", Tech(pr.TechnicianId)); Row("What they asked for", pr.ItemsWanted);
            html.Append("</table>");
            foreach (var n in pr.Notes)
                html.Append($"<div class=\"entry{(n.IsInternal ? " internal" : "")}\">{(n.IsInternal ? "<span class=\"tag\">INTERNAL</span> " : "")}<span class=\"muted\">{E(Local(n.CreatedAt))} - {E(Actor.Label(n.By))}</span>\n{E(n.Text)}</div>");
        }

        html.Append($"<h2>Mentioned in other tickets ({d.Mentions.Count})</h2>");
        html.Append("<p class=\"muted\">Tickets someone else raised whose title, description or comments contain their full name or email. Listed so they can be reviewed; the content is not copied here.</p>");
        if (d.Mentions.Count > 0)
        {
            html.Append("<table>");
            foreach (var m in d.Mentions) html.Append($"<tr><th>#{m.Ticket.Number}</th><td>{E(m.Ticket.Title)} <span class=\"muted\">- in the {E(m.Where)}, raised {E(Local(m.Ticket.CreatedAt))}</span></td></tr>");
            html.Append("</table>");
        }

        html.Append($"<h2>Audit log ({d.AuditEntries.Count})</h2>");
        html.Append("<p class=\"muted\">Changes to their record, what they did in the staff portal, and sign-in events for their email.</p>");
        if (d.AuditEntries.Count > 0)
        {
            html.Append("<table>");
            foreach (var a in d.AuditEntries) html.Append($"<tr><th>{E(Local(a.At))}<br><span class=\"muted\">{E(Actor.Label(a.By))}</span></th><td><strong>{E(a.Action)}</strong> - {E(a.Entity)}<br>{E(a.Details)}</td></tr>");
            html.Append("</table>");
        }
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string Json(HelpdeskStore.SubjectAccessData d, IReadOnlyList<(HelpdeskStore.SubjectAccessAttachment Item, string? ZipPath)> files)
    {
        object ActorJson(Actor? a) => a is { } x ? new { name = x.Name } : null!;
        var p = d.Person;
        var document = new
        {
            generatedAt = d.GeneratedAt,
            generatedBy = d.GeneratedBy.Name,
            person = new
            {
                p.Id, p.Name, p.Email, p.Department, p.Location, active = p.IsActive, leftAt = p.LeftAt,
                portalPasswordSet = p.PasswordHash is not null, canRaiseProjects = p.CanRaiseProjects, isProjectLead = p.IsProjectLead,
                type = AccessRules.PersonType(p), startDate = p.StartDate
            },
            access = d.Access.Select(a => new
            {
                system = a.Resource, a.Grant.AccessLevel, account = a.Grant.Identifier, a.Grant.Privileged, mfa = a.Grant.Mfa, a.Grant.GrantedOn, a.Grant.GrantedBy,
                a.Grant.ApprovedBy, a.Grant.LastReviewedOn, removedOn = a.Grant.RevokedOn, removedReason = a.Grant.RevokeReason, a.Grant.Notes
            }),
            tickets = d.Tickets.Select(t => new
            {
                t.Number, t.Title, t.Description, t.Type, t.Status, t.Priority, t.Category, t.Location, t.CreatedAt, closedAt = TicketReports.ClosedTime(t),
                t.DueDate, technician = t.TechnicianId is { } tech ? d.TechnicianNames.GetValueOrDefault(tech) : null, team = t.TeamName,
                fields = d.TicketFields.GetValueOrDefault(t.Number, []).Select(f => new { field = f.Field, value = f.Value }),
                comments = t.Comments.Select(c => new { c.CreatedAt, by = ActorJson(c.By), internalNote = c.IsInternal, fromThem = c.FromRequester, c.Text }),
                history = t.History.Select(h => new { h.CreatedAt, by = ActorJson(h.By), h.Action, h.Details }),
                files = files.Where(f => f.Item.Attachment.TicketNumber == t.Number)
                    .Select(f => new { f.Item.Attachment.FileName, f.Item.Attachment.UploadedAt, sentByThem = f.Item.Attachment.FromRequester, f.Item.Attachment.Size, inExport = f.ZipPath })
            }),
            assets = d.Assets.Select(a => new
            {
                a.Asset.AssetTag, a.Asset.Make, a.Asset.Model, a.Asset.Type, a.Asset.SerialNumber, heldNow = a.HeldNow,
                periods = a.Periods.Select(x => new { from = x.StartedAt, to = x.EndedAt, dueBack = x.DueBack, reason = x.Reason })
            }),
            kitLoans = d.KitLoans.Select(k => new { kit = k.KitName, k.Loan.IssuedAt, k.Loan.IssuedBy, k.Loan.DueBack, k.Loan.ReturnedAt, k.Loan.Reason, k.Loan.Notes }),
            projects = d.Projects.Select(pr => new
            {
                pr.Reference, pr.Title, pr.CreatedAt, pr.Status, pr.Outcome, neededBy = pr.DueDate, pr.ItemsWanted,
                technician = pr.TechnicianId is { } tech ? d.TechnicianNames.GetValueOrDefault(tech) : null,
                notes = pr.Notes.Select(n => new { n.CreatedAt, by = ActorJson(n.By), internalNote = n.IsInternal, n.Text }),
                history = pr.History.Select(h => new { h.CreatedAt, by = ActorJson(h.By), h.Action, h.Details })
            }),
            mentionedIn = d.Mentions.Select(m => new { ticket = m.Ticket.Number, m.Ticket.Title, where = m.Where }),
            auditLog = d.AuditEntries.Select(a => new { a.At, by = ActorJson(a.By), a.Area, a.Entity, a.Action, a.Details })
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return slug.Length == 0 ? "person" : slug.Length > 40 ? slug[..40] : slug;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\']).ToHashSet();
        var safe = new string(Path.GetFileName(name).Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "file" : safe.Length > 100 ? safe[..100] : safe;
    }
}
