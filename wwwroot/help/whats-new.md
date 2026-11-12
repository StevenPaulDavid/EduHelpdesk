## Next version (not released yet)

- **Your own ticket types** - **Settings → Ticket types** adds types beside Incident, Request and Disposal, each on one desk (*Planned* and *Reactive* for site jobs, say). Only that desk's tickets can have them, and they show in the ticket list's filter, **Change type**, the ticket report, templates and recurring tickets. Rename, reorder, move to another desk, or remove one, moving what has it to another type. See [Ticket types](settings-ticket-types).
- **Disposal ticket type** - alongside Incident and Request, a **Disposal** ticket always disposes of its linked assets when it's closed. **Dispose via ticket** and **Dispose of them via one ticket** now make one, and the ticket list, filters and ticket report know the type. Changing a ticket to or from Disposal needs the **Dispose of assets** tick.
- **Dispose of assets through a ticket** - tick **Dispose of them when closed** on a ticket's Assets tab, or use **Dispose via ticket** on an asset or **Dispose of them via one ticket** on the asset list. The linked assets wait as **Awaiting disposal** (no loans, kits or handing out), the ticket keeps the method, contractor, certificate and proceeds as they come in, and **Mark as closed** asks whether to dispose of them or put them back. Reopening the ticket offers to undo it. Disposing now has its own **Dispose of assets** tick on roles, given on upgrade to every role that could delete assets. See [Disposing of assets through a ticket](dispose-via-a-ticket).
- **New ticket from an asset** - **New ticket** on an asset's page opens the form with the asset linked and you as the requester, and **Raise one ticket for them** on the asset list does the same for every asset you tick. See [The asset page](asset-page).
- **Today on the phone app** - version 1.4 of the app has a **Today** list beside Mine, Unassigned, Overdue and Open: the jobs planned for you today on the planner, and yours that slipped from earlier days. Tickets show their planned day on the list and on the ticket. See [The phone app](phone-app#today).
- **Planned jobs on the overview and the daily sheet** - the overview shows **Planned for today**, and warns when planned jobs weren't done on their day. The daily sheet can print one day's planned jobs, a page for each person, from the overview, the Tickets page's **More** menu or **Print** under a day on the planner; today's also carries jobs that slipped from earlier days. See [The daily sheet](daily-sheet#a-days-planned-jobs).
- **Other dates on the planner** - the planner now shows recurring tickets and premises checks still to be logged (in the technician's row), cleaning rounds, loans due back, new starters' first days, contract notice and renewal dates, and asset checks due. **Also show** turns each kind on or off. See [The ticket planner](ticket-planner#other-dates-on-the-planner).
- **Ticket planner** - **Planner** on the Tickets page puts tickets on the days they're planned for: a week with a row for each person, or a month's calendar. Drag a ticket onto a day to plan it - on the week, the row is who it's given to - and work through **Not planned** and **Planned day gone by** at the side. Only the helpdesk sees the plan. See [The ticket planner](ticket-planner).
- **Ticket board** - **List | Board** on the Tickets page shows the same tickets as cards in columns, by status or by technician. Drag a card, or use **Move** on it, to change its status or give it to someone; dropping it on **Closed** closes it, asking for a closing message when one is needed. The queue, search and filters work as they do on the list. See [The ticket board](ticket-board).
- **Planned for** - a ticket can be booked in for the day the team means to do it, from the pencil beside **Planned for** on the ticket or **Plan for a day** on many at once. The ticket list has a **Planned** column and a filter for today, this week, not planned and planned days gone by. Requesters never see it, and it leaves the due date and SLA alone. See [Planning a ticket for a day](plan-a-ticket).
- **New custom attributes from an import** - when a spreadsheet column matches no asset field or custom attribute, the asset import offers to make it one. The kind is guessed from the values, the asset types are those of the rows that use it, and you can change any of it before importing. Custom asset attributes can now also be a **date** or a **number**. See [Importing assets](import-assets).
- **Devices with students** - **Give to a student** on an asset's Ownership & loans tab records a device a pupil keeps, such as a 1:1 laptop, with just their name, so pupils don't need to be in People. The status becomes **With Student**, and the asset list (**Held by → With a student**), search and a new **With students** table on the asset report all show them. It isn't a loan, so it stays out of Loans and the loan report. An import row with the status **With Student** takes its Assigned to name as the student's. See [The asset page](asset-page).
- **Importing assets with no tag, and with a contract** - the asset import can give rows with no asset tag a built-in number (a prefix set in **Settings → Asset, part & loan rules** and a running number, such as AST-00001), matching them by serial number when the file is imported again. A **Contract** column links each asset to the contracts register by name, adding any contract that isn't there yet with just its name and marking it **Needs details** until it's filled in. See [Importing assets](import-assets).
- **Reset one part** - **Settings → Go live & reset** can now empty just tickets, projects, assets, onboarding, parts, contracts, access control or facilities history, without the factory reset's "everything". The set-up around each is kept, ticket and project numbers start again at 1, a backup is saved first, and you type the part's name to confirm. See [Going live and resetting](go-live-and-reset).
- **Compliance is back in the top bar** - the compliance summary, contracts register, access control and access review have their own **Compliance** menu after **Assets**, instead of sitting at the bottom of **Reports**. It shows while the compliance registers are on in **Settings → Features**; someone who can open only one of them gets a plain link.
- **Phone app alerts, app lock and updates** - the phone app now puts new tickets, replies and tickets given to you in the phone's notifications (only during the desk's hours, unless you choose otherwise), can lock itself with the phone's fingerprint or PIN - which an administrator can make compulsory in **Settings → Sign-in security** - and keeps ticket screens out of screenshots. Each release now brings the matching app, and paired phones offer to update. See [Phone app](phone-app).
- **Work a ticket from the phone app** - reply or add an internal note (with photos), change the status or priority, and close a ticket with a closing message, without opening the browser. The phone follows the ticket page's rules: the desk's statuses, the checklist before closing, and a closing message where Settings asks for one.
- **Phone app** - technicians can see their tickets, take them with Assign to me or On my way, and log new ones (or jobs already done, with photos) from the EduHelpdesk app for Android. It's off until an administrator turns it on in **Settings → Sign-in security**, works only over HTTPS, and only for roles with **Use the phone app** ticked - an upgraded helpdesk doesn't tick it for anyone. Each person pairs a phone from **Phones** in their account menu, and can revoke it there. See [Phone app](phone-app).
- **Assigned to you** - the bell now says when someone else gives you a ticket.
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
