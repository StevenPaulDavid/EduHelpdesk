---
title: Going live and resetting
summary: Removing the demo data a new install starts with, emptying one part such as tickets or assets, or putting everything back to a fresh install.
area: Settings
kind: page
pages: /Settings/Reset
needs: Settings:Edit
keywords: go live demo data example remove reset factory erase start again clear empty tickets projects assets module
order: 21
---
## Go live

A new install starts with a worked example - a few demo tickets, people and assets marked *(demo)* - so it can be shown to people before it's used for real. The page lists them.

**Go live - remove demo data** removes those records and leaves an empty helpdesk for your own data. Anything you've added yourself is left alone, and so are the list values the example needed (departments, makes, part categories); delete any of those you don't want from their own Settings pages.

## Reset one part

Empties one part of the helpdesk and leaves everything else as it was - trial tickets before going live, say, or an asset import that went wrong. Open the part to see how many records it holds, what goes and what is kept.

| Part | Removes | Keeps |
|---|---|---|
| **Tickets** | Every ticket with its comments, attachments, time, costs and links; onboarding records; ticket alerts under the bell; the Spiceworks import history. Numbering starts again at 1 on each desk. | Templates, catalogue, categories, statuses, SLAs, recurring schedules, desks and parts stock. |
| **Projects** | Every project with its quotes and their files. Numbering starts again at PRJ-0001. | Purchasing requirements, spending bands, and contracts made from projects. |
| **Assets** | Every asset with its history and loan history, and every kit loan. | Kits (emptied), types, models, custom attributes and lists. Tickets and contracts lose the link to the asset. |
| **Onboarding** | Every onboarding with its new-starter ticket and tasks. | Templates, the welcome pack, and the starters in People. |
| **Parts** | Every part with its stock and stock history. | Part categories, locations and suppliers. |
| **Contracts** | Every contract on the register. | The contract lists and suppliers. |
| **Access** | Every system, access grant and termly review on the access register. | The access lists and the people. |
| **Facilities** | What was recorded on cleaning rounds, and premises check tickets. | The rounds (they count from today), cleaners and check schedules. |

- **Save a backup copy of the current data first** - leave this ticked, so it can be undone from the backup.
- Type the part's name (for example **Tickets**) to confirm, then press its **Reset** button.
- The reset is written to the audit log as one entry.

## Reset to factory settings

Puts the whole helpdesk back to how a new install starts: **every ticket, asset, person, project and setting is erased**.

- **Save a backup copy of the current data first** - leave this ticked, so it can be undone from the backup.
- **Also erase the audit log** - tick only if the log should go too.
- Type **DELETE** to confirm, then press **Erase everything and reset**.

> **Warning:** This can't be undone except by restoring a backup. It's meant for starting again after a trial, not for tidying up.
