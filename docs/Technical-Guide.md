# EduHelpdesk — Technician and Administrator Guide

For IT technicians, system administrators and anyone else who runs or maintains EduHelpdesk.
If you are a teacher or member of support staff who just wants to report a problem, read the **Staff Guide** instead.

---

## 1. What EduHelpdesk is

EduHelpdesk is a self-hosted helpdesk for school IT teams. It combines three things that are usually separate products:

- **Tickets** — logging, triaging and resolving jobs, with SLAs, templates and reporting.
- **Assets** — a register of school equipment, with warranty and replacement planning, loans and full ticket history per device.
- **Parts** — a consumable stock inventory that tickets draw from, with reorder thresholds and suppliers.

Everything is linked: a ticket can name the assets it concerns and the parts consumed fixing it, so a device's whole repair history and cost-in-parts stays in one place.

### At a glance

| | |
|---|---|
| Platform | ASP.NET Core Razor Pages, .NET 10 |
| Database | SQLite, single file at `App_Data/helpdesk.db` |
| Dependencies | `Microsoft.Data.Sqlite`, `DocumentFormat.OpenXml` (Word print templates) |
| Authentication | Cookie-based, custom roles and permissions |
| Hosting | Self-hosted on the school network; no cloud service or external API required |

### How the data is stored

The whole dataset is held in memory and written to SQLite on every change. This keeps the code simple and reads instant, at the cost of rewriting the tables on each save. It has been measured at roughly 2,000 assets and 2,000 tickets with comfortable performance (list views 8–110 ms, bulk operations well under a second). For a single school that is ample headroom.

Files that matter, all under `App_Data/`:

| Path | What it holds |
|---|---|
| `helpdesk.db` | Everything: tickets, assets, parts, people, settings, audit log |
| `attachments/` | Ticket attachments, stored by GUID with no file extension |
| `backups/` | Copies written before a factory reset |
| `print-template.docx` | Optional Word template for printing tickets |

---

## 2. Installing and running

From the project folder:

```bash
dotnet run
```

The URL is set in `Properties/launchSettings.json`. Out of the box it is `http://localhost:5277`; change `applicationUrl` to the machine's LAN address (for example `http://10.64.18.13:5277`) to let other people on the school network reach it.

The database is created automatically on first run. No migration step is needed — the schema updates itself on startup, and older databases are upgraded in place.

### Hosting notes

- The app must be reachable by staff for the portal to be useful, so it needs to run on a machine that stays on.
- Run it as a Windows service or scheduled task so it survives a reboot.
- Everything is stored under `App_Data/`; back that folder up (see section 15).

---

## 3. First run

On first start the system creates:

- A protected **Administrator** role with every permission.
- Three ordinary roles: Senior Technician, Technician, Junior Technician.
- A bootstrap administrator account.
- A small demo dataset (one technician, one requester, one laptop) so the screens are not empty.

**Sign in with:**

- Email: `admin@eduhelpdesk.local`
- Password: `ChangeMe123!`

You will be forced to change the password immediately. Do it properly — this account has full access to everything.

After signing in, work through:

1. **Settings → Branding** — set the school name and colours.
2. **Settings → Directory and asset options** — set up Teams, Departments and Locations before importing people.
3. **People** — add technician accounts and import your staff list.
4. **Settings → Ticket options** — adjust Categories, Priorities, Statuses and add SLAs.
5. Delete the demo records (Alex Morgan, Jordan Lee, asset LT-1001) once you have real data in.

---

## 4. Accounts, roles and permissions

There are two completely separate kinds of login:

| | Technician login | Staff portal login |
|---|---|---|
| URL | `/Login` | `/Portal` |
| Who | IT staff | Teachers and support staff |
| Managed at | People → Technicians | People → Users |
| Has roles/permissions | Yes | No |
| Can see | Everything their role allows | Only their own tickets |

A member of staff who is also a technician needs both records if they want to use both sides.

### Sessions

Sessions last **8 hours of inactivity**, with a hard cap of **14 days** regardless of use. The cookie is `EduHelpdeskAuth`, HTTP-only, SameSite=Lax.

### Roles

Roles are fully custom — create as many as you like from **People → Roles**. Each is a name plus nine permission toggles:

| Permission | Allows |
|---|---|
| Settings | Branding, option lists, CSV import, audit log, factory reset |
| Manage roles | Create, edit and delete role definitions |
| Manage staff accounts | Add/edit technician accounts, assign roles, reset passwords |
| Manage requesters | Add/edit the requester directory on the People page |
| Manage assets | Add/edit/delete assets, loan and return |
| Manage suppliers | Add/edit suppliers |
| Manage parts | Add/edit parts |
| Delete/merge tickets | The two destructive ticket actions |
| Change "Working as" | Pick who "Working as" resolves to, instead of always being yourself |

