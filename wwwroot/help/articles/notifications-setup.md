---
title: Notifications for everyone
summary: Turning pings and pop-ups on or off for staff and requesters, and the reminder to switch them on.
area: Settings
kind: page
pages: /Settings/Notifications
needs: Settings:Edit
keywords: notifications ping pop-up popup desktop browser alert switch reminder https
order: 14
---
EduHelpdesk can **ping** and show a **pop-up** in the browser when a ticket arrives, a requester replies, or a technician updates a requester. Each person still chooses for themselves - staff from **Notifications** in their account menu, requesters on the portal home page. These switches sit above that.

## Who gets notifications

- **Notifications are available** - the master switch. Off means off for everyone.
- **Staff** - a ping when a requester submits a ticket or replies.
- **Requesters** - a ping in the staff portal when a technician ticks **Notify the requester** on a reply.
- **Remind people to switch them on** - a bar under the header after signing in, for anyone who hasn't. They can dismiss it for a week.

When a side is switched off, nothing is sent to it, browsers stop asking, and that side's Notifications page says so. Changes reach open pages within about half a minute.

## How it behaves

- Pings and pop-ups only work while a helpdesk or portal page is **open** in the browser.
- They never carry ticket details - only that something happened, and the ticket number.
- Pop-ups need the helpdesk to be reached over **https://**. On plain http:// the browser refuses them, and people get the ping and a banner in the page instead.
- Missed ones wait under the **bell** in the top bar.

Pings go only to people on the ticket's desk. See [Desks: IT, Site and Cleaning](desks#who-sees-what).
