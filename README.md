# EduHelpdesk

Technician-first education IT helpdesk for logging jobs, tracking school assets, and preserving the relationship between every job and the equipment it concerns.

## Run locally

```powershell
dotnet run
```

Open the local URL printed by ASP.NET Core. Data is stored in the SQLite database `App_Data/helpdesk.db`, which is created automatically on first run and should be backed up in production. Existing installations are migrated from `App_Data/helpdesk.json` the first time the application starts after this update.

The database uses separate tables for users, technicians and teams, departments, ticket options, assets, tickets, comments, ticket activity, branding, and migration metadata. Existing databases that used the earlier single-payload `Store` table are migrated automatically.

## Running it for real

`dotnet run` is for development. For the helpdesk people use every day, install it as a Windows service with the scripts in `deploy/` - `Publish.ps1`, then `Install-Service.ps1` from an administrator PowerShell. They move the data out of OneDrive, run it in Production mode, and restart it after a reboot or failure. See section 2 of `docs/Technical-Guide.md`.

## Signing in

Staff sign in with an email and password. **Roles are fully custom**, managed from the Roles panel on the **People** page: each role is a name plus nine permission toggles (Settings, manage roles, manage staff accounts, manage requesters, manage assets, manage suppliers, manage parts, delete/merge tickets, change "Working as"). Anyone signed in can already do the day-to-day ticket work (comment, assign, change status/priority/category, attachments, close) regardless of role - only the two destructive ticket actions are permission-gated.

**Administrator** is the one hardcoded, protected role: it always has every permission and can't be edited, renamed or deleted, guaranteeing there's always a way into the system. Every other role - including the three seeded on first run (Senior Technician, Technician, Junior Technician) - is an ordinary editable/deletable row; a role can't be deleted while a technician account still holds it.

On first run (or the first run after upgrading a database with no logins configured), an Administrator account is created automatically:

- Email: `admin@eduhelpdesk.local`
- Password: `ChangeMe123!`

Sign in and change this password immediately (you'll be prompted automatically). New staff accounts are created and assigned a role from **People → Add technician** (requires the "manage staff accounts" permission); a new account has no password until someone with access sets one there. Passwords are reset the same way - there's no self-service "forgot password" email flow yet.

Passwords must be at least 8 characters, and common or name-based passwords are refused. A password someone else set must be changed on first use; this holds on every page, not just straight after signing in. Five wrong passwords lock that email for 15 minutes, and thirty from one computer block that computer. Setting a new password unlocks the account at once, and lockouts are recorded in the audit log.

### HTTPS

The site starts as plain `http://`, and Settings → Sign-in security warns that passwords then cross the network unencrypted. Once the site has a certificate (in Kestrel, IIS or a reverse proxy), turn HTTPS on in `appsettings.json`:

```json
"EduHelpdesk": { "RequireHttps": true }
```

Plain-HTTP requests are then redirected, browsers are told to stay on HTTPS, and every cookie is marked Secure. Only do this once HTTPS works, or nobody can reach the site. Behind a proxy that handles HTTPS itself (Azure App Service, IIS ARR, nginx), also set the environment variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.

### Backups and the data folder

Backups run nightly - see **Settings → Backups & data** for the schedule, folder, retention and restore steps. The data lives in `App_Data` unless `"EduHelpdesk": { "DataPath": "C:\\EduHelpdeskData" }` points elsewhere. Keep it out of OneDrive and other synced folders. The first start with a new DataPath copies the data across. See `docs/Technical-Guide.md` section 15.

On the Tickets page, "Working as" (which technician you're logging work under) is preset to your own account. Only a role with the "change Working as" permission can change it to someone else - everyone else is always themselves.

## CSV imports

Users: `name,email,department,location`

Technicians: `name,email,team,role` (role is optional, defaults to Technician; must match the name of an existing role, seeded or custom). Imported technician accounts have no password until someone sets one.

## Staff portal

Anyone on the network can reach `/Portal`, and signs in with their own email and password - a separate, much lighter login than the technician one (no roles, no permissions, just "is this really them"). Once signed in, staff can report a problem, see the status of tickets they've submitted, and add a follow-up comment. It's deliberately minimal: no requester/technician/team/SLA/asset pickers, and staff can never see internal notes or another person's ticket.

New staff need a **People → Add user** entry with a password set before they can sign in (requires the "manage requesters" permission) - a user with no password set can't log in yet. Passwords are reset the same way; there's no self-service "forgot password" flow. They choose their own password the first time they sign in, and can change it from the portal home page. A portal sign-in lasts until the browser closes (or 2 hours idle), unless they tick "Keep me signed in", which lasts 30 days.
