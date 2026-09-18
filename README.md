# EduHelpdesk

Technician-first education IT helpdesk for logging jobs, tracking school assets, and preserving the relationship between every job and the equipment it concerns.

## Run locally

```powershell
dotnet run
```

Open the local URL printed by ASP.NET Core. Data is stored in the SQLite database `App_Data/helpdesk.db`, which is created automatically on first run and should be backed up in production. Existing installations are migrated from `App_Data/helpdesk.json` the first time the application starts after this update.

The database uses separate tables for users, technicians and teams, departments, ticket options, assets, tickets, comments, ticket activity, branding, and migration metadata. Existing databases that used the earlier single-payload `Store` table are migrated automatically.

## CSV imports

Users: `name,email,department,location`

Technicians: `name,email,team`

The first version deliberately has no staff-facing portal. Technicians use the workspace to create jobs, assign ownership, link an asset, add assets, and import directory records.
