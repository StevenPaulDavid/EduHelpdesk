---
title: Going live and resetting
summary: Removing the demo data a new install starts with, or putting everything back to a fresh install.
area: Settings
kind: page
pages: /Settings/Reset
needs: Settings:Edit
keywords: go live demo data example remove reset factory erase start again
order: 21
---
## Go live

A new install starts with a worked example - a few demo tickets, people and assets marked *(demo)* - so it can be shown to people before it's used for real. The page lists them.

**Go live - remove demo data** removes those records and leaves an empty helpdesk for your own data. Anything you've added yourself is left alone, and so are the list values the example needed (departments, makes, part categories); delete any of those you don't want from their own Settings pages.

## Reset to factory settings

Puts the whole helpdesk back to how a new install starts: **every ticket, asset, person, project and setting is erased**.

- **Save a backup copy of the current data first** - leave this ticked, so it can be undone from the backup.
- **Also erase the audit log** - tick only if the log should go too.
- Type **DELETE** to confirm, then press **Erase everything and reset**.

> **Warning:** This can't be undone except by restoring a backup. It's meant for starting again after a trial, not for tidying up.
