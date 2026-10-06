# EduHelpdesk: current feature inventory (as of 2026-10-03)

Sources are the codebase (paths relative to the repo root `C:\Users\steve\OneDrive\Documents\05 - Projects\EduHelpdesk`) and the owner's Claude project memory notes (`mem:<file>.md`, in `C:\Users\steve\.claude\projects\C--Users-steve-OneDrive-Documents-05---Projects-EduHelpdesk\memory\`). Where notes or docs conflict with code, the code is treated as correct and the conflict is listed in section 8. Read-only investigation: nothing was built or run.

## 1. Product shape, platform, scale and release state

### Takeaway
EduHelpdesk is a self-hosted, single-school, single-instance ASP.NET Core (.NET 10) Razor Pages app. All data sits in memory and is saved to one SQLite file. It is installed on a Windows server as a Windows service from a release zip. Current version is 1.8 (2 Oct 2026), MIT-licensed and intended to be shared with other schools. It now covers tickets, a staff portal, assets, loans and kits, parts, suppliers, purchasing projects, onboarding, reports and an audit log.

### Cited Findings
- Platform: ASP.NET Core Razor Pages, .NET 10; SQLite `App_Data/helpdesk.db` with WAL; the whole dataset is held in memory and saved on every change. Since 2026-09-30 a save writes only changed rows, falling back to a full rewrite on the first save after startup, after a failed save, or when rows that other tables point at are reordered — [docs/Technical-Guide.md:18-32](docs/Technical-Guide.md); [Services/HelpdeskStore.SaveChanges.cs](Services/HelpdeskStore.SaveChanges.cs); [mem:saving-only-changes.md]
- NuGet dependencies: DocumentFormat.OpenXml 3.5.1 (Word print templates), Microsoft.Data.Sqlite 10.0.12, Microsoft.Extensions.Hosting.WindowsServices 10.0.12, PDFsharp-MigraDoc 6.2.4 (proposal and welcome-pack PDFs), QRCoder 1.8.0 (only used for the TOTP setup QR code) — [EduHelpdesk.csproj:19-23](EduHelpdesk.csproj); [Pages/TwoFactor.cshtml.cs:90-92](Pages/TwoFactor.cshtml.cs)
- Version: `<Version>1.8.0</Version>` — [EduHelpdesk.csproj:12](EduHelpdesk.csproj). Git history has releases 1.0 to 1.8; the v1.8 commit is dated 2026-10-02 (git tags only go up to v1.5). A GitHub release-upload workflow exists (`.github`). LICENSE.txt is MIT — git log; [mem:school-installer-requirements.md]
- Main modules (Services/Modules.cs:33-49), as permission modules: Tickets, Projects, Onboarding, Assets, Kits, Loans, Parts, Suppliers, Requesters, Staff accounts, Roles, Reports, Settings, Audit log. Top nav: Overview, Tickets, Projects, HR (People + Onboarding), Inventory (Assets, Kits, Loans, Parts, Suppliers), Reports, Settings. Menu items are hidden when the role has no access — [Pages/Shared/_Layout.cshtml:86-127](Pages/Shared/_Layout.cshtml)
- Scale the owner stated in interviews: 20-100 open tickets and 3-8 technicians (ticketing, 2026-09-21); 500-2,000 assets identified by tag stickers (assets); 50-300 parts in one physical location (parts) — [mem:ticketing-requirements.md]; [mem:asset-management-requirements.md]; [mem:parts-inventory-requirements.md]
- The real Spiceworks export from the owner's school had 1,288 tickets, 4 technicians and 76 end users. The owner imported it for real on 2026-09-30 — [mem:spiceworks-import-requirements.md]
- Measured performance. One year at a large secondary (1,500 staff, 3,000 assets, 8,000 tickets): 3 s start-up, about 50 ms per change, 390 MB RAM. Five years (40,000 tickets, 6,000 assets): 8 s start-up, about 0.25 s per change, 900 MB — [docs/Technical-Guide.md:36-39](docs/Technical-Guide.md)
- Single school only. The guide says: "Locations exist, but there is no multi-site tenancy or scoping." The code has no site, school, tenant or trust entity (zero matches for SiteId/SchoolId/TenantId/TrustId) — [docs/Technical-Guide.md:842](docs/Technical-Guide.md); code grep
- Must stay single instance (in-memory singleton plus single-writer SQLite). Never scale out — [mem:azure-hosting-plan.md]; [mem:mysql-migration-requirements.md]
- Test suite: xUnit project `Tests/EduHelpdesk.Tests`, 156 `[Fact]`/`[Theory]` attributes across 24 files (as of today). The memory notes' figure of 41 tests is out of date. A `PageConventionTests` test fails the build on any inline script or onclick — code grep; [mem:system-review-2026-09-26.md]

### Inferences
- The "trust" references (orders placed via the Trust in the finance report; an installer answers file "for trusts") point to MAT use by repeated per-school installs, not a multi-school tenancy.
- Tested scale comfortably exceeds the owner's own school. The in-memory model sets the ceiling: about 900 MB RAM at five years of a large secondary.

### Gaps
- No load or concurrency testing with many simultaneous users was recorded.
- The number of other schools actually running it is unknown.

## 2. Ticketing (fields, queues, filters, bulk, templates, attachments, comments, SLA, teams, assignment, merging, time, imports)

### Takeaway
Ticketing is mature for a technician-led desk:
- server-side queues, filters and search; bulk actions; templates; incident/request types
- public and internal comments; attachments; related and follow-up links; merging
- SLAs counted in school work days or lesson periods, with pause-on-status
- closing rules, a reopen window and Spiceworks import

Absent: email, auto-routing, recurring tickets, time tracking, satisfaction ratings and approvals.

### Cited Findings
- **Ticket record fields:**
  - number, title, description, requester, multiple linked assets, one technician, priority, status, category, SLA, due date (can be overridden), SLA override, team, location, created and closed dates
  - comments (public or internal, and a FromRequester flag)
  - history, Type (Incident/Request), SubCategory (the service catalogue item), SLA pauses, RequesterSeenAt
  - Source: [Models/HelpdeskModels.cs:223-258](Models/HelpdeskModels.cs)
  - Comments carry `IsInternal` and `FromRequester`; every history, comment and audit line records the actor who made it — [Models/HelpdeskModels.cs:75-86,272-282](Models/HelpdeskModels.cs)
- Default lists:
  - Statuses: Open, In Progress, On Hold, Closed. "Closed" is built in and can't be renamed or deleted.
  - Priorities: Normal, Low, High, Urgent.
  - Categories: Hardware, Software, Account, Network, Classroom AV, Other. On Hold pauses the SLA by default.
  - Source: [Services/HelpdeskStore.Options.cs:547-573](Services/HelpdeskStore.Options.cs)
- Queues (tabs): Open (default), My tickets, Unassigned, Overdue/due soon, Onboarding, All — [Services/TicketListQuery.cs:67-99](Services/TicketListQuery.cs). The due-soon window defaults to 24 h — [docs/Technical-Guide.md:252-266](docs/Technical-Guide.md)
- Search covers title, description, comments, requester, asset tag, ticket number and custom attribute answers. Filters:
  - multi-select checkboxes for status, type, priority and category
  - dropdowns for technician (including Me and Unassigned), team, requester, department and location
  - an asset filter, shown as filter chips with Clear all
  - sort on any column; pages of 25/50/100/200
  - Source: [docs/Technical-Guide.md:268-275](docs/Technical-Guide.md)
