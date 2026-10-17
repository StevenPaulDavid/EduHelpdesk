# EduHelpdesk

A helpdesk for schools, run on the school's own server. Technicians log and work tickets, and staff report problems through a simple portal. It also covers the jobs around the tickets:

- **Desks** - IT, Site and Cleaning share one helpdesk, each with its own categories, people, hours and ticket numbers.
- **Inventory** - assets, loan kits and loans, parts and suppliers, each linked to the tickets about it.
- **Site and cleaning** - premises checks with checklists, and cleaning rounds.
- **Projects** - purchasing requests from the portal, with supplier quotes and a printed proposal.
- **Onboarding** - one checklist per new starter, with tasks owned by IT, HR and others.
- **DfE registers** - assets, contracts and access control, with a compliance summary.
- **Reports**, recurring tickets, SLAs that count in the school day, desktop notifications, Teams and EduNotify.

## Help

Everything in the app is explained in its own **Help centre**. Sign in and press the **?** in the top bar, or go to `/Help`. Pages with a guide have a **? Help** link next to their title, and a **?** beside a field explains what to put in it. Staff using the portal have their own short help at `/Portal/Help`.

## Installing

Schools install EduHelpdesk from a release zip. Unzip it and double-click `Install.cmd`. It installs EduHelpdesk as a Windows service with its own .NET runtime, asks about the data folder, HTTPS, branding and backups, and upgrades an existing install in place. See [docs/Install-Guide.md](docs/Install-Guide.md).

To make the zip, run `deploy/New-Release.ps1`. See [docs/Release-Guide.md](docs/Release-Guide.md).

## Run locally

```powershell
dotnet run
```

This is for development. It opens on `https://localhost:5277`, and the repository's `appsettings.json` turns `RequireHttps` on, so the .NET development certificate must be trusted (`dotnet dev-certs https --trust`). Data goes in the SQLite database `App_Data/helpdesk.db`, which is made on first run with some demo data marked *(demo)*. **Settings → Go live & reset** removes the demo data. Older databases are upgraded automatically when the app starts.

## Signing in

On first run, or the first run on a database with no Administrator, this account is created:

- Email: `admin@eduhelpdesk.local`
- Password: `ChangeMe123!`

You must change the password when you first sign in. Then set up two-step sign-in and a recovery key, both in the menu under your name.

**Roles** are on the Roles tab of the **People** page. Each role has five ticks per area (Access, View, New, Edit, Delete), each independent of the others, for areas such as tickets, projects, onboarding, assets, loans, parts, contracts, access control, people, reports, settings and the audit log. Some roles also need extra on/off ticks, such as *See all desks*, *See and add job costs*, *Change "Working as"*, *Record cleaning rounds*, one per report, and *View raw database*. **Administrator** can do everything and can't be changed or deleted. Three ordinary roles are made on first run: Senior Technician, Technician and Junior Technician. See **Help → Roles and permissions**.

Staff accounts and portal users are added in **People**. A new account gets a temporary password to change at first sign-in, plus a quick start guide to print and hand over. **Give them a temporary password** resets a forgotten one. A technician with a recovery key can also reset their own password from the sign-in page, unless recovery keys are turned off in **Settings → Sign-in security**. Portal users have no self-service reset. See **Help → Sign-in security** and **Help → Staff accounts**.

## HTTPS

The installer sets up HTTPS when you ask it to. To switch over an install that runs on plain `http://`, run `Enable-Https.cmd` from the installer folder. Underneath, this is the `EduHelpdesk:RequireHttps` setting. When it's on, plain-HTTP requests are redirected, browsers are told to stay on HTTPS, and every cookie is marked Secure. Only turn it on once HTTPS works, or nobody can reach the site. Behind a proxy that handles HTTPS itself (Azure App Service, IIS ARR, nginx), also set the environment variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. See *HTTPS* in [docs/Install-Guide.md](docs/Install-Guide.md).

## Backups and the data folder

Backups run every night. **Settings → Backups & data** has the schedule, folder, retention and restore steps. Data is kept in `App_Data` unless `"EduHelpdesk": { "DataPath": "C:\\EduHelpdeskData" }` points somewhere else. The first start with a new, empty DataPath copies the data across. Keep the data out of OneDrive and other synced folders. See **Help → Backups and data**.
