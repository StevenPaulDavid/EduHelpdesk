# EduHelpdesk

Technician-first education IT helpdesk for logging jobs, tracking school assets, and preserving the relationship between every job and the equipment it concerns.

## Run locally

```powershell
dotnet run
```

Open the local URL printed by ASP.NET Core. Data is stored in the SQLite database `App_Data/helpdesk.db`, which is created automatically on first run and should be backed up in production. Existing installations are migrated from `App_Data/helpdesk.json` the first time the application starts after this update.

The database uses separate tables for users, technicians and teams, departments, ticket options, assets, tickets, comments, ticket activity, branding, and migration metadata. Existing databases that used the earlier single-payload `Store` table are migrated automatically.

## Signing in

Staff sign in with an email and password. Four roles: **Administrator** (full access, including Settings and staff account management), **Senior Technician** (identical to Administrator except no access to Settings), **Technician** (full ticket and asset control), **Junior Technician** (works all tickets, read-only on assets/suppliers/parts).

On first run (or the first run after upgrading a database with no logins configured), an Administrator account is created automatically:

- Email: `admin@eduhelpdesk.local`
- Password: `ChangeMe123!`

Sign in and change this password immediately (you'll be prompted automatically). New staff accounts are created and assigned a role from **People → Add technician** (Administrator or Senior Technician only); a new account has no password until someone with access sets one there. Passwords are reset the same way - there's no self-service "forgot password" email flow yet.

On the Tickets page, "Working as" (which technician you're logging work under) is preset to your own account. Only Administrator and Senior Technician can change it to someone else; Technician and Junior Technician are always themselves.

## CSV imports

Users: `name,email,department,location`

Technicians: `name,email,team,role` (role is optional, defaults to Technician; valid values are Administrator, Senior Technician, Technician, Junior Technician). Imported technician accounts have no password until someone sets one.

The first version deliberately has no staff-facing portal. Technicians use the workspace to create jobs, assign ownership, link an asset, add assets, and import directory records.
