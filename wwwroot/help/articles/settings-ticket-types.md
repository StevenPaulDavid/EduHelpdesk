---
title: Ticket types
summary: Incident, Request and Disposal, and types of your own for each desk - for filtering the ticket list, the ticket report and templates.
area: Settings
kind: page
pages: /Settings/TicketTypes
needs: Settings:Edit
keywords: ticket type types incident request disposal planned reactive own custom label desk
order: 3
---
Every ticket has a **type**. Three are built in, on every desk, and can't be renamed or removed:

- **Incident** - something broken.
- **Request** - something being asked for.
- **Disposal** - getting rid of the linked assets; it disposes of them when the ticket is closed. See [Disposing of assets through a ticket](dispose-via-a-ticket).

## Your own types

Add types of your own in **Settings → Ticket types** - *Planned* and *Reactive* for site jobs, say. With more than one desk, choose the desk each is for: only that desk's tickets can have it, and the New ticket form shows it only for that desk.

Your own types are labels. They show on the ticket list (with a **Type** filter), in **Change type** on many tickets at once, in the ticket report's type filter, and in ticket templates and recurring tickets. They don't change how a ticket works.

- **Rename** changes the name on every ticket, template and recurring ticket that has it.
- **↑ ↓** sets the order they're listed in.
- **Desk** moves it to another desk. That's refused while open tickets on other desks have it.
- **Remove** takes it off the list. If anything has it, choose the type to move them to first - a built-in type or another on the same desk. The tickets' history notes the change.

A ticket moved to another desk (by changing its category) loses a type that desk doesn't have, and goes back to **Incident**.