- Bulk actions:
  - change status, technician, team, priority, category or type
  - add one comment to all; close with a message; merge; export CSV
  - work on ticked rows or on "all N matching"
  - assigning a technician skips tickets in teams they are not in
  - Source: [docs/Technical-Guide.md:277-287](docs/Technical-Guide.md); [Services/TicketCsv.cs](Services/TicketCsv.cs)
- Ticket page: sidebar fields for technician, team, SLA, due date, status, priority, type, category and location. Tabs: Comments, History, Assets, Parts, Attachments. Actions: close, merge, delete, and three print options — [docs/Technical-Guide.md:289-311](docs/Technical-Guide.md)
- Internal notes never show to the requester and never print. Comments can be removed: your own with Edit, anyone else's with Delete. The text is gone for good, but history keeps who wrote it and who removed it (commit 5d64690, 2026-09-30) — [docs/Technical-Guide.md:303-305](docs/Technical-Guide.md); [Services/HelpdeskStore.Comments.cs](Services/HelpdeskStore.Comments.cs)
- Attachments:
  - 10 MB per file, 5 per upload, whitelisted types (no archives, SVG or HTML)
  - served with nosniff and a sandbox CSP; only images display inline
  - can be shared with the requester per file
  - Source: [docs/Technical-Guide.md:307,622](docs/Technical-Guide.md); [Services/HelpdeskStore.TicketExtras.cs](Services/HelpdeskStore.TicketExtras.cs)
- Links: "related" and "follow-up". A follow-up copies requester, assets, category, priority, technician, team and type. Merging moves comments, attachments and links. Onboarding tickets can't be merged — [docs/Technical-Guide.md:309,421](docs/Technical-Guide.md); [mem:ticketing-requirements.md]
- Ticket types: a fixed pair, Incident and Request, used only to separate lists and reports — [Models/HelpdeskModels.cs:194-202](Models/HelpdeskModels.cs)
- Templates (Settings > Ticket templates):
  - name, type, title, description, category, priority, optional SLA, custom-attribute defaults
  - optionally shown in the portal with a helper line and a set order
  - no team or technician on a template
  - Source: [Models/HelpdeskModels.cs:203-214](Models/HelpdeskModels.cs); [mem:ticketing-requirements.md]
- Custom ticket attributes: single line, multi-line or choice list, optionally limited to certain categories — [docs/Technical-Guide.md:604-606](docs/Technical-Guide.md)
- **SLAs:**
  - name plus duration in minutes, hours, days, work days or lesson periods, optionally scoped by priority and category; the matching SLA sets the due date automatically
  - per-ticket override of the due date or SLA
  - Settings > School day sets which weekdays are school days and the lesson period times (one timetable for every day)
  - periods count from the next full period
  - no term dates or holidays, so tickets over a holiday show overdue
  - Source: [Services/SlaClock.cs:8-18](Services/SlaClock.cs); [Models/HelpdeskModels.cs:180-187](Models/HelpdeskModels.cs); [mem:sla-school-time-requirements.md]
- SLA pause: any status except Closed can pause the clock (On Hold by default). The due date moves on by the paused time in the SLA's own units. A ticket that is already overdue can't be paused. A due date typed by hand is never moved — [docs/Technical-Guide.md:327-334](docs/Technical-Guide.md); [Services/HelpdeskStore.TicketProcess.cs](Services/HelpdeskStore.TicketProcess.cs)
- Closing rules: a closing message can be required per priority and per category. Reopen window: a requester reply reopens a closed ticket for 14 days by default (0 turns it off); after that the portal offers "Report it again", which links the new ticket as related — [docs/Technical-Guide.md:336-338,623](docs/Technical-Guide.md)
- Assignment and teams:
  - one technician plus one team per ticket; teams are a managed list
  - "Working as" picker for whose queue "My tickets" shows (changing it needs the `Tickets.WorkingAs` flag)
  - no automatic assignment or routing rules: portal tickets are created with no technician, no team and no assets (`[], null ... null` in the AddTicket call)
  - no grep matches for auto-assign, round-robin, routing or escalation
  - Source: [Pages/Portal/NewTicket.cshtml.cs:132-136](Pages/Portal/NewTicket.cshtml.cs); [Services/Modules.cs:64,80](Services/Modules.cs); code grep
- Parts consumed on a ticket: stock is deducted on assignment, restored on ticket delete and moved on merge — [docs/Technical-Guide.md:526](docs/Technical-Guide.md)
- Projects can be started from or linked to a ticket — [mem:projects-quotes-requirements.md] (stage 6)
- Time tracking: none. The guide lists "No time tracking - Not built, by choice". Spiceworks time entries arrive only as history lines — [docs/Technical-Guide.md:837,685](docs/Technical-Guide.md)
- Recurring/scheduled tickets: none (no code matches). Satisfaction rating: none (no code matches). Approvals or change management: none ("Requests do not route for sign-off") — code grep; [docs/Technical-Guide.md:838](docs/Technical-Guide.md)
- Ticket import: no generic CSV ticket import "by design". The Spiceworks Cloud importer (Settings > Imports > Import from Spiceworks):
  - reads the .xlsx full export and shows a preview with value mapping
  - takes a backup first and gives new numbers, keeping the Spiceworks number as a searchable custom attribute
  - brings across comments (private ones become internal notes), change history and merges
  - creates requesters
  - supports repeat imports with conflict detection, and undo of the last import
  - imports no attachments (the export has none)
  - Source: [docs/Technical-Guide.md:668-701](docs/Technical-Guide.md); [Services/HelpdeskStore.SpiceworksImport.cs](Services/HelpdeskStore.SpiceworksImport.cs), [Services/HelpdeskStore.SpiceworksApply.cs](Services/HelpdeskStore.SpiceworksApply.cs)
- Printing: three print views of a ticket.
  - Full ticket: an uploaded Word template (placeholders `{{Job.Number}}` etc.) or a standard layout; public comments only.
  - Label: a 62 mm receipt-printer label showing ticket number, title and date only, with no barcode or QR.
  - Job sheet.
  - Source: [Pages/PrintLabel.cshtml:10-25](Pages/PrintLabel.cshtml); [docs/Technical-Guide.md:705-717](docs/Technical-Guide.md)

### Inferences
- Gaps against typical helpdesk products cluster around automation: routing, recurring tickets, auto-close and reminders, canned replies, email. Several of these were explicitly declined (see section 9).
- School-time SLAs (lesson periods, school days) set it apart; most generic helpdesks offer only business hours. Without term dates, though, SLA overdue counts are wrong over holidays.

### Gaps
- The reports have no first-response time metric. The owner declined "first-response tracking" (section 9).
- Was it re-verified recently that ticket-side part consumption is still not gated by the Parts permission? The memory says the owner chose to leave it ungated (2026-09-22); the current handler was not checked.

## 3. Requester (staff) portal

