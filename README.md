# EduHelpdesk

Technician-first education IT helpdesk for logging jobs, tracking school assets, and preserving the relationship between every job and the equipment it concerns.

## Run locally

```powershell
dotnet run
```

Open the local URL printed by ASP.NET Core. Data is stored in `App_Data/helpdesk.json`, which is created automatically on first run and should be backed up in production.

## CSV imports

Users: `name,email,department,location`

Technicians: `name,email,team`

The first version deliberately has no staff-facing portal. Technicians use the workspace to create jobs, assign ownership, link an asset, add assets, and import directory records.
