---
title: Getting started: setting up EduHelpdesk
summary: For whoever runs the helpdesk - the order to set things up in, who can do what, and keeping the data safe.
area: Getting started
kind: start
pages: /Settings
needs: Settings:Edit
keywords: administrator admin setup configure first install school settings roles permissions backup
order: 2
---
Everything that shapes how EduHelpdesk works for your school is under **Settings**. Use the search box at the top of the Settings page to find a setting by name.

## A sensible order to set things up

1. **Branding & logo** - the school's name, colours and logo. See [Branding and logo](branding).
2. **Desks** - one per team that takes work (IT, Site, Cleaning). Each has its own categories, people and ticket numbers (IT-12, SITE-4). See [Desks](desks).
3. **Categories, statuses and priorities** - what tickets are about, the steps they move through, and how urgent they are. See [The simple lists](simple-lists) and [Statuses](statuses).
4. **SLAs** - how long tickets have, by priority and category. **School day and periods** sets the days and lessons they count. See [SLAs](slas) and [School day and periods](school-day).
5. **Rooms** and **departments**, in **Settings → Lists** - used on tickets, assets and cleaning rounds. Long lists can be imported; see [Importing from spreadsheets](imports).
6. **Roles** (in People) - what each kind of account can see and do. Then add **staff accounts** and give each a role. See [Roles and permissions](roles-and-permissions) and [Staff accounts](staff-accounts).
7. **Service catalogue** - the list of common problems staff pick from in the staff portal. See [The service catalogue](service-catalogue).
8. **Notifications**, **Teams channel** and **EduNotify** - how people hear about new tickets. See [Notifications for everyone](notifications-setup).
9. **Go live** - remove the demo data when you're ready. See [Going live and resetting](go-live-and-reset).

## Who can do what

Each role has five ticks per area - **Access, View, New, Edit, Delete** - plus extra ticks such as *See all desks*, *See and add job costs*, or one per report. A person only sees menus and buttons their role allows. **Administrator** can do everything and can't be changed.

> **Tip:** Make roles for jobs, not people: *Technician*, *Site staff*, *Cleaning supervisor*, *Business manager*. Then a new member of staff just needs the right role.

## Keeping the data safe

- **Backups** run every night. Check **Settings → Backups & data** now and then, and keep a copy somewhere other than the server's own drive.
- **The audit log** records every change to settings, accounts and records, and who made it.
- **Two-step sign-in** (a code from a phone app as well as the password) can be required for every helpdesk account in **Settings → Sign-in security**.

## The school's own help

You can add the school's own articles to this help centre (**Help → Add a school article**), and a note on any built-in article - who to ring for leaks, say, or how your school does something. Your notes stay when EduHelpdesk is updated.
