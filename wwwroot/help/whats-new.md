## Next version (not released yet)

- **Help centre** - this help: Getting started guides, a guide to every page, step-by-step articles, a **?** beside fields that need explaining, a **Help** link by each page's title, and the school's own articles and notes. Staff using the portal get their own short help from the **? Help** in its top bar.
- **Cleaning rounds** - rooms grouped into rounds on chosen days, a printed sheet for the cleaners, ticking off rooms with spot-check scores, and a report of missed rooms. See [Getting started: cleaning rounds](getting-started-cleaning).
- **Premises checks** - recurring checks can have a checklist with readings (water temperatures, say). A ticket can't be closed until it's filled in, and failures raise a follow-up job. A new **Premises checks** report is the compliance log.
- **Plant** - each asset type belongs to a desk, so boilers and extinguishers are the Site desk's.
- **Site work** - each desk can have its own working hours for its SLAs, **Log a job already done** records quick fixes, a printable **daily sheet** lists open jobs by room, and jobs can have **costs** with budget codes and a **Job spend** report.
- **Desks kept apart** - people see only their own desks' tickets unless their role has *See all desks*, and alerts go to the right desk.
- **Ticket numbers per desk** - each desk counts its own tickets (IT-12, SITE-4).
- **Desk switcher and a tidier top bar** - choose the desk you're working on next to the school's name: tickets, assets, the daily sheet, the overview and **Log a ticket** follow it, and each desk chooses which parts of the bar it uses. The Facilities menu is gone, Inventory is now **Assets**, **Reports** is a menu with the compliance registers in it, and Settings is the gear beside **?**. See [Desks](desks).
- **Simpler setup** - SLAs can be edited; the service catalogue is the one place for staff-portal buttons (templates that were buttons have become catalogue items); requesters are imported from the staff list only, which can now give them temporary passwords; and *Working as* is gone - use the Technician filter to cover a colleague's tickets.

## 1.12

- **Desks** - IT, Site and Cleaning can share one helpdesk, each with its own categories, teams, ticket prefix and portal tile.
- **Recurring tickets** - log tickets automatically on a schedule: weekly, monthly, termly or yearly.

## 1.11

- **Ticket report** - print a whole ticket as a PDF, with its attachments, or a requester's copy without internal notes.
- Notifications say so when the browser has stopped allowing pop-ups.

## 1.10

- **EduNotify** - tell staff about an outage on their PCs from a ticket, and ping technicians about urgent tickets.
- **DfE registers** - asset checks, contracts register, access control register and a compliance summary.
- A refreshed look: full-width lists, one filter bar everywhere, and a tidier ticket page.

## 1.9

- **Notification bell** with an unread count.
- **On my way** button on tickets.

## 1.8

- **Teams channel** - post new portal tickets and overdue tickets to a Microsoft Teams channel.
- Sign-in security shows a green **Encrypted** badge when the helpdesk is reached over https.

## 1.6 and 1.7

- **Service catalogue** - staff report problems by tapping a category tile, then a common problem, with *Something else* for anything not listed. Tiles have icons, colours and a set order.
- **Notifications** - a ping and pop-up for technicians when a ticket arrives or a requester replies, and for requesters when a technician ticks **Notify the requester**. Settings can switch them off for everyone or one side, and remind people to switch them on.
- The installer can make a **self-signed HTTPS certificate**, and `Enable-Https` adds one to a running install.

## 1.5

- **Import from Spiceworks** - bring tickets, comments and people across from a Spiceworks export, import again while both are in use, and undo the last import.
- **Settings → Database** - every table as stored, read-only, behind its own permission, with a health check and warnings on the Overview.
- Saving is much faster: only what changed is written.
- Comments posted by mistake can be removed from tickets, assets, projects and onboardings.

## 1.4

- **Onboarding** - one checklist per new starter, with tasks owned by whoever does them, templates per kind of role, tasks that make the portal account or issue a laptop, a welcome pack PDF and an onboarding report.
- People and Onboarding sit under a new **HR** menu.

## 1.3

- **Recovery keys** - technicians can reset a forgotten password themselves.
- Projects are banded on the highest quote for each item, before any is chosen, and project amounts show excluding VAT by default.
- The project lead gets **View projects** in the staff portal, with the number waiting to be assigned.

## 1.2

- New accounts get a **temporary password** and a printable **quick start guide**, one at a time or for a whole import.

## 1.1

- The installer makes the helpdesk reachable from other PCs on the school network.

## 1.0

The first release for schools, with a one-script installer: tickets with SLAs (including work days and lesson periods), templates and reports; the asset register with loans, kits and the finance and audit report; parts; the staff portal; purchasing projects with quotes and a proposal PDF; custom roles; backups, data retention and subject access; two-step sign-in; and running as a Windows service.