### Takeaway
`/Portal` is a separate, lighter login for teachers and support staff: email and password set by IT, with no SSO and no 2FA. They report problems through a category-tile and button launcher fed by a Settings-managed service catalogue and portal-visible templates. They can see and reply to their own tickets (with files), get pings and pop-ups on staff replies, and, if ticked as a project raiser or lead, raise or assign purchasing projects. There is no knowledge base, FAQ, announcements page, device picker or satisfaction rating.

### Cited Findings
- Access: the `/Portal` folder allows anonymous visitors, and the site root sends signed-out visitors there. Each requester needs a People record created by IT. The password is a temporary one (three words and a number, changed at first sign-in) or one IT types. A printable quick start guide is produced, and bulk temporary passwords plus guides are printed on CSV import — [Program.cs:36-38](Program.cs); [docs/Technical-Guide.md:170-201,629-633](docs/Technical-Guide.md)
- Sign-in:
  - email and password only; no Microsoft 365, Google or Entra sign-in (no OpenIdConnect, MicrosoftAccount or Google packages)
  - "The staff portal has its own passwords and doesn't use two-step sign-in"
  - no self-service reset ("Forgotten your password? Ask the IT team")
  - session ends when the browser closes, after 2 h idle or 12 h in all; "Keep me signed in" lasts 30 days
  - lockout after 5 failures per email or 30 per IP in 15 minutes
  - Source: [Pages/Portal/Index.cshtml:17-27](Pages/Portal/Index.cshtml); [docs/Technical-Guide.md:132-148,213](docs/Technical-Guide.md); [EduHelpdesk.csproj](EduHelpdesk.csproj)
- Technicians reach the portal as themselves through "Open staff portal" in the account menu — [Pages/Shared/_Layout.cshtml:136](Pages/Shared/_Layout.cshtml)
- **Reporting a problem (tile launcher, 2026-10-02):**
  - the steps are category tiles, then that category's buttons (shown catalogue items plus portal-visible templates), then a confirm step (Where is it? How urgent? optional description, files)
  - a dashed "Something else" plain form is always available
  - icons (24 line icons) and 10 colours per category, with order set by hand in Settings > Service catalogue
  - the requester now picks the priority, starting from the button's default; location is required when locations exist and prefilled from the requester
  - up to 3 files of 10 MB
  - a 14-item starter catalogue is seeded only on new installs; the owner's real database started with an empty catalogue
  - Source: [Pages/Portal/NewTicket.cshtml.cs:34-146](Pages/Portal/NewTicket.cshtml.cs); [Services/PortalLook.cs](Services/PortalLook.cs); [Services/HelpdeskStore.ServiceCatalogue.cs](Services/HelpdeskStore.ServiceCatalogue.cs); [Services/HelpdeskStore.Seed.cs:291-310](Services/HelpdeskStore.Seed.cs); [mem:portal-tile-launcher-requirements.md]; [mem:service-catalogue-portal-form.md]
- What requesters see:
  - only their own tickets, with status, priority, category, location, description, their own files plus any IT has shared, and public updates; never internal notes
  - a "New reply" flag and a count on the home page; can add messages with files
  - a reply reopens a closed ticket within the reopen window
  - text limits: titles 200 characters, messages 5,000; portal posts capped at 1 MB except the two forms that take files
  - Source: [docs/Technical-Guide.md:610-635](docs/Technical-Guide.md); [Program.cs:151-159](Program.cs)
- Projects in the portal: requesters ticked "Can raise projects" can request a purchasing project. A "Project lead" sees every project, assigns technicians and sets priority from the portal. Requesters can download the proposal PDF once it is ready — [Pages/Portal/Index.cshtml:38-55](Pages/Portal/Index.cshtml); [Models/HelpdeskModels.cs:5-11](Models/HelpdeskModels.cs)
- Not present (no code matches): knowledge base or FAQ articles, announcements or known-issues banner, satisfaction rating, requester device picker (portal tickets carry no assets), ticket search in the portal, portal 2FA — code grep; [Pages/Portal/NewTicket.cshtml.cs:132-133](Pages/Portal/NewTicket.cshtml.cs); [mem:second-review-2026-09-27.md]
- Onboarding is internal and never appears in the portal — [docs/Technical-Guide.md:425](docs/Technical-Guide.md)
- Mobile and tablet: the owner said staff use classroom PCs and tablets. The portal tiles were checked at tablet width; the helpdesk has a phone layout (Menu button below 980 px, list cards below 800 px, 16 px inputs) — [mem:portal-tile-launcher-requirements.md]; [docs/Technical-Guide.md:598](docs/Technical-Guide.md)

### Inferences
- The portal deliberately trades features for low friction (teachers between lessons). The biggest missing self-service elements against competitors are SSO with school M365/Google accounts (which also removes IT-issued passwords), an announcements or known-issues banner, a KB/FAQ, and a device picker.
- Accounts are provisioned by IT (manual or CSV), with no directory sync, so keeping starters and leavers current is a manual job.

### Gaps
- Real-device mobile or phone testing of the portal is not recorded; only browser-pane geometry checks are.

## 4. Notifications and integrations (email, Teams, MIS, directory/device sync)

### Takeaway
There is no email at all, and this was ruled out deliberately. Instead there are opt-in browser pings and desktop pop-ups through long-polling, plus a Microsoft Teams Workflows webhook for new portal tickets and overdue tickets. There are no MIS (Arbor/Bromcom/SIMS/Wonde), directory (Entra/Google/AD/LDAP) or device-management (Intune/Google Admin) integrations.

### Cited Findings
- Email: none — no SMTP, MailKit, SendGrid or System.Net.Mail sending. The only `MailAddress` uses validate addresses. The guide says "No email at all - No notifications, no email-to-ticket, no outbound replies", and "There is no emailed reset link, because the helpdesk doesn't send email" — code grep; [docs/Technical-Guide.md:181,832](docs/Technical-Guide.md)
- Why email was ruled out (2026-09-21 interview):
  - requester updates: "nothing automatic"
  - the mail system was "unknown"
  - no email-to-ticket and no canned replies or mailto button ("nothing more needed")
  - Recorded reason: "the user chose these deliberately, so keep to them and don't add email, portal or workflow features unasked". The queues (tickets going quiet) were treated as the fix for "keeping requesters informed".
  - Source: [mem:ticketing-requirements.md]
  - Later notes repeat "mention, don't push" — [mem:second-review-2026-09-27.md]
- **Browser notifications** (built 2026-10-02 in three stages, plus a Settings page):
  - each person opts in per browser; choices kept in localStorage
  - staff are pinged for a new portal ticket ("New ticket received") and for a requester reply ("Reply on ticket #N")
  - requesters are pinged when a technician ticks "Notify requester" on a public comment ("Update on your ticket #N")
  - delivery is a 25 s long-poll to `/notifications/poll`; desktop pop-up on https or localhost, otherwise an in-page banner and sound
  - text is generic only (no ticket text, because classroom screens get projected)
  - messages kept 14 days or 5,000
  - Settings > Notifications has a master switch plus staff and requester switches and a reminder bar
  - no email, web push or service worker
  - Source: [docs/Technical-Guide.md:218-231](docs/Technical-Guide.md); [Services/HelpdeskStore.Notifications.cs](Services/HelpdeskStore.Notifications.cs); [Services/NotificationEndpoints.cs](Services/NotificationEndpoints.cs); [mem:desktop-notifications-requirements.md]