**Everyone signed in can already do normal ticket work** — comment, assign, change status/priority/category, add attachments, close — regardless of role. Only the two destructive ticket actions are permission-gated.

**Administrator** is the one hardcoded role. It always has every permission and cannot be edited, renamed or deleted, so there is always a way back into the system. A role cannot be deleted while a technician still holds it.

### Creating accounts

- **Technicians**: People → Add technician. Requires "manage staff accounts". A new account has no password until someone sets one here; the same form resets passwords.
- **Requesters (staff)**: People → Add user. Requires "manage requesters". They cannot sign in to the portal until a password is set.

There is no self-service password reset. All password changes go through a technician, or the user's own **Change password** link once signed in.

---

## 5. Tickets

**Tickets** in the top nav (`/Jobs`) is the main working list.

### Queues

Five tabs across the top:

| Queue | Shows |
|---|---|
| Open | Everything not Closed (the default view) |
| My tickets | Assigned to whoever "Working as" resolves to |
| Unassigned | No technician assigned |
| Overdue / due soon | Past due, or due within the configured window |
| All tickets | Everything, including closed |

**"Working as"** sets which technician you are logging work as. It defaults to your own account; only a role with the "Change Working as" permission can set it to someone else.

The "due soon" window is configured in **Settings → Ticket queues** (default 24 hours).

### Finding tickets

- **Search** covers title, description, comments, requester, asset tag and ticket number.
- **Filters**: status, type, priority and category (multi-select checkboxes); technician (including Me and Unassigned), team, requester, department and location (dropdowns).
- **Sort** by clicking any column header; click again to reverse.
- **Paging** at 25/50/100/200 per page.
- **Asset filter**: reached by clicking the ticket count on the Assets list or from an asset's page; shows as a removable chip.
- **Export to CSV** via the bulk actions bar.

### Bulk actions

Tick rows (or use "select all N matching the filters", which re-applies your filters server-side so it spans every page), then choose an action:

- Change status, technician, team, priority, category or type
- Add one comment to all of them
- Close with a message
- Merge
- Export to CSV

Assigning a technician skips any ticket whose team that technician is not in.

### The ticket page

The sidebar holds editable fields — technician, team, SLA, due date, status, priority, type, category, location. Every change is recorded in the ticket's own History.

Tabs across the middle:

| Tab | Contents |
|---|---|
| Comments | Public comments and internal notes |
| History | Every field change, newest last |
| Assets | Devices this ticket concerns |
| Parts | Stock consumed fixing it |
| Attachments | Uploaded files |

**Internal notes** are ticked on the comment form. They are visible to technicians only — never to the requester in the portal, and left off printed tickets.

**Attachments**: 10 MB per file, 5 per upload. Allowed types are a whitelist — pictures, PDF, Office documents, text and email files. Archives, SVG and HTML are blocked deliberately. Files are served with `nosniff` and a sandbox policy; only pictures display inline.

**Links**: tickets can be marked *related* to each other, or a *follow-up* can be created — which copies the requester, assets, category, priority, technician and team into a new open ticket.

**Actions**: Mark as closed, Merge ticket, Delete ticket (both destructive ones need the permission), and three print options.

### Ticket types

Two fixed values, **Incident** (something is broken) and **Request** (something is being asked for). They exist only to separate the two in lists and reports — they do not have different categories, SLAs or statuses.

### Templates

**Settings → Ticket templates** holds saved starting points: a name, type, title, description, category, priority, optional SLA and default answers for custom attributes. Technicians pick one when logging a ticket and everything stays editable afterwards.

If a template's category or priority is later deleted it shows "(no longer exists)" and must be re-chosen before the template can be saved again.

### SLAs and due dates

SLAs are defined in **Settings → SLAs**: a name, a target duration, and optional scoping to particular priorities and categories. Leaving a scope empty means "applies to all". The matching SLA sets a ticket's due date automatically; a technician can override the due date or the SLA on an individual ticket.

### Closing requirements

**Settings → Closing requirements** lets you force a closing message before a ticket can be marked closed, per priority and per category. Everything else closes in one click.

---

## 6. Assets

**Assets** in the top nav is the equipment register.

### What an asset holds

Identity (asset tag, make, model, type, serial number), location, assigned user, supplier, status, and the lifecycle fields: purchase date, purchase price, purchase order, warranty end, replacement date and loan due date. Plus custom attributes, comments, full history and ownership history.

