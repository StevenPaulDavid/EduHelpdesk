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

The whole dataset is held in memory and written to SQLite on every change. This keeps the code simple and reads instant, at the cost of rewriting the tables on each save - and while a save runs, every other change waits for it. Measured on generated data (September 2026):

| Data | Database | Start-up | Pages | Each save | Memory |
|---|---|---|---|---|---|
| One year at a large secondary school: 1,500 staff, 3,000 assets, 8,000 tickets, 40,000 audit lines | 16 MB | 3 s | 5–65 ms | about 0.6 s | 390 MB |
| Five years of the same, nothing deleted: 6,000 assets, 40,000 tickets with 280,000 comments and history lines, 200,000 audit lines | 71 MB | 8 s | 15–230 ms | about 3 s | 900 MB |

A save costs time in proportion to everything held, so the five-year row is where it starts to be felt: three seconds after every comment, with the rest of the helpdesk waiting. The data retention rules (section 16) keep a school near the one-year row - deleting closed tickets after two or three years, say. If a school ever needs to keep much more, the fix is to write only the rows a change touched rather than every table.

Files that matter, all in the data folder - `App_Data/` unless `EduHelpdesk:DataPath` says otherwise (see section 15):

| Path | What it holds |
|---|---|
| `helpdesk.db` | Everything: tickets, assets, parts, people, settings, audit log |
| `attachments/` | Ticket attachments, stored by GUID with no file extension |
| `keys/` | The keys that sign-in cookies are encrypted with. Lose them and everyone signs in again; nothing else is lost |
| `backups/` | The nightly backup zips, unless the backup folder has been pointed elsewhere |
| `logs/` | The error log: warnings and errors, one file a day, kept for 30 days. Read it in **Settings → Error log** |
| `print-template.docx` | Optional Word template for printing tickets |

---

## 2. Installing and running

### For real use: the installer

The helpdesk people use every day runs as a Windows service, installed from a release zip: unzip it and double-click `Install.cmd`. It asks where to keep the data, the school's name, colours and logo, where backups go, HTTPS and the port, then installs the program (with its own .NET runtime - nothing to download), registers the **EduHelpdesk** service, opens the firewall and starts it. Running it again from a newer zip upgrades in place. **`docs/Install-Guide.md`** (`INSTALL.pdf` in the zip) is the full guide for schools, and **`docs/Release-Guide.md`** explains how to make the zip (`deploy/New-Release.ps1`) and how to move an existing `dotnet run` install onto the service.

### For development

From the project folder:

```bash
dotnet run
```

The URL is set in `Properties/launchSettings.json`. `dotnet run` uses Development mode, which shows the full developer error page - only to requests from the machine itself; anyone else gets the ordinary error page. It isn't meant for the helpdesk people use every day.

The database is created automatically on first run. No migration step is needed - the schema updates itself on startup, and older databases are upgraded in place.
### Hosting notes