- **Teams channel webhook** (built 2026-10-02, commit 8c60f0f):
  - Settings > Teams channel posts an Adaptive Card through a Teams Workflows webhook for (a) a new portal ticket and (b) a ticket going overdue (checked every 5 minutes; more than 3 at once become one summary)
  - generic text plus an "Open ticket" button, never title or requester
  - the URL is treated as a secret: https on a public host name only, no redirects followed (SSRF guard), never echoed back or written to the audit log
  - retries after 3 s and 15 s
  - the only outbound HTTP call in the app; real delivery to Teams is unverified
  - Source: [Services/HelpdeskStore.Teams.cs](Services/HelpdeskStore.Teams.cs); [Services/TeamsPoster.cs](Services/TeamsPoster.cs); [Services/TeamsWebhook.cs](Services/TeamsWebhook.cs); [Program.cs:212-217](Program.cs); [mem:teams-channel-webhook.md]
- No MIS, directory or device sync: no matches for Graph, Intune, Entra, Wonde, Arbor, Bromcom, SIMS, LDAP or DirectoryServices in code. Intune appears only in installer text about pushing the self-signed certificate. People come in by CSV only (Users: Name, Email, Department, Location; Technicians: Name, Email, Team, Role) — code grep; [installer/Certificate.ps1:128](installer/Certificate.ps1); [docs/Technical-Guide.md:639-668](docs/Technical-Guide.md)
- The Technical Guide's "Known limitations" table still lists "No Microsoft 365 or Google sign-in" and "No asset discovery or agents" — [docs/Technical-Guide.md:839,843](docs/Technical-Guide.md)

### Inferences
- Without email, a requester learns of updates only if they open the portal or have pings switched on. This was the owner's stated trade-off. Staff learn of new tickets through pings or Teams.
- Directory and device sync (stage K) depends on the school creating an Entra or Google app registration. That is the recorded blocker.

### Gaps
- Real Teams delivery, real desktop toasts under Chrome or Edge, and the sound under autoplay rules are recorded as not verified ([mem:teams-channel-webhook.md], [mem:desktop-notifications-requirements.md]).

## 5. Assets, loans, kits, parts and suppliers

### Takeaway
There is a full asset register with lifecycle fields, a review list, bulk tools, a mapped CSV import, disposals kept in the register for audit, and finance reporting. Loans and named kits come with reason codes and a repeat-borrower report for line managers. Parts inventory has reorder thresholds, logged stock adjustments and suppliers. Absent: barcodes/QR, scanning, stocktake, device discovery and sync, depreciation, and part costs.

### Cited Findings
- **Asset fields:**
  - tag, make, model, type, serial, location, assigned user, supplier, status
  - purchase date and price, purchase order, quote reference, warranty end, replacement date override, loan due date
  - disposal date, method and proceeds
  - comments, history, ownership history (assignments with dates, reasons and kit-loan id), custom attributes scoped by asset type
  - Source: [Models/HelpdeskModels.cs:131-179](Models/HelpdeskModels.cs)
- Asset statuses (editable): In use, On loan, In stock or spare, In repair, Lost or stolen, Disposed. "On loan" and "Disposed" are system-guaranteed — [Services/HelpdeskStore.Options.cs:550,567](Services/HelpdeskStore.Options.cs)
- Replacement date = purchase date + lifespan per asset type (Settings), unless overridden on the asset. A duplicate tag is blocked; a duplicate serial warns — [docs/Technical-Guide.md:449-457](docs/Technical-Guide.md)
- Review list (Overview and reports): warranty ending or replacement due within the window (default 60 days), overdue loans, and leavers still holding kit — [docs/Technical-Guide.md:463-465,797](docs/Technical-Guide.md)
- Asset list:
  - search, filters (status, type, make, location, held by; needs review, on loan, loan overdue, disposed), natural sort, paging
  - bulk actions: change status, owner or location; export CSV
  - CSV import with column mapping, preview and match-by-tag (add or update; never deletes)
  - Source: [Services/AssetListQuery.cs](Services/AssetListQuery.cs); [Services/HelpdeskStore.AssetImport.cs](Services/HelpdeskStore.AssetImport.cs); [docs/Technical-Guide.md:467-494](docs/Technical-Guide.md)
- Disposal: Disposed is a status plus date, method (sold, recycled/WEEE, donated, written off) and proceeds. The asset stays in the register for audit and is hidden from day-to-day lists. Disposal is gated by Assets: Delete — [mem:finance-asset-report-requirements.md]; [Services/Modules.cs:38](Services/Modules.cs)
- **Loans and kits:**
  - named loan kits with swappable contents; an asset can be in one kit at most and is chosen by search
  - kit loans take a directory person or a typed-in name (supply staff, visitors); individual asset loans are directory-only
  - a required reason from a Settings list (Forgot own device / Supply or visitor / Own device in repair / Other); due back the same day by default
  - issuing a kit marks its assets On loan; a kit that is out is read-only
  - repeat-borrower flag with a configurable threshold (N loans in D days)
  - `/Loans` desk screen, `/Loans/Issue` (kit or asset), `/Kits`
  - Source: [Models/HelpdeskModels.cs:106-130,154-165](Models/HelpdeskModels.cs); [Services/LoanInsights.cs](Services/LoanInsights.cs); [Services/HelpdeskStore.Kits.cs](Services/HelpdeskStore.Kits.cs); [mem:loan-kits-requirements.md]
- Labels, QR, barcodes and stocktake: not built. The only label is the ticket label (no code on it). The guide says "No barcode or QR scanning - Labels print, but carry no scannable code" and "No asset stocktake workflow". "barcode" appears only as an import column-heading alias — [Pages/PrintLabel.cshtml](Pages/PrintLabel.cshtml); [docs/Technical-Guide.md:840-841](docs/Technical-Guide.md); [Services/AssetImportTargets.cs:36](Services/AssetImportTargets.cs)
- Device discovery and sync (Intune, Google Admin, agents): none — [docs/Technical-Guide.md:839](docs/Technical-Guide.md); code grep
- **Parts:**
  - fields: name, SKU, category and location (separate managed lists), quantity, reorder threshold (per part, or the default of 5), several suppliers, compatible asset types
  - low-stock badge, filter, Overview panel ("Parts to reorder") and report
  - "Adjust stock" requires a reason and is logged; quantity is read-only after creation
  - list search, filters and paging; bulk category, location and delete; CSV export only
  - no unit cost or value, no CSV import
  - Source: [Models/HelpdeskModels.cs:88-105](Models/HelpdeskModels.cs); [docs/Technical-Guide.md:498-530](docs/Technical-Guide.md); [Services/PartInsights.cs](Services/PartInsights.cs)
- Suppliers: a directory of name, contact, email, phone, address, website and notes. Deletion is blocked while a supplier is linked to an asset, part or project — [Models/HelpdeskModels.cs:87](Models/HelpdeskModels.cs); [docs/Technical-Guide.md:532-534](docs/Technical-Guide.md)