**Duplicate handling**: a duplicate asset tag is blocked outright; a duplicate serial number is allowed but warns.

### Statuses

Editable in **Settings → Asset statuses**. Defaults: In use, In stock or spare, In repair, Lost or stolen.

### Replacement planning

Set an expected lifespan in years per asset type (**Settings → Asset types**). An asset's replacement date is then its purchase date plus that lifespan — unless a date is typed directly on the asset, which always wins.

### Loans

**Loan out** records who holds a device and when it is due back; **Return** ends that period and optionally sets a new status. Every holder period is kept in the asset's ownership history with names and dates. Overdue loans always appear on the review list.

### The review list

The Overview page and the asset reports both show **Assets to review** — anything whose warranty ends or replacement falls due within the review window (or has already passed), plus overdue loans. The window is set in **Settings → Asset review** (default 60 days).

### List and bulk actions

Search, filters (status, type, make, location, held by, and a Show filter for needs-review / on-loan / loan-overdue), sortable columns with natural ordering (LT-2 before LT-10), and paging. Bulk actions change status, owner or location, or export to CSV — applied to ticked rows or to every row matching the current filters.

Reassigning an owner in bulk clears any loan due date, because handing a device to someone else is a new assignment rather than a continuation of the old loan.

### Importing assets

**Assets → Import from CSV** handles bulk loading and updating:

1. Upload the file.
2. Map columns — guessed from your headings, including custom attributes.
3. Review a preview of exactly what would change.
4. Apply.

Rows are matched **by asset tag**: an existing tag updates that asset, any other tag adds a new one.

Rules worth knowing:

- Blank cells are ignored unless you tick "blank clears"; tags, types, models and statuses are never cleared.
- Missing makes, models, types, locations, statuses and suppliers are created automatically (shown in the preview, and can be turned off).
- Unknown users are never created — those rows are skipped.
- New assets need a type and a model; you can set defaults for both.
- A tag matching two existing assets is skipped. A tag repeated within the file uses the first row only.
- The file is never used to delete assets.
- An exported file re-imports cleanly as "up to date".

**Known limitation**: older Windows-encoded files are read as Latin-1, so symbols such as € in text fields can come out wrong. Save as UTF-8 to avoid it. Prices are unaffected.

---

## 7. Parts

**Parts** in the top nav is the consumable stock inventory — toner, cables, batteries, chargers.

### What a part holds

Name (the only required field), SKU, category, location/bin, quantity on hand, reorder threshold, suppliers (optional, several allowed) and compatible asset types (optional).

Categories and locations are managed lists, set up in **Settings → Parts inventory → Part categories / Part locations**. Part locations are deliberately separate from the building-level Locations list used by people and assets, because they describe shelves and cupboards rather than rooms.

A duplicate SKU is allowed but warns.

### Low stock

A part is **low** when its quantity on hand is at or below its reorder threshold. Each part can set its own threshold; anything left blank uses the default in **Settings → Parts inventory** (default 5).

Low parts surface in three places, all using the same rule:

- A warning badge on the Parts list, and a "Low stock only" filter.
- The **Parts to review** panel on the Overview page.
- The **Parts reports** page.

### Adjusting stock

Once a part exists, its quantity is **read-only on the edit form**. Changes go through the **Adjust stock** action at the bottom of the page, which requires a reason ("Restocked from supplier", "Stocktake correction") and records it in the part's own stock history with a timestamp.

This is deliberate: editing the number directly left no record of why it changed. New parts still take an opening quantity on the add form.

Stock also moves automatically when parts are assigned to tickets — consumed on assignment, returned if the ticket is deleted, moved across on a merge. Those movements are recorded in the ticket's history.

### List, bulk actions and export

Search (name, SKU, category, location, supplier), filters (category, location, supplier, low-stock-only), sortable columns, paging. Bulk actions change category or location, delete, or export to CSV. A part still assigned to a ticket cannot be deleted and is skipped with a count.

### Suppliers

**Suppliers** in the top nav is a simple directory — name, contact, email, phone, address, website, notes. A supplier that is linked to any asset or part cannot be deleted.

---

## 8. Reports

Three report pages, switchable via the tab strip at the top of each:

### Asset reports (`/Reports`)

Review list, counts by type/make/location/status, fleet age and refresh planning, warranty expiry (with a configurable window), and problem devices by ticket count over a chosen period.

### Ticket reports (`/Reports/Tickets`)

Filterable by period (30 days, 3 months, 6, 12, all time) and type (both, incidents only, requests only).

