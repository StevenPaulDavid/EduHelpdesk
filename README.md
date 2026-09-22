# EduHelpdesk

Technician-first education IT helpdesk for logging jobs, tracking school assets, and preserving the relationship between every job and the equipment it concerns.

## Run locally

```powershell
dotnet run
```

Open the local URL printed by ASP.NET Core. Data is stored in the SQLite database `App_Data/helpdesk.db`, which is created automatically on first run and should be backed up in production. Existing installations are migrated from `App_Data/helpdesk.json` the first time the application starts after this update.

The database uses separate tables for users, technicians and teams, departments, ticket options, assets, tickets, comments, ticket activity, branding, and migration metadata. Existing databases that used the earlier single-payload `Store` table are migrated automatically.

## Signing in

Staff sign in with an email and password. **Roles are fully custom**, managed from the Roles panel on the **People** page: each role is a name plus nine permission toggles (Settings, manage roles, manage staff accounts, manage requesters, manage assets, manage suppliers, manage parts, delete/merge tickets, change "Working as"). Anyone signed in can already do the day-to-day ticket work (comment, assign, change status/priority/category, attachments, close) regardless of role - only the two destructive ticket actions are permission-gated.

**Administrator** is the one hardcoded, protected role: it always has every permission and can't be edited, renamed or deleted, guaranteeing there's always a way into the system. Every other role - including the three seeded on first run (Senior Technician, Technician, Junior Technician) - is an ordinary editable/deletable row; a role can't be deleted while a technician account still holds it.

On first run (or the first run after upgrading a database with no logins configured), an Administrator account is created automatically:

- Email: `admin@eduhelpdesk.local`
- Password: `ChangeMe123!`

Sign in and change this password immediately (you'll be prompted automatically). New staff accounts are created and assigned a role from **People → Add technician** (requires the "manage staff accounts" permission); a new account has no password until someone with access sets one there. Passwords are reset the same way - there's no self-service "forgot password" email flow yet.

On the Tickets page, "Working as" (which technician you're logging work under) is preset to your own account. Only a role with the "change Working as" permission can change it to someone else - everyone else is always themselves.

## CSV imports

Users: `name,email,department,location`

Technicians: `name,email,team,role` (role is optional, defaults to Technician; must match the name of an existing role, seeded or custom). Imported technician accounts have no password until someone sets one.

The first version deliberately has no staff-facing portal. Technicians use the workspace to create jobs, assign ownership, link an asset, add assets, and import directory records.