### Inferences
- The asset side suits a tag-sticker workflow at 500-2,000 assets. QR labels, scanning and stocktake (stage J) are the obvious next step for audit-style checks. The owner put them off as "not wanted yet".

### Gaps
- There is no warranty lookup from vendor APIs, and there are no software or licence records. Neither has been discussed in the notes.

## 6. Other modules: projects and quotes, onboarding/offboarding, reports, dashboards, printing/PDF

### Takeaway
There are three modules beyond core helpdesk:
- purchasing projects with per-supplier quotes, spending bands and a merged proposal PDF
- staff onboarding checklists with a welcome-pack PDF
- seven report pages with print-to-PDF and CSV

Offboarding beyond the "leaver check" is not built. Dashboards are list and count panels; there are no charts.

### Cited Findings
- **Projects (built 2026-09-25/26, six stages):**
  - raised from the staff portal by people ticked "Can raise projects"; a portal-only "Project lead" assigns them
  - statuses New, Gathering quotes, Proposal ready, Closed (outcome Approved, Not approved or Cancelled)
  - items with sub-items; per-supplier quote status with chase-after-7-days and expiry
  - payment lines (amount, frequency, term, VAT); a chosen quote per item
  - spending bands (Settings) based on the whole-contract total
  - merged proposal PDF (PDFsharp/MigraDoc) with quote PDFs and images appended
  - link to or start from a ticket; Projects report
  - Source: [Services/HelpdeskStore.Projects.cs](Services/HelpdeskStore.Projects.cs); [Services/ProposalPdf.cs](Services/ProposalPdf.cs); [mem:projects-quotes-requirements.md]
- **Onboarding (built 2026-09-29, five stages; shipped in 1.4):**
  - one ticket per new starter, carrying a checklist from per-staff-type templates
  - tasks have an owner (IT or the onboarding officer, optionally a named technician) and a day offset that puts them under Before arrival, First day or First week
  - action tasks: "make portal account" (temporary password plus quick start guide) and "issue a <asset type>" (assigns a free asset)
  - the ticket closes itself when every task is done
  - an Onboarding officer role holds only this module
  - welcome-pack PDF (sign-in details, equipment issued, school IT info, signature block, plus uploaded PDFs chosen per template)
  - Overview "New starters" card and panel, onboarding report
  - Source: [Services/HelpdeskStore.Onboarding.cs](Services/HelpdeskStore.Onboarding.cs); [Services/WelcomePackPdf.cs](Services/WelcomePackPdf.cs); [docs/Technical-Guide.md:340-437](docs/Technical-Guide.md); [mem:onboarding-requirements.md]
- Offboarding: not built as a workflow. What exists is the leaver check on a person's page:
  - marking someone Inactive records a leave date
  - the page lists assets held, kits out, open tickets, active projects and sole project-lead status
  - "Book everything back in"; People-list flag "Still holds N"; Leaver items on the asset review list
  - The notes say: "Offboarding comes later as its own stage, joined to the existing leaver check"
  - Source: [docs/Technical-Guide.md:793-799](docs/Technical-Guide.md); [Services/HelpdeskStore.Lifecycle.cs](Services/HelpdeskStore.Lifecycle.cs); [mem:onboarding-requirements.md]
- **Reports** (each behind its own role flag, plus an Export/Print flag):
  - Assets: review list, counts, fleet age and refresh, warranty, problem devices
  - Tickets: SLA performance, technician and team workload, repeat problems; by period and type
  - Parts: low stock
  - Loans: per-person repeat borrowers, reasons, out and overdue; print-to-PDF for line managers
  - Finance: academic-year spend, a derived order table by PO or quote reference, additions and disposals, holdings four ways; no depreciation
  - Projects
  - Onboarding
  - Source: [Services/Modules.cs:66-73,83-90](Services/Modules.cs); [Program.cs:108-132](Program.cs); [docs/Technical-Guide.md:538-570](docs/Technical-Guide.md); [mem:finance-asset-report-requirements.md]
- Report output: A4 print views (browser print-to-PDF, `_ReportPrint` layout) and per-table CSV. No scheduled or emailed reports. No "volume and trends over time" ticket report (not chosen) — [mem:eduhelpdesk-schema-migrations.md]; [docs/Technical-Guide.md:836](docs/Technical-Guide.md); [mem:ticketing-requirements.md]
- Dashboard (Overview):
  - counts and short lists: My tickets, Overdue and due soon, Unassigned, Replies waiting, New starters, My projects, Assets to review, Parts to reorder
  - Administrator-only health warnings and a backup warning
  - no charts anywhere (no svg, canvas or chart code in Pages except one unrelated match)
  - Source: [Pages/Index.cshtml:75-217](Pages/Index.cshtml); code grep
- Generated PDFs: project proposal, onboarding welcome pack. Print views: ticket (or Word template), ticket label, job sheet, quick start guides, report print views, loans report — [Services/ProposalPdf.cs](Services/ProposalPdf.cs); [Services/WelcomePackPdf.cs](Services/WelcomePackPdf.cs); [docs/Technical-Guide.md:705-717](docs/Technical-Guide.md)

### Inferences
- Projects and onboarding are unusual for a school helpdesk and go beyond typical competitors' scope (procurement quotes, HR onboarding).
- The absence of charts or trend reporting may matter to SLT and governors, who usually expect visual summaries.

### Gaps
- No notes on how often reports are used, or whether governors or the Trust have asked for trends.

## 7. Security, administration, data protection, accessibility and hosting

### Takeaway
Security has been hardened in staged reviews:
- PBKDF2 passwords, lockout, TOTP 2FA (can be required)
- server-side sessions revoked on sign-out, custom roles with per-module ticks
- a full audit log with actor attribution, a CSP with no inline script, HTTPS support (including a self-signed certificate generator)
- nightly verified backups, data retention and anonymisation, subject-access export, a read-only raw-data viewer and a database health check

Absent: SSO (M365/Google), portal 2FA, encrypted backups, a real privacy page, and any formal accessibility audit.