- **SLA performance** — on time, late, open and overdue, broken down by type, priority, category and technician, worst first, plus the longest-overdue open tickets. The on-time rate leaves out tickets that are open but not yet due, and those with no due date.
- **Technician workload** — open now split by incident and request, overdue, oldest open, closed in period, average resolution, and per-team figures.
- **Repeat problems** — devices with the same category of ticket more than once, assets and requesters with the most tickets, and category counts against the previous period.

### Parts reports (`/Reports/Parts`)

Every part at or below its reorder threshold, with quantity against threshold, plus a count of what is completely out of stock.

---

## 9. Settings reference

| Area | What it covers |
|---|---|
| Branding | School name, dashboard wording, colours, dark mode |
| Directory and asset options | Teams, Departments, Locations, Asset types (and lifespans), Makes, Models, Asset statuses, Custom asset attributes |
| Asset review | How many days ahead the review list looks (default 60) |
| Ticket queues | The "due soon" window in hours (default 24) |
| Parts inventory | Default reorder threshold, Part categories, Part locations |
| Ticket options | Categories, Statuses, Priorities, Custom ticket attributes, SLAs, Ticket templates |
| System audit | The filterable log of every change |
| Closing requirements | Which priorities/categories need a closing message |
| Ticket print template | Upload a Word .docx template |
| Imports | Users, technicians and option lists by CSV |
| Reset to factory settings | Wipe everything back to a new install |

### Managed lists behave consistently

Across every option list — teams, departments, locations, asset types, categories, part locations and the rest — renaming a value **cascades** to every record using it, and a value still in use **cannot be deleted**. You will get told what is blocking it.

### Custom attributes

Both assets and tickets support custom attributes: a name, a field type (single line, multi-line, choice list), and an optional scope. An asset attribute can apply to particular asset types; a ticket attribute to particular categories. Leaving the scope empty means it applies to everything.

---

## 10. The staff portal

`/Portal` is the staff-facing side. It is reachable **without any login at all** by design — anyone on the network gets the sign-in page — and the root URL `/` sends signed-out visitors there. Technicians reach their own login from the "Login as Technician" link at the bottom.

Once signed in with their email and password, staff can:

- Report a problem — title, optional description, category, location.
- See the tickets they have submitted, with current status.
- Open one and add a follow-up message.

They cannot see internal notes, anyone else's tickets, or any of the technician-side pickers (requester, technician, team, SLA, asset). It is deliberately minimal.

**To give a member of staff access**: People → Add user, fill in their details and set a password. Until a password is set they cannot sign in. Passwords are reset the same way.

---

## 11. CSV import formats

From **Settings → Imports**. Each has a downloadable template.

**Users** — header row required:

```
Name,Email,Department,Location
Jane Doe,jane.doe@example.com,IT,Main Building
```

**Technicians** — role is optional and defaults to Technician; it must match an existing role name:

```
Name,Email,Team,Role
John Smith,john.smith@example.com,IT Support,Technician
```

Imported technician accounts have no password until someone sets one.

**Option lists** (asset types, makes, models, categories) — a single `Name` column, except asset models which take an optional second `Make` column:

```
Name,Make
Latitude 5440,Dell
```

Blank rows and values that already exist are skipped. Rows with a missing name or an invalid email are skipped and counted in the result message.

Assets have their own richer importer with column mapping — see section 6. **There is no CSV import for tickets or parts**, by design.

---

## 12. Printing

Three options on the ticket page:

- **Print ticket** — the full ticket. Uses the uploaded Word template if there is one, otherwise a standard layout. Public comments are included; internal notes never are.
- **Print label** — a small label, sized for a receipt printer (10 mm margins).
- **Print job sheet** — a worksheet to go with the device.

### Word templates

Upload a `.docx` at **Settings → Ticket print template**. Placeholders are replaced with the ticket's details:

`{{Job.Number}}`, `{{Job.Title}}`, `{{Job.Status}}`, `{{Job.Comments}}` (public comments only), `{{Requester.Name}}`, `{{Technician.Name}}`, `{{Asset.Tag}}`

---

## 13. The audit log

**Settings → System audit** records every change made in the system: tickets, assets, parts, people, suppliers, lists, SLAs, custom attributes and settings. Filter by area, action, date or free text.

It works by snapshotting the data before and after each save and recording the differences, so any new action is audited automatically without having to log itself. Ticket and asset history and comments, and parts stock adjustments, are folded into the same view.

**Important limitation**: the log records *what* changed and *when*, but **not who did it**. No part of the system attributes changes to a user — this predates the addition of logins and has not been retrofitted. Treat the audit log as a change history, not an accountability trail.

---

## 14. Factory reset

