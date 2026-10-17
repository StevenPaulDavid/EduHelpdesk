---
title: Backups and data
summary: The nightly backup, where it's kept, how long for, and how to restore one.
area: Settings
kind: page
pages: /Settings/Backups
needs: Settings:Edit
keywords: backup backups restore data folder nightly copy keep onedrive network share disaster recovery
order: 12
---
EduHelpdesk makes a copy of everything - tickets, assets, projects, people, attachments and settings - every night. Each backup is checked by reading the copied database back before it's kept.

## The latest backup

The top of the page shows when the last backup was made. **Back up now** makes one straight away, before a big change, say. If the last backup failed or is too old, Administrators see a warning on the Overview.

## Automatic backups

- **Make a backup automatically every night** - leave this on.
- **Backup folder** - leave blank for the default, next to the data. Another drive, a network share (`\\server\share\helpdesk`) or a OneDrive or SharePoint folder protects against this computer's disk failing.
- **Keep backups for** - from 7 days to a year. Older backups are deleted automatically, but the newest few are always kept, and only EduHelpdesk's own backup files are ever deleted.
- **Run at** - the hour it runs, when nobody's using it.

> **Important:** Backups hold everything, including personal data and password hashes. Keep the backup folder private, and make sure it isn't on the same drive as the data, or a disk failure loses both.

## Data folder

Shows where the live data is kept. It can be moved with the `EduHelpdesk:DataPath` setting in `appsettings.json`; **How to move it** gives the steps.

## Restoring

**How to restore from a backup** at the bottom of the page gives the steps for this installation. Restoring replaces everything with the backup's copy, so anything done since that backup is lost; make a fresh backup first.

Data retention rules only run once a backup has worked in the last day. See [Data retention](data-retention).