### Cited Findings
- Technician login: cookie auth with 8 h sliding expiry and a 14-day absolute cap. Every request re-validates the account, so deactivation or a role or password change takes effect at once. Sign-out deletes the server-side session record — [Program.cs:220-242](Program.cs); [Services/TechnicianSession.cs](Services/TechnicianSession.cs); [Services/HelpdeskStore.Sessions.cs](Services/HelpdeskStore.Sessions.cs); [docs/Technical-Guide.md:128-142](docs/Technical-Guide.md)
- Passwords: PBKDF2 with 210k iterations. Minimum 8 characters, common, all-number and name-based passwords refused, no composition rules. A password set by someone else must be changed on first use. Lockout after 5 per email or 30 per IP in 15 minutes, audited — [Services/PasswordHasher.cs](Services/PasswordHasher.cs); [Services/PasswordRules.cs](Services/PasswordRules.cs); [Services/SignInThrottle.cs](Services/SignInThrottle.cs); [mem:second-review-2026-09-27.md]
- 2FA: TOTP (RFC 6238) for technician accounts, with 10 recovery codes and replay protection. Can be required for all staff accounts (Settings > Sign-in security). Reset by Staff accounts: Edit; only an Administrator can reset an Administrator's. The DfE cyber security standard is cited as the reason. Not available for portal users — [Services/Totp.cs](Services/Totp.cs); [Services/HelpdeskStore.TwoFactor.cs](Services/HelpdeskStore.TwoFactor.cs); [docs/Technical-Guide.md:205-214](docs/Technical-Guide.md)
- Self-service reset for technicians: a 24-character recovery key, stored as SHA-256, throttled, can be switched off. Built 2026-09-29 (commits 1021f51, cc51763) — [Services/HelpdeskStore.RecoveryKeys.cs](Services/HelpdeskStore.RecoveryKeys.cs); [Pages/ForgotPassword.cshtml](Pages/ForgotPassword.cshtml); [docs/Technical-Guide.md:181-195](docs/Technical-Guide.md)
- SSO: none. No OpenIdConnect, Microsoft Account or Google authentication in code or packages. Deferred because it "needs the school's app registration" — code grep; [mem:system-review-2026-09-26.md] (stage F "Left out"); [mem:second-review-2026-09-27.md] (stage K)
- **Roles and permissions:**
  - fully custom roles, each with five independent ticks (Access, View, New, Edit, Delete) per module across 14 modules
  - 11 flags: Working as, Assign projects, 7 report flags, report export, raw database
  - Administrator is hardcoded and protected; only Administrators grant Administrator, and nobody can change their own role
  - seeded roles: Senior Technician, Technician, Junior Technician, Onboarding officer
  - Source: [Services/Modules.cs:8-98](Services/Modules.cs); [Models/HelpdeskModels.cs:39-74](Models/HelpdeskModels.cs); [Program.cs:49-149,249-268](Program.cs); [mem:role-permissions-requirements.md]
- **Audit:**
  - a before-and-after diff on every save goes into the append-only AuditLog, together with ticket, asset and part history
  - each line attributed to the technician, the portal requester's name, or System
  - filter by area, action, date and text; print view
  - the audit log is its own permission (for a DPO without Settings)
  - Source: [Services/AuditTracker.cs](Services/AuditTracker.cs); [docs/Technical-Guide.md:721-727](docs/Technical-Guide.md)
- Error log: warnings and errors to `<data>/logs/eduhelpdesk-yyyyMMdd.log`, kept 30 days, viewable at Settings > Error log. The error page shows a reference id. The developer page is shown only to loopback requests — [Services/FileLog.cs](Services/FileLog.cs); [Program.cs:279-290](Program.cs)
- **Backups:**
  - nightly (02:00 default) verified zip: SQLite online backup with an integrity check, plus attachments, logo, template and RESTORE.txt
  - keep 14 days (newest 3 always kept); configurable folder (a network share is recommended)
  - Overview warns when backups are off, failing or more than 2 days old
  - zips are **not encrypted**
  - a backup is taken before factory reset, retention and Spiceworks import
  - Source: [Services/HelpdeskStore.Backups.cs](Services/HelpdeskStore.Backups.cs); [Services/BackupScheduler.cs](Services/BackupScheduler.cs); [docs/Technical-Guide.md:766-787](docs/Technical-Guide.md)
- Health check (Settings > Database): database and attachment size and 30-day growth, free disk, median and slowest save time, biggest tables. Administrator-only Overview warnings (slow save over 1 s, disk under 2 GB) — [Services/HelpdeskStore.Health.cs](Services/HelpdeskStore.Health.cs); [docs/Technical-Guide.md:739-747](docs/Technical-Guide.md)
- Raw data page: every table, read-only connection, search, filter, CSV, a per-record "Raw data" view. Secrets are masked except for Administrators. Every look is audited. Gated by the `System.RawDatabase` flag (no seeded role has it) — [Services/RawDatabase.cs](Services/RawDatabase.cs); [docs/Technical-Guide.md:729-737](docs/Technical-Guide.md)
- **GDPR tools:**
  - subject-access export zip (report.html, data.json, files, README; audited)
  - retention rules in months: delete closed tickets, anonymise leavers to "Former staff member", trim audit log (at least 12 months); run nightly after a successful backup
  - leaver check
  - Notifications and Teams posts never contain ticket text
  - `Pages/Privacy.cshtml` is still the ASP.NET template placeholder ("Use this page to detail your site's privacy policy")
  - Source: [Services/SubjectAccessExport.cs](Services/SubjectAccessExport.cs); [Services/HelpdeskStore.Lifecycle.cs](Services/HelpdeskStore.Lifecycle.cs); [Pages/Settings/Retention.cshtml](Pages/Settings/Retention.cshtml); [docs/Technical-Guide.md:791-822](docs/Technical-Guide.md); [Pages/Privacy.cshtml:8](Pages/Privacy.cshtml)
- Web security headers:
  - CSP `script-src 'self'`, no inline script (enforced by a test), no framing, nosniff, same-origin referrer, no-store
  - attachments served with a sandbox CSP; Data Protection keys protected with DPAPI (machine scope)
  - 404 for unknown handlers; antiforgery on all forms
  - Source: [Services/SecurityHeaders.cs](Services/SecurityHeaders.cs); [Program.cs:161-197](Program.cs); [docs/Technical-Guide.md:82](docs/Technical-Guide.md)
- HTTPS: off by default. `EduHelpdesk:RequireHttps` gives a redirect, HSTS (365 days) and Secure cookies. The installer can use an existing certificate or make a self-signed one; `Enable-Https.cmd` converts a running install. A trust script, GPO and Intune instructions are written to the data folder. Settings shows an "Encrypted" badge or a warning — [Program.cs:171-181](Program.cs); [installer/Certificate.ps1](installer/Certificate.ps1); [installer/Enable-Https.ps1](installer/Enable-Https.ps1); [docs/Install-Guide.md:71-80](docs/Install-Guide.md)
- **Installer and hosting:**
  - self-contained win-x64 release zip from `deploy/New-Release.ps1`
  - `Install.cmd` asks for the data folder (refuses OneDrive/Dropbox), school name, colours, logo, backup folder and hour, HTTPS, port and firewall
  - registers the Windows service "EduHelpdesk" under `NT SERVICE\EduHelpdesk`
  - in-place upgrade with a backup first; optional unattended answers file
  - Windows 10/11 or Server 2016+
  - Source: [docs/Install-Guide.md:9-52](docs/Install-Guide.md); [installer/Install.ps1](installer/Install.ps1); [mem:school-installer-requirements.md]
- Planned Azure move (not built): App Service Linux B1, about £9.71 a month in UK South (price as of 2026-09-25), single instance. Prerequisites: configurable data path, Key Vault for keys, TZ=Europe/London, open fonts for the PDF — [mem:azure-hosting-plan.md]
- Database: SQLite only. MySQL 8 (installer-bundled, MySqlConnector) was interviewed 2026-09-30 and then **deferred** ("keep this as an option for later"). SQLite was future-proofed instead (delta saves plus WAL, raw page, health check) — [mem:mysql-migration-requirements.md]
- **Accessibility and UI:**
  - "Skip to content" link; `main` has tabindex; 113 aria or role attributes across 27 pages
  - menus are `<details>`/`<summary>` and work without JavaScript; the portal tile flow works without scripts
  - light, dark or match-device per browser
  - phone layout: menu folds below 980 px, list cards below 800 px, 16 px inputs
  - searchable pickers; printing always light
  - Source: [Pages/Shared/_Layout.cshtml:77,94,145](Pages/Shared/_Layout.cshtml); [docs/Technical-Guide.md:596-598](docs/Technical-Guide.md); code grep; [mem:portal-tile-launcher-requirements.md]