**Settings → Reset to factory settings** erases everything and returns the system to a new install: all tickets, assets, people, suppliers, parts, SLAs, custom attributes, every list you added to, the print template, attachments, and branding. Only the small demo set a new install ships with remains.

You must type `DELETE` in capitals to confirm. By default it:

- Saves a backup to `App_Data/backups` first (including attachments).
- Keeps the audit log, and records the reset itself as an entry.

Both of those can be turned off with the tickboxes. The database is compacted afterwards so deleted rows do not linger in the file.

**To restore a backup**: stop the app, copy the backup over `App_Data/helpdesk.db`, and copy the attachments folder back as `App_Data/attachments`.

---

## 15. Backups

There is **no automatic backup schedule**. The only automatic backup happens before a factory reset. Set one up yourself — it is the single biggest operational risk in the system.

A safe approach:

1. Stop the app (or accept a copy taken while running — SQLite tolerates this better than most, but a clean copy is safer).
2. Copy the whole `App_Data/` folder somewhere off the machine.
3. Keep dated copies with a sensible retention.

Test a restore at least once, so you know the process works before you need it.

---

## 16. Known limitations

Things the system deliberately or currently does not do. Worth knowing before someone asks:

| Limitation | Notes |
|---|---|
| No email at all | No notifications, no email-to-ticket, no outbound replies. Requesters must check the portal. |
| No in-app notifications | Nothing tells a requester their ticket changed. |
| No "who did it" | History and audit record what and when, never who. |
| No login rate limiting or lockout | Password guessing is unthrottled. The portal is reachable by anyone on the network. |
| No self-service password reset | All resets go through a technician. |
| No ticket or parts CSV import | Assets only. |
| No report exports | Reports are on-screen only. |
| No time tracking | Not built, by choice. |
| No approvals or change management | Requests do not route for sign-off. |
| No asset discovery or agents | Nothing scans the network; the register is what you put in it. |
| No barcode or QR scanning | Labels print, but carry no scannable code. |
| No asset stocktake workflow | Parts have stock adjustments; assets do not. |
| Single school | Locations exist, but there is no multi-site tenancy or scoping. |

---

## 17. Troubleshooting

**"That team/location/category cannot be deleted because it is in use."**
Something still references it. Move those records onto a different value first, then delete.

**A technician cannot be assigned to a ticket in bulk.**
Bulk assignment skips tickets whose team that technician is not in. Change the ticket's team, or add the technician to it.

**An import says rows were skipped.**
Check the message — the usual causes are a missing name, an invalid email, an email already in use, or an unknown user referenced by an asset row.

**Symbols come out wrong after an asset import.**
The file was Windows-encoded. Re-save it as UTF-8 and import again.

**A part will not delete.**
It is assigned to a ticket. Remove it from the ticket first, or use bulk delete, which skips and counts those.

**Nobody can sign in / the Administrator password is lost.**
The system guarantees an Administrator account exists, but it cannot be recovered from the UI. Restore from a backup, or — as a last resort on a test copy — clear the `PasswordHash` for that technician row directly in SQLite and sign in with the bootstrap credentials.

**The app will not start because the database is locked.**
Another copy is already running. Only one process can hold `helpdesk.db`.

---

## 18. Where things live in the code

For anyone maintaining it:

| Area | Files |
|---|---|
| Data model | `Models/HelpdeskModels.cs` |
| Storage, schema, every mutation | `Services/HelpdeskStore*.cs` |
| Audit diffing | `Services/AuditTracker.cs` |
| List search/filter/sort engines | `Services/AssetListQuery.cs`, `TicketListQuery.cs`, `PartListQuery.cs` |
| CSV export | `Services/AssetCsv.cs`, `TicketCsv.cs`, `PartCsv.cs` |
| Asset import | `Services/HelpdeskStore.AssetImport.cs`, `AssetImportTargets.cs`, `CsvReader.cs` |
| Calculated insights | `Services/AssetInsights.cs`, `PartInsights.cs`, `TicketReports.cs` |
| Auth and permissions | `Program.cs`, `Services/PermissionAuthorizationHandler.cs`, `PasswordHasher.cs`, `PortalIdentity.cs` |

Two conventions to preserve when changing anything:

1. **Schema changes** go in `EnsureSchema` as `CREATE TABLE IF NOT EXISTS` or a try/catch `ALTER TABLE`, so existing databases upgrade themselves on startup.
2. **Multi-value fields** (a list on a record) use a join table with `ON DELETE CASCADE`, an explicit `DELETE FROM` before the parent in `WriteData`, and must be sourced from the stored record in page handlers — never from the posted form model, which will bind them empty and silently wipe them.