- The app must be reachable by staff for the portal to be useful, so it needs to run on a machine that stays on.
- Everything is stored in the data folder, which is backed up nightly (see section 15). Keep the data folder out of OneDrive, Dropbox and similar - a sync client can corrupt a live database. Settings warns if it is inside one.
- **HTTPS.** Out of the box the site is plain `http://`, so passwords cross the network unencrypted from any other computer; Settings → Sign-in security says so. Once the site has a certificate (in Kestrel, IIS or a reverse proxy), add `"EduHelpdesk": { "RequireHttps": true }` to `appsettings.json` (the install script does this for you with `-CertificateSubject`). Plain-HTTP requests are then redirected, browsers are told to stay on HTTPS (HSTS, which browsers never apply to `localhost`), and every cookie is marked Secure. Don't turn it on before the certificate works, or nobody can reach the site.
- **Behind a proxy** that handles HTTPS itself (Azure App Service, IIS ARR, nginx), set the environment variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, so the app sees each visitor's real address and scheme. The sign-in lockout counts failures per address, and without this every visitor looks like the proxy.
- Every page is sent with a content security policy (only the site's own script files, styles and images; no inline script, so an injected `<script>` or `onclick=` will not run; no framing by other sites), `nosniff`, a same-origin referrer policy and `no-store` caching - see `Services/SecurityHeaders.cs`.

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

1. **Settings → Branding & logo** — set the school name, colours, logo and default appearance.
2. **Settings → Teams, Departments and Locations** — set these up before importing people.
3. **People** — add technician accounts, and import your staff list from **Settings → Imports**.
4. **Settings → Categories, Priorities, Statuses and SLAs** — adjust them to how your team works.
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

**Helpdesk:** sessions last **8 hours of inactivity**, with a hard cap of **14 days** regardless of use. The cookie is `EduHelpdeskAuth`, HTTP-only, SameSite=Lax, and Secure when `RequireHttps` is on. Every request re-checks the account: deactivating a technician, resetting their password or changing their role takes effect on their next click.

**Staff portal:** the cookie is `portal_who`, encrypted. How long it lasts depends on the sign-in:

| Sign-in | Lasts |
|---|---|
| Password, "Keep me signed in" ticked | 30 days |
| Password, not ticked (the default) | Until the browser closes, 2 hours without use, or 12 hours in all |
| A technician's "Open staff portal" | Only while that technician is signed in to the helpdesk in the same browser |

A new password or deactivating the requester ends their portal sessions everywhere.

**Signing out ends the session on the server,** for the helpdesk and the portal alike. Each sign-in is recorded in the `Sessions` table and its cookie names it; signing out deletes the record, so a copy of the cookie taken before then - from a shared PC's browser, say - is refused. Sessions survive a restart. A password change keeps the browser that made it signed in and signs out every other one; a factory reset signs out everyone.

### Passwords and lockout

- **Rules**, wherever a password is chosen: at least 8 characters, not one of the passwords guessed first (`Password1!`, `Welcome2025`, `Teacher1`…), not made only of numbers, and not containing the person's name or email. There are no "must contain a symbol" rules.
- **Set by someone else means changed on first use.** A password a technician sets - for a colleague or for a requester's portal account - must be changed before anything else. This applies to every page, not just straight after signing in.
- **Lockout.** 5 wrong passwords for one email within 15 minutes locks that email for 15 minutes; 30 from one computer (IP address) blocks that computer. The helpdesk and portal forms count separately. The lock is on the email as typed, so it doesn't reveal whether the account exists. Setting a new password lifts it at once, and a restart clears all locks. Each lockout is written to the audit log (area **Sign-in**), and Settings → Sign-in security shows the count for the last 7 days.

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

- **Technicians**: People → Add technician. Requires "manage staff accounts".
- **Requesters (staff)**: People → Add user. Requires "manage requesters".

**Passwords for new accounts.** Leave the password box empty and the helpdesk makes up a **temporary password**: three words and a number, such as `Otter-Lantern-Maple-38`. The person must choose their own the first time they sign in. If you type a password instead, it is theirs to keep and they aren't asked to change it.

**Quick start guide.** Saving a new account opens a one-page guide to print and hand over. It has the address, their email, the password and the first things to know. Technicians get a guide to the helpdesk; requesters get one to the staff portal. Reprint it any time with **Print quick start guide** on the account's page. The password is only printed for an hour after it was set, while it is still their password: only a scrambled copy is kept, so after that the sheet has a line to write it on.

The address on the guide comes from **Settings → Sign-in security → Helpdesk address**. The installer fills this in. If it's empty, the guide uses the address it was printed from, with `localhost` swapped for the computer's name.

**Forgotten passwords.** There is no emailed reset link, because the helpdesk doesn't send email.

A **technician** can reset their own with a **recovery key**:
- They're given one when they first choose their own password: on a new account, after a temporary password, and for the built-in administrator on first sign-in. That's unless they already have one. The page shows it once, with the recovery file to download, before they carry on.
- They can make a new one at any time from **Recovery key** in their account menu, confirming their current password.
- The key is 24 characters, shown once, and saved as a small **recovery file** to download or print.
- On the sign-in page, **Forgotten your password?** takes their email and the key, typed or as the uploaded file, and sets a new password they keep.
- The key then stops working, every sign-in of the account ends, and they sign in with the new password. Two-step sign-in still applies.
- Only a SHA-256 of the key is stored.
- Wrong keys are throttled like wrong passwords: 5 for one email in 15 minutes lock that email's reset for 15 minutes.
- Making, replacing, using and removing keys are all in the audit log (area Sign-in).

On a technician's account page, **Recovery key** shows whether they have one. **Remove recovery key** cancels one that has been lost or seen by someone else. Only the holder can make a new one.

**Settings → Sign-in security → Recovery keys** turns the reset off for everyone. Keys already made are kept, but don't work until it's turned back on.

Otherwise, and always for portal users, someone resets it for them:
- **Give them a temporary password** on the account's page makes a new one, which must be changed at next sign-in, and opens the guide to print it on.
- Typing a new one under **Set a password** gives them a password they keep.

Anyone signed in can change their own: technicians from **Change password** in the account menu, portal users from the **Change password** link on the portal home page.

---

### Two-step sign-in

Staff accounts can use a code from an authenticator app (Microsoft Authenticator, Google Authenticator or any other that shows six-digit codes) as well as their password. The DfE's cyber security standard for schools expects this on accounts like these.

- **Setting it up:** account menu → **Two-step sign-in**. Scan the QR code (or type the key), enter the code the app shows, and keep the ten **recovery codes** it then shows once. Turning it on signs the account out everywhere else.
- **Signing in:** after the password, the helpdesk asks for the code (`/LoginCode`). A recovery code works instead, once each. Codes can't be reused, and 5 wrong codes lock the account for 15 minutes, as wrong passwords do.
- **Requiring it:** **Settings → Sign-in security** can require it of every staff account. Anyone without it is then sent to set it up before they can do anything else (after changing their password, if they must do that too). While it is required, nobody can turn theirs off.
- **Lost phone:** someone with Staff accounts: Edit opens the person's staff account and presses **Reset two-step sign-in**; they set it up again. Only an Administrator can reset an Administrator's, and nobody resets their own that way.
- The staff portal has its own passwords and doesn't use two-step sign-in.
- Every change - turned on, reset, required, recovery code used - is in the audit log under **Sign-in**. The app secrets are stored in the database like the rest of the data (so a restored backup still works on another machine); treat backups accordingly.
## 5. Tickets

**Tickets** in the top nav (`/Jobs`) is the main working list. The **Overview** (the home page) is the day's starting point: counts of your open tickets, overdue, unassigned and replies waiting (each opens its queue), then short lists of your tickets (most urgent first), overdue and due soon, unassigned, requesters waiting on a reply, your projects, and the asset and parts review lists. "Your" follows Working as, the same as the My tickets queue. Each panel only appears for a role allowed that module.

Ticket statuses are coloured by what they do rather than their name, here and in the staff portal: the first status in the list (where new tickets start), statuses that stop the SLA clock (waiting), Closed, and everything else (being worked on).

Long dropdowns - requesters, assets, borrowers - are search boxes: type any part of a name, email, department, asset tag, model or serial and pick from the matches. A box for several assets keeps the chosen ones as chips, each with a × to remove it.

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

The "due soon" window is configured in **Settings → Ticket queues & closing** (default 24 hours).

### Finding tickets

- **Search** covers title, description, comments, requester, asset tag and ticket number.
- **Filters**: status, type, priority and category (multi-select checkboxes); technician (including Me and Unassigned), team, requester, department and location (dropdowns). They sit in a **More filters** panel that starts folded, so the list begins near the top of the screen. Every filter in force is shown as a chip under the search box; the × on a chip takes just that one off, and **Clear all** takes them all off.
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

**Pausing the clock.** In **Settings → Statuses**, any status except Closed can be set to stop the SLA clock (On Hold does by default). Use it for tickets waiting on someone else.
- While a ticket is in that status, it shows **SLA paused** instead of overdue or due soon, and stays out of the Overdue / due soon queue and the reports' overdue counts.
- When it moves to another status, or is closed, a due date worked out from the SLA moves on by the time it was paused. That time is counted the SLA's own way: every minute for hours and days SLAs, school-day time for work days, lesson time for periods.
- A due date typed by hand is left as it is.
- A ticket that is already overdue can't be paused. The history says so, rather than letting On Hold hide a missed deadline.
- Every pause and restart is recorded in the ticket's history, and pauses survive a later priority, category or SLA change.

On the ticket page, the due date carries an **Overdue**, **Due in…** or **SLA paused** flag. **Back to tickets** returns to the list with the filters and sort you were last using.

### Closing requirements

**Settings → Ticket queues & closing** lets you force a closing message before a ticket can be marked closed, per priority and per category. Everything else closes in one click.

### Onboarding new staff

The **Onboarding** menu covers everything to get ready for a new member of staff: before they arrive, on their first day and in their first week.

**Starting one.** **＋ New onboarding** asks for:
- their name, job title, department, location and line manager
- their start date
- which checklist to follow

Their school email is optional, since creating it is often one of the tasks. Starting an onboarding:
- adds them to People, with no password, so they can't sign in to anything yet
- opens one ticket, category *Onboarding*, that carries the checklist

**Checklists** come from templates in **Settings → Onboarding checklists**, one per kind of new starter, such as Teacher or Support staff. New installs have an example Teacher template. Each task has:
- a stage: before arrival, first day or first week
- a number of days from the start date, which sets its due date
- an owner: IT, or the onboarding officer

Tasks can be added or removed for one person without changing the template. Moving the start date moves every due date with it.

**Tasks that do the job for you.** A task's **Does** setting can make ticking it do more than tick it:
- **Makes their staff portal account.**
  - Ticking it gives the new starter a temporary password (to change at first sign-in) and opens their quick start guide to print, password included.
  - It needs their school email in the details first.
  - Whoever can work the onboarding can print that guide from the task, without needing People permissions.
- **Issues a Laptop** (or any asset type).
  - The task shows the free devices of that type: not disposed, held by nobody, and not in a loan kit, with in-stock ones first.
  - Issuing one assigns it to the new starter as their own (not a loan) and links it to the onboarding ticket.
  - **Take it back** returns it to stock and makes the task to do again.
  - Issuing needs Assets: Edit or Onboarding: Edit.

The example Teacher template has both: *Build and set up laptop* issues a Laptop, and *Create their staff portal account* makes the account. Existing installs had them added to their Teacher template once.

**Welcome pack.** **Download welcome pack** on an onboarding makes one PDF to print or send. The first part covers:
- a welcome, and how to sign in to the staff portal
- how to get IT help
- the equipment issued to them, with asset tags and serial numbers
- your school IT information
- what else is in the pack
- a "received by" signature block, when they've been issued something

Then come the PDFs chosen for them, page by page, with page numbers running through the whole pack.

About the password:
- The temporary password is only included within an hour of their portal account being made, and only for someone who can work the onboarding.
- Otherwise it says the password is given separately, or that the account will be ready by their first day.

Set it up in **Settings → Onboarding checklists → Welcome pack**:
- Write the **school IT information**: Wi-Fi, printing, who to ask. A blank line starts a new paragraph.
- Upload **documents**, such as the acceptable use policy or staff handbook. They must be PDFs of up to 25 MB, each checked on upload: password-protected or damaged PDFs are refused.
- Tick which documents each template's pack includes. A new onboarding copies its template's choice, and **Documents in their pack** on the onboarding changes it for that person.

The documents are kept with the attachments, so they are in every backup. Deleting one takes it out of every template and pack.

**Working one.** The onboarding page shows the checklist, the new starter's details, and the ticket's notes and history. Ticking a task records who did it and when, in the ticket's history.
- IT tasks can be given to a named technician.
- The ticket's due date is the earliest task still to do, so overdue onboardings show in the ticket queues like any other ticket.
- When every task is ticked, the ticket closes itself. Unticking a task opens it again.
- If the person won't be starting, **Cancel** closes it without deleting anything, and it can be resumed.

**The Onboarding menu** lists every onboarding in tabs, with a search by name, email, job title, department or checklist:
- In progress
- Not started yet
- Overdue tasks
- IT tasks left
- Finished

**In the ticket list:**
- Onboarding tickets carry an *Onboarding* tag and "IT tasks 2 of 4 done", and open the onboarding page.
- The **Onboarding** queue holds the open ones with IT tasks still to do.
- **My tickets**, on the list and the Overview, includes any onboarding where one of your unfinished IT tasks is given to you, as well as the ones you lead.
- Onboarding tickets can't be merged.

**The Onboarding officer role** holds only the Onboarding module, for whoever runs onboarding without being a technician. They see Overview and Onboarding, and can do everything on an onboarding, IT tasks included. The role was added once to every install. Nobody holds it until you give it to someone, and if you delete it, it stays deleted. New installs also give Senior Technician full Onboarding permissions.

**Onboarding is internal.** It never appears in the staff portal, to the new starter or anyone else.

**On the Overview**, anyone with Onboarding or Tickets access sees:
- a **New starters** card, with how many start in the next 14 days and how many onboarding tasks are overdue. It opens the Onboarding list, or the ticket list's Onboarding queue for someone without the Onboarding module;
- a **New starters** panel, listing the unfinished onboardings for anyone starting in the next 14 days or already started, soonest first, with progress and overdue tasks.

**The onboarding report** (**Reports → Onboarding**) is covered in [section 8](#8-reports). New installs give it to Senior Technician and the Onboarding officer. An install that already had the officer role doesn't have it added: tick **Onboarding report** (and Reports: Access) on the role if the officer should have it.

**Demo data.** New installs include a demo onboarding for *Alex Morgan (demo)*, starting two weeks after install, from the Teacher template with the first two tasks done. **Go live** removes it with the other demo records. A real onboarding whose line manager was the demo requester keeps going without a line manager.

**Permissions:**
- The **Onboarding** module in the role editor covers the list, starting onboardings, editing details and tasks, the officer's tasks, cancelling (Edit) and deleting (Delete).
- A technician without it can still open an onboarding from the ticket list, tick its IT tasks and add notes, using their Tickets permissions.

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

The Overview page and the asset reports both show **Assets to review** — anything whose warranty ends or replacement falls due within the review window (or has already passed), plus overdue loans. The window is set in **Settings → Asset, part & loan rules** (default 60 days).

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

Categories and locations are managed lists, set up in **Settings → Part categories** and **Settings → Part locations**. Part locations are deliberately separate from the building-level Locations list used by people and assets, because they describe shelves and cupboards rather than rooms.

A duplicate SKU is allowed but warns.

### Low stock

A part is **low** when its quantity on hand is at or below its reorder threshold. Each part can set its own threshold; anything left blank uses the default in **Settings → Asset, part & loan rules** (default 5).

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

Each report is its own permission in the role editor. The tab strip at the top shows only the reports this role can open.

### Asset reports (`/Reports`)

Review list, counts by type/make/location/status, fleet age and refresh planning, warranty expiry (with a configurable window), and problem devices by ticket count over a chosen period.

### Ticket reports (`/Reports/Tickets`)

Filterable by period (30 days, 3 months, 6, 12, all time) and type (both, incidents only, requests only).

- **SLA performance** — on time, late, open and overdue, broken down by type, priority, category and technician, worst first, plus the longest-overdue open tickets. The on-time rate leaves out tickets that are open but not yet due, and those with no due date.
- **Technician workload** — open now split by incident and request, overdue, oldest open, closed in period, average resolution, and per-team figures.
- **Repeat problems** — devices with the same category of ticket more than once, assets and requesters with the most tickets, and category counts against the previous period.

### Parts reports (`/Reports/Parts`)

Every part at or below its reorder threshold, with quantity against threshold, plus a count of what is completely out of stock.

### Onboarding report (`/Reports/Onboarding`)

For whoever runs onboarding and the IT lead. The period is an academic year, or all time, and picks onboardings by **start date**. A September starter therefore counts in the year they joined, even if their before-arrival tasks were done in August. The *In progress now* figures ignore the period.

- **In progress now** — every unfinished onboarding, with its start date, progress, IT tasks left and overdue tasks.
- **Tasks that run late** — the same task across different starters, matched by name and owner, most often late first. It shows how many times each was late, how many are still overdue, and the median days late. A task late every time probably needs an earlier due date in its template.
- **Who the tasks fell to** — the onboarding officer, the IT team as a whole, and each technician named on an IT task. It shows how many tasks each had, how many are done, on time, late and overdue now.
- **By kind of starter** — per template: started, finished, cancelled, the median finish (days from the start date to the last task being ticked, before or after), and the share of tasks done on time.
- **New starters** — everyone starting in the period, month by month and one by one.

**Late** means a task was ticked after its due date, or is past its due date and still not done. Tasks not due yet don't count either way, and neither do tasks left undone on a cancelled onboarding.

With **Export and print reports**, there are CSVs of the starters and of every task, with due date, date done, who ticked it and days late.

---

## 9. Settings reference

**Settings** is an index: one card per settings page, in five groups, with a **Find a setting** box that narrows the cards as you type (Enter opens the only match). Anything that needs attention is flagged at the top and on its card: failing backups, an unencrypted connection, demo data still present.

| Group | Pages |
|---|---|
| Tickets | **Ticket queues & closing** (the "due soon" window, default 24 hours; how long replies reopen closed tickets, default 14 days; which priorities/categories need a closing message; the Word print template), Statuses, Categories, Priorities, SLAs, School day and periods, Ticket templates, Ticket custom attributes |
| Assets & inventory | **Asset, part & loan rules** (asset review window, default 60 days; academic year start; default reorder threshold, default 5; repeat-borrowing flag), Asset types (and lifespans), Makes, Models, Asset statuses, Custom asset attributes, Part categories, Part locations, Loan reasons |
| People & places | Teams, Departments, Locations, **Imports** (users, technicians and option lists by CSV) |
| Projects | **Spending bands** (each band's range, quotes needed and requirements; whether projects are banded including or excluding VAT; whether a project's page shows its amounts excluding VAT, the default, or including it - the proposal PDF always shows both), Purchasing requirements |
| System | **Branding & logo** (school name, overview wording, colours, logo, default appearance), Backups & data, **Sign-in security** (HTTPS status, lockouts, requiring two-step sign-in and who has it), **Error log**, Audit log, **Go live & reset** (remove the demo data; factory reset) |

**How a project is banded.** Each project is placed in a band by the **highest quote for each item**, added up over the whole contract. The band therefore shows as soon as the first prices go in, before any quote is chosen, which tells you how many quotes to gather.
- Choosing a cheaper quote doesn't move a project down into a band that needs fewer quotes.
- A quote counts once it has prices, unless it's marked declined.
- Items with no priced quote add nothing yet, and the project page says the total may still rise.
- The project's ⓘ Spending bands panel and the proposal PDF list the highest quote for each item.

The bands are for reference only: nothing is blocked.

**Who can change what.** Opening the Settings index needs Settings: Access. Every page behind it needs Settings: Edit, except the audit log, which has its own permission. A role with Access alone sees the cards but no links, and the index itself has no forms to post.

**Light and dark.** Each browser chooses for itself from the **Appearance** switch at the bottom of every page, including the staff portal and the sign-in page: Light, Dark, or Match device, which follows the computer's or phone's own setting. The choice is a cookie on that device, so a technician's laptop and the staffroom PC can differ. Pressing the chosen one again goes back to the school's default, set in **Branding & logo** (a new install uses Match device; an upgraded install keeps the look it had). In dark, the branding background and accent are replaced with dark ones and the primary colour is lightened for text, so any school colour stays readable. Printing is always light.

**Phones and tablets.** The helpdesk works at phone width. Below 980px the top navigation and the account menu fold behind a **Menu** button, and Log a ticket stays in the bar as a **+** button. Below 800px the working lists (tickets, projects, assets, parts, loans, kits and the audit log) become cards: the ticket number and title head each card, every other value is labelled, the tick box for bulk actions sits in the corner, and tapping the card opens it. Filters sit two to a row, the ticket queues are one row you can swipe along, and form fields use 16px text so iPhones don't zoom in on every tap. Reports keep their tables, which scroll sideways on a small screen.

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

- **Files.** Staff can attach up to 3 files (10 MB each, the same types as the helpdesk accepts) to a new ticket or a reply. On the ticket page these show as **From the requester**. A technician's own attachments stay private unless they tick **Share with the requester** when uploading, or use **Share with requester** on the file later. The portal only ever lists the requester's files and shared ones.
- **Replies to closed tickets.** A reply reopens a closed ticket for 14 days after it closed (**Settings → Ticket queues & closing**; 0 turns reopening off). After that the portal offers **Report it again** instead. That raises a new ticket, pre-filled from the old one and linked to it as related.
- **Who has the last word.**
  - A **Reply** badge on the Tickets list marks an open ticket whose latest public message came from the requester.
  - In the portal, **New reply** marks a ticket with a message from IT, or a status change, that the requester hasn't opened yet. The home page counts them.
  - Internal notes never count.

**To give a member of staff access**: People → Add user and fill in their details. Leave the password empty and they get a temporary one, which they change the first time they sign in, or type one they'll keep. Print the quick start guide that opens and give it to them. For a forgotten password, use **Give them a temporary password** on their page. They can change their password later from the portal home page.

**Many staff at once.** When you import users or technicians in **Settings → Imports**, leave **Give each a temporary password and print their quick start guides** ticked. Everyone imported gets a temporary password, and you go straight to their guides, one to a page, sorted by department. Print them straight away: they're only shown for an hour, and only to the person who imported them. Administrator accounts in a technicians CSV only get a password when an Administrator imports them.

Staff imported with the box unticked, or before this existed, have no password. The Users and Technicians tabs on the People page then show **Temporary passwords for N without one**, which does the same for every active account still without a password. The portal records the helpdesk makes for technicians are left out, because they're reached through the technician's own sign-in.

Titles are limited to 200 characters and descriptions and messages to 5,000, and a post to any portal page is capped at 1 MB.

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

Upload a `.docx` at **Settings → Ticket queues & closing**. Placeholders are replaced with the ticket's details:

`{{Job.Number}}`, `{{Job.Title}}`, `{{Job.Status}}`, `{{Job.Comments}}` (public comments only), `{{Requester.Name}}`, `{{Technician.Name}}`, `{{Asset.Tag}}`

---

## 13. The audit log

**Settings → Audit log** records every change made in the system: tickets, assets, parts, people, suppliers, lists, SLAs, custom attributes and settings. Filter by area, action, date or free text.

It works by snapshotting the data before and after each save and recording the differences, so any new action is audited automatically without having to log itself. Ticket and asset history and comments, and parts stock adjustments, are folded into the same view.

Each line records who made the change: the technician's account, the requester's name for anything done in the staff portal, or System for scheduled work such as backups and retention. Lines from before attribution was added show a dash. Retention can trim the log after a set number of months (section 16).

---

## 14. Factory reset

**Settings → Go live & reset** erases everything and returns the system to a new install: all tickets, assets, people, suppliers, parts, projects, SLAs, custom attributes, every list you added to, the print template, attachments, and branding. Only the small demo set a new install ships with remains.

You must type `DELETE` in capitals to confirm. By default it:

- Makes a backup zip first, the same checked kind as the nightly backups, in the backup folder.
- Keeps the audit log, and records the reset itself as an entry.

Both of those can be turned off with the tickboxes. The backup settings survive the reset. The database is compacted afterwards so deleted rows do not linger in the file.

To restore, see section 15.

---

## 15. Backups

**Settings → Backups & data.** Every night (02:00 by default), the app writes one zip, `EduHelpdesk-backup-<date>-<time>.zip`. It holds the database, every attachment, the logo, the print template and `RESTORE.txt`. If the app was off at backup time, the backup runs shortly after it next starts, and a failed attempt retries every hour.

- **Checked before it's kept.** The database copy is taken with SQLite's online backup, so there's no need to stop the app. It is integrity-checked and counted before the zip is kept, and the summary (for example "62 tickets, 155 assets…") is shown on the page.
- **Where.** The `backups` folder inside the data folder by default. Point it at another drive, a network share or a synced folder, so a lost disk doesn't take the backups with it. Backup zips are safe to sync: each is written under a temporary name and renamed only when complete.
- **How long.** Zips older than the chosen number of days (14 by default) are deleted, but the newest three are always kept. Nothing else in the folder is touched.
- **Warnings.** The overview warns anyone who can edit Settings when backups are off, failing, or more than two days old.
- **Not encrypted.** The zips contain personal data (names, emails, ticket text), so keep the backup folder somewhere only IT can read.

**To restore:**
1. Stop the app.
2. Rename the data folder to keep it, and create an empty folder with the original name.
3. Unzip the backup into the empty folder.
4. Copy the `keys` folder across from the old one, so sign-ins keep working. Without it, everyone just signs in again.
5. Start the app.

Each zip's `RESTORE.txt` has the same steps, with the real folder name filled in.

**Moving the data folder:** set `EduHelpdesk:DataPath` in `appsettings.json` (or the environment variable `EduHelpdesk__DataPath`) to a full path, then restart. On the first start with an empty target, the app copies the database, attachments, logo, template and sign-in keys across. It then renames the old database to `helpdesk.db.moved-<time>` and leaves `DATA-MOVED.txt`, so the stale copy can't be used by mistake.

Test a restore at least once, so you know the process works before you need it.

---

## 16. Leavers and personal data

### The leaver check

Mark someone **Inactive** on their person page (People → their name) when they leave. The date is recorded, and the page's **Leaver check** then lists what is still recorded against them: assets they hold, loan kits they have out, open tickets they raised, active projects they asked for, and whether they were the only project lead. **Book everything back in** ends their loans and takes the assets off them in one go; anything that was in use or on loan goes back to stock, and anything marked in repair or lost keeps that status. Open tickets and projects are linked, to close or pass on.

Saving someone as inactive while they still hold equipment says so straight away. People who have left but still hold something are also flagged in the People list (**Still holds 3**; filter **Status: Left, still holding equipment**), and their assets appear on the asset review list as **Leaver** items.

People already inactive when this was added count as having left on the day of the upgrade.

### Subject access

**Download their data (.zip)** on a person's page (needs Requesters: Edit) gives everything the helpdesk holds about them:

- `report.html`: readable in any browser, and printable to PDF. It covers their record; every ticket they raised, with all updates, internal notes (marked INTERNAL), history and custom fields; the assets they hold or have held, with dates; their kit loans; the projects they asked for, with notes; other tickets that mention their full name or email (listed, not copied); and audit log lines about their record, their use of the staff portal and sign-ins with their email.
- `data.json`: the same, as data.
- `files/`: the files attached to their tickets.
- `README.txt`: what to check before sending, such as internal notes, other people named, and the mentions list.

Password hashes are never included. Every download is written to the audit log with who took it. Deciding what to redact or withhold is for the school's DPO, not the software.

### Data retention

**Settings → Data retention** has three rules, each in months (0 keeps everything, which is the default):

| Rule | What it does |
|---|---|
| Delete closed tickets | Tickets closed more than N months ago are deleted with their updates, notes, history, files, custom fields and audit lines. Open tickets are never touched. Numbers are never reused; reports for those months stop counting them. Parts used on them are not put back in stock. |
| Anonymise people who have left | Requesters inactive for more than N months become "Former staff member": name, email, department, location and password go from their record, and their name and email are replaced on the tickets and projects they raised, their loans, asset holding history and asset notes, and in the audit log. Their tickets and loans stay, so counts still add up. Anyone who still holds equipment or has an open ticket or active project is skipped until that is sorted out. Free text other people wrote about them on *other* tickets is not rewritten; the subject access export lists where it is. |
| Delete old audit entries | Audit log lines older than N months (at least 12) are deleted. Ticket, asset and project history lives with those records and is unaffected. |

The page shows what each rule would remove right now. Rules run every night after the backup, and only once a backup has worked in the last day. **Back up, then apply now** runs them straight away, taking a backup first. Each run writes one summary line to the audit log, with counts and ticket numbers but no names, and changing the rules is audited too. Backups still hold removed data until they age out (section 15).

---

## 17. Known limitations

Things the system deliberately or currently does not do. Worth knowing before someone asks:

| Limitation | Notes |
|---|---|
| No email at all | No notifications, no email-to-ticket, no outbound replies. Requesters must check the portal. |
| No in-app notifications | Nothing tells a requester their ticket changed. |
| No "forgotten password" reset | There is no email, so a forgotten password is reset by a technician. People can change their own once signed in. |
| No ticket or parts CSV import | Assets only. |
| No scheduled or emailed reports | Reports print to PDF and export to CSV on demand. |
| No time tracking | Not built, by choice. |
| No approvals or change management | Requests do not route for sign-off. |
| No asset discovery or agents | Nothing scans the network; the register is what you put in it. |
| No barcode or QR scanning | Labels print, but carry no scannable code. |
| No asset stocktake workflow | Parts have stock adjustments; assets do not. |
| Single school | Locations exist, but there is no multi-site tenancy or scoping. |
| No Microsoft 365 or Google sign-in | Staff portal accounts have their own passwords, set by IT. |

---

## 18. Troubleshooting

**Someone saw "Something went wrong" with a reference.**
Open **Settings → Error log** and search for the reference: the entry has what failed and the technical details. Anyone reaching the helpdesk over the network sees only the plain error page; the full developer error page appears only on the machine running it, and only in Development.

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

**"Too many failed sign-ins for this account."**
5 wrong passwords within 15 minutes locked that email for 15 minutes. Wait, or have someone set a new password for the account, which unlocks it at once. The audit log (area Sign-in) shows when it happened and from which address.

**"Too many failed sign-ins from this computer."**
30 wrong passwords came from that address, across any accounts. It clears after 15 minutes, or when the app restarts. If the whole school hits this at once, the app is probably behind a proxy without `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (section 2), so everyone looks like one address.

**A technician has lost their phone (two-step sign-in).**
They can sign in with one of their recovery codes. Without those, someone with Staff accounts: Edit resets their two-step sign-in from their staff account, and they set it up again. If every Administrator is locked out, restore from a backup - or, on a test copy only, clear `TotpSecret` for that technician row in SQLite.

**Nobody can sign in / the Administrator password is lost.**
The system guarantees an Administrator account exists, but it cannot be recovered from the UI. Restore from a backup, or — as a last resort on a test copy — clear the `PasswordHash` for that technician row directly in SQLite and sign in with the bootstrap credentials.

**The app will not start because the database is locked.**
Another copy is already running. Only one process can hold `helpdesk.db`.

---

## 19. Where things live in the code

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
| Auth and permissions | `Program.cs`, `Services/PermissionAuthorizationHandler.cs`, `PasswordHasher.cs`, `PortalIdentity.cs`, `TechnicianSession.cs` |
| Sign-in protection | `Services/SignInThrottle.cs`, `PasswordRules.cs`, `SessionFilters.cs`, `SecurityHeaders.cs`, `HelpdeskStore.Sessions.cs`, `HelpdeskStore.TwoFactor.cs`, `Totp.cs`, `TwoFactorPending.cs`, `Pages/TwoFactor.cshtml`, `Pages/LoginCode.cshtml` |
| Backups and the data folder | `Services/HelpdeskStore.Backups.cs`, `BackupScheduler.cs`, `DataLocation.cs`, `SaveFailureFilter.cs` |
| Error log and error page | `Services/FileLog.cs`, `UnknownHandlerFilter.cs`, `Pages/Error.cshtml`, `Pages/Settings/Log.cshtml` |
| Leavers, subject access, retention | `Services/HelpdeskStore.Lifecycle.cs`, `SubjectAccessExport.cs`, `Pages/User.cshtml`, `Pages/Settings/Retention.cshtml` |
| Themes and phone layout | `Services/Themes.cs`, `wwwroot/css/site.css`, `wwwroot/js/layout.js`, `picker.js` |
| Page behaviour (buttons, dialogs, tabs, bulk selection) | `wwwroot/js/actions.js` (the `data-` attributes every page uses), `bulk-select.js`, `wwwroot/js/pages/*.js` |
| Tests | `Tests/EduHelpdesk.Tests` (xUnit): SLA clock, passwords and sign-in lockout, the store against a real database in a temporary folder, leavers and retention, subject access, and the no-inline-script rule |

Three conventions to preserve when changing anything:

1. **Schema changes** go in `EnsureSchema` as `CREATE TABLE IF NOT EXISTS` or a try/catch `ALTER TABLE`, so existing databases upgrade themselves on startup.
2. **Multi-value fields** (a list on a record) use a join table with `ON DELETE CASCADE`, an explicit `DELETE FROM` before the parent in `WriteData`, and must be sourced from the stored record in page handlers — never from the posted form model, which will bind them empty and silently wipe them.
3. **No script in the pages.** The security policy refuses inline `<script>` blocks and `onclick=`/`onchange=` attributes, so they silently do nothing. Use the `data-` attributes described at the top of `wwwroot/js/actions.js` (`data-confirm`, `data-autosubmit`, `data-open-blade` and so on), or put page-specific code in `wwwroot/js/pages/` and load it from the page's `Scripts` section. Data a script needs goes in `data-` attributes.

To run the tests: `dotnet test Tests/EduHelpdesk.Tests`. Each test builds its own store in a temporary folder, never the live database. While the helpdesk itself is running from this folder its `bin` is locked, so add `-o` with a folder of your own (for example `-o %TEMP%\eh-tests`).