### Inferences
- The security posture fits a single-school on-premises deployment and maps to several DfE cyber-security expectations: MFA for staff accounts, HTTPS, backups, audit. The main shortfalls against DfE and cloud-first expectations are no SSO with the school's identity provider, unencrypted backups, and portal accounts without MFA.
- Accessibility work is practical (skip link, keyboard-friendly native controls, responsive layout) but no WCAG 2.2 AA audit or accessibility statement is recorded.

### Gaps
- No formal penetration test or WCAG audit is recorded.
- The owner's own live install was recorded (2026-09-27) as running `dotnet run` in Development from OneDrive with no DataPath set. Whether it has since moved to the installed service is not recorded in the notes.

## 8. Where docs and memory notes conflict with the current code

### Takeaway
The in-repo docs (README, the Technical Guide's roles and limitations sections, the Staff Guide) and some early memory notes lag behind the code. The report writer should rely on the code-backed facts above.

### Cited Findings
- Roles: README.md and the Technical Guide §4 "Roles" still describe "a name plus nine permission toggles" where any signed-in technician can do ticket work. The code has 14 modules × 5 independent ticks plus 11 flags (since 2026-09-24) — [README.md:21](README.md); [docs/Technical-Guide.md:150-166](docs/Technical-Guide.md) vs [Services/Modules.cs:33-94](Services/Modules.cs)
- The Technical Guide §17 "Known limitations" still says "No in-app notifications - Nothing tells a requester their ticket changed" and "No 'forgotten password' reset". The code has browser pings and pop-ups (including Notify requester) and technician recovery-key reset. Portal users still have no self-reset — [docs/Technical-Guide.md:833-834](docs/Technical-Guide.md) vs [Services/HelpdeskStore.Notifications.cs](Services/HelpdeskStore.Notifications.cs), [Services/HelpdeskStore.RecoveryKeys.cs](Services/HelpdeskStore.RecoveryKeys.cs)
- Staff-Guide.md (for requesters) still describes the old free-text form ("What's the problem?"), says "You do not set the priority yourself", and says the system sends no notifications and nothing alerts IT. The code has the tile launcher with a requester-chosen priority, plus pings and Teams posts — [docs/Staff-Guide.md:29-49,68-70,107](docs/Staff-Guide.md) vs [Pages/Portal/NewTicket.cshtml.cs:44,117-118](Pages/Portal/NewTicket.cshtml.cs)
- The Technical Guide §10 portal summary ("title, optional description, category, location") predates the tile launcher. §1 "At a glance" lists only two dependencies — [docs/Technical-Guide.md:24,616](docs/Technical-Guide.md)
- Finance report gating: the memory says it uses the Settings permission. The code uses a dedicated `Reports.Finance` flag — [mem:finance-asset-report-requirements.md] vs [Program.cs:125-127](Program.cs)
- Asset label: the memory says an "asset label" print view was added on 2026-09-22. The code's `PrintLabel` is a **ticket** label (number, title, date) reached from the ticket page. No asset label page exists — [mem:auth-roles-portals.md] vs [Pages/PrintLabel.cshtml](Pages/PrintLabel.cshtml); [Pages/Job.cshtml:31](Pages/Job.cshtml)
- The ticketing interview (2026-09-21) says "only technicians log them (no requester portal)". This was superseded by the portal (2026-09-22 onward) — [mem:ticketing-requirements.md]
- Loans and kits permissions: the memory says issuing is open to any signed-in technician and only kit editing is gated. The code now has separate Kits and Loans modules (Loans: New issues, Edit books back in) — [mem:loan-kits-requirements.md] vs [Program.cs:77-82](Program.cs)
- Onboarding: MEMORY.md says it is "not yet released". Git shows "Release 1.4" and "v1.4" commits after onboarding stage 5, so it shipped in 1.4 — [mem:MEMORY.md] vs git log (59885d1 then 7533b6a / 9942c79)
- Deploy scripts: the memory mentions `deploy/Publish.ps1` and `Install-Service.ps1`. Only `deploy/New-Release.ps1` remains, and installer/ replaces the rest. A `Program.cs:10` comment still points to `deploy/Install-Service.ps1` — [mem:second-review-2026-09-27.md] vs file listing
- Test count: memory 41 → code 156 test attributes — [mem:system-review-2026-09-26.md] vs code grep

### Inferences
- If competitors are compared using the shipped docs, EduHelpdesk will look weaker on notifications and permissions than it actually is.

### Gaps
- The `.docx` versions of the guides were not inspected. The memory says Technical-Guide.docx was not regenerated after the notifications work.

## 9. Summary lists: Built / Deferred or suggested but not built / Ruled out by owner

### Takeaway
Nearly everything the owner interviewed for has been built. The open roadmap is the reviewer-suggested stages I-K (routing, portal content, device picker, rating, QR labels and stocktake, M365/Google sign-in and sync), offboarding, term-date SLAs and the deferred MySQL/Azure moves. Email in every form was ruled out by the owner.

### Cited Findings

**Built (in code as of 2026-10-03)**
- Tickets:
  - server-side queues (Open, Mine, Unassigned, Overdue/due soon, Onboarding, All), search, filters, chips, sort, paging, CSV export
  - bulk status, technician, team, priority, category, type, comment, close-with-message and merge
  - Incident/Request types; templates (also as portal buttons); custom attributes scoped by category; internal notes; comment removal
  - attachments (whitelist, 10 MB) with per-file sharing to the requester; related and follow-up links; merge; parts consumption; project links
  - SLAs in minutes, hours, days, school work days or lesson periods, with pause-on-status; closing-message rules; 14-day reopen window
  - "Working as"; Word print template, label, job sheet
  - Spiceworks Cloud import (preview, repeat import with conflict flags, undo)
- Portal: separate email-and-password login with temporary passwords and quick start guides; tile launcher (catalogue items plus templates, icons, colours, set order, "Something else"); requester-chosen priority and location; up to 3 files; own tickets with "New reply" flags; replies; "Report it again"; portal projects for raisers and the lead; light/dark; notifications page.
- Notifications: opt-in browser pings and desktop pop-ups (new portal ticket, requester reply, Notify requester), Settings switches and reminder bar; Teams Workflows webhook (new portal ticket, overdue).
- Assets: lifecycle fields; statuses including On loan and Disposed; lifespan-based replacement; review list; list, filters and bulk; mapped CSV import; disposal with method and proceeds; custom attributes by type; ownership history.
- Loans and kits: named kits with swappable contents; kit and asset loans with required reasons; typed-in borrower for kits; repeat-borrower flag and printable line-manager report.
- Parts: reorder thresholds, low-stock surfaces, logged stock adjustments, several suppliers, compatible asset types, bulk tools and CSV export. Suppliers directory.
- Projects and quotes: portal-raised; lead assignment in the portal; items and sub-items; quote statuses and expiry; payment lines with VAT; spending bands; merged proposal PDF; report.
- Onboarding: checklist templates per staff type, task owners and time frames, portal-account and issue-asset tasks, auto-close, officer role, welcome-pack PDF, Overview card, report.
- Reports: Assets, Tickets, Parts, Loans, Finance (academic year, orders by PO or quote, disposals), Projects, Onboarding. Print-to-PDF and CSV, each behind a role flag.
- Security and admin:
  - PBKDF2, lockout, password rules, forced change; TOTP 2FA (can be required); technician recovery keys
  - server-side sessions; custom roles (5 ticks × 14 modules plus 11 flags); escalation guards
  - audit log with actor attribution; error log; CSP with no inline script; HTTPS option with a self-signed certificate generator
  - nightly verified backups; data folder relocation; factory reset; demo data and "Go live"
  - leaver check; subject-access export; retention and anonymisation; raw read-only DB viewer; DB health check
  - Windows service installer and upgrade; MIT release zip; delta saves plus WAL; responsive and dark UI; skip link
- Sources: sections 1-7 above.

**Deferred or suggested but not built**
- Stage I (suggested 2026-09-27): routing rules (portal tickets arrive with no team or technician); portal announcements/known issues and FAQ articles; requester picks their own device; satisfaction rating in the portal — [mem:second-review-2026-09-27.md]; confirmed absent in code (sections 2-3)
- Stage J: asset QR labels, scanning, bulk check-in/out and stocktake. The asset interview said "not wanted yet" — [mem:second-review-2026-09-27.md]; [mem:asset-management-requirements.md]
- Stage K: Microsoft 365/Google sign-in (portal and staff), Entra/Google directory sync, Intune/Google Admin device sync. Blocked on the school's app registration — [mem:second-review-2026-09-27.md]; [mem:system-review-2026-09-26.md]
- (Stage L, delta saves, **was built** on 2026-09-30 as the SQLite stage A) — [mem:saving-only-changes.md]
- Recurring tickets; loan acceptance or signature — [mem:second-review-2026-09-27.md]
- Offboarding as its own stage, joined to the leaver check — [mem:onboarding-requirements.md]
- SLA term dates, half terms and INSET days (offered, deferred); per-weekday timetables (declined "for now") — [mem:sla-school-time-requirements.md]
- MySQL 8 backend (five-stage plan; deferred 2026-09-30) and the Azure App Service move (planned, not built) — [mem:mysql-migration-requirements.md]; [mem:azure-hosting-plan.md]
- Smaller follow-ups:
  - VAT rates as a setting; real PO records
  - a per-reason "counts toward flagging" tick for loans; model-level part-to-asset linkage
  - catalogue sub-category on the technician `/NewTicket` page, a sub-category filter or report, per-item SLA or team
  - icons on button tiles
  - Sources: [mem:projects-quotes-requirements.md]; [mem:finance-asset-report-requirements.md]; [mem:loan-kits-requirements.md]; [mem:parts-inventory-requirements.md]; [mem:service-catalogue-portal-form.md]; [mem:portal-tile-launcher-requirements.md]
- Listed in the guide as not done (no stated decision): scheduled or emailed reports, approvals or change management, asset discovery or agents, multi-site tenancy, portal self-service password reset, portal 2FA, encrypted backups — [docs/Technical-Guide.md:826-843](docs/Technical-Guide.md); [docs/Technical-Guide.md:774](docs/Technical-Guide.md)
- Unverified items: real Teams delivery; real desktop toasts and sound; installer admin-only paths (LocalMachine certificate store, service stop/start/rollback) — [mem:teams-channel-webhook.md]; [mem:desktop-notifications-requirements.md]; [mem:school-installer-requirements.md]

**Ruled out by the owner (with stated reason where recorded)**
- **All automatic email**, email-to-ticket, CSV ticket import, a quick-add box, canned replies or mailto button, reopen-with-reason, first-response tracking, auto-close and reminders (2026-09-21).
  - Reasons: requester updates should be "nothing automatic"; the mail system was "unknown"; canned replies "nothing more needed"; the choices were deliberate.
  - The "keep requesters informed" pain point was to be met by queues, and later by pings.
  - Later reviews say "mention, don't push".
  - Source: [mem:ticketing-requirements.md]; [mem:second-review-2026-09-27.md]
- Time spent or time tracking on tickets (not chosen; "Not built, by choice"); queue tabs for "waiting" or "no update lately" (not chosen); "volume and trends" ticket report (not chosen); team or technician on templates (not chosen); ticket types with different categories, SLAs or statuses (types are list and report separation only) — [mem:ticketing-requirements.md]; [docs/Technical-Guide.md:837](docs/Technical-Guide.md)
- A formal asset retirement or disposal **workflow**: the owner chose a review list instead. Disposal was later added only as a status plus fields for the finance report. Printable labels, barcode, QR and stocktake were "not wanted yet" (2026-09-21) — [mem:asset-management-requirements.md]; [mem:finance-asset-report-requirements.md]
- Parts: unit cost and inventory value, blocking duplicate SKUs, parts CSV import, gating ticket-side part consumption — [mem:parts-inventory-requirements.md]
- Finance report: valuation or depreciation (it "could disagree with the finance team's own figure"); an "awaiting PO" chase list (the report stays descriptive); real PO entities (deferred) — [mem:finance-asset-report-requirements.md]
- Loans: free-text-only reasons (they can't be counted); a generated PDF or Word file for the loans report (print-to-PDF chosen); typed-in names for individual asset loans (an asset can't be traced back to a free-typed name) — [mem:loan-kits-requirements.md]
- Onboarding: portal requests for onboarding; showing onboarding in the portal (internal only); tracking keyfobs; contract end date — [mem:onboarding-requirements.md]
- Notifications: web push, service worker or SignalR; a header bell; detailed notification text (generic only, because classroom screens are projected); Teams posts for requester replies or staff-raised tickets; a Teams "detail level" option — [mem:desktop-notifications-requirements.md]; [mem:teams-channel-webhook.md]
- Portal launcher: search box, buttons-only mode, alphabetical or usage-based ordering, emoji or uploaded icons, merging templates and catalogue into one list — [mem:portal-tile-launcher-requirements.md]
- Data layer: a nightly in-memory-versus-database comparison (tests only chosen); MySQL now (deferred, "keep this as an option for later") — [mem:mysql-migration-requirements.md]
- Installer: asking for an admin account during install (the built-in default with a forced change was chosen); demo data is always included — [mem:school-installer-requirements.md]
- Roles: stacked permission levels (built, then rejected the same day; "Do not re-propose") — [mem:role-permissions-requirements.md]

### Inferences
- The owner's design philosophy is consistent: minimal friction for teachers, no email dependency (unknown or uncontrolled mail system), privacy-conscious generic notifications, and audit-friendly records. Recommendations that need email (email-to-ticket, emailed updates, CSAT by email) conflict with explicit owner decisions. No-email alternatives (portal and Teams) fit better.

### Gaps
- The reason for ruling out "first-response tracking", "auto-close/reminders" and "reopen-with-reason" is recorded only as a deliberate choice, with no fuller rationale.
- No recorded decision exists on multi-site or MAT support, satisfaction ratings or KB/FAQ beyond their appearing as reviewer suggestions.
