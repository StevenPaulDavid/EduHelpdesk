---
title: Custom fields
summary: Adding your own fields to tickets (by category) and assets (by type).
area: Settings
kind: page
pages: /Settings/TicketAttributes, /Settings/AssetAttributes
needs: Settings:Edit
keywords: custom attributes fields extra details dropdown checkbox text date number category asset type
order: 10
---
When the built-in fields aren't enough, add your own - a *Room booked?* tick on room-move tickets, a *Toner colour* drop-down on printer tickets, a *Licence key* on software assets.

## Adding a field

- **Name** - what the field is called.
- **Field type** - a single line of text, several lines, a tick box, or a drop-down. Asset fields can also be a **date** (such as *Battery replaced*) or a **number** (such as *RAM in GB*).
- **Dropdown choices** - for a drop-down, the options, separated by commas.
- **Categories** (ticket fields) or **asset types** (asset fields) - which tickets or assets it appears on. A field only shows once one of those is chosen.

Ticket fields are on **Settings → Ticket custom attributes**; asset fields on **Settings → Custom asset attributes**.

## Where they appear

- On tickets, under **Additional details**, on the New ticket form and in the ticket's side panel.
- On ticket templates, which can set a default for each.
- In search: the ticket list's search looks in custom fields too, so a serial number typed into one can be found.

Asset fields can also be made by the asset import, from a spreadsheet column that matches nothing: see [Importing assets](import-assets).

**Change categories** on a field changes where it appears. Deleting a field removes it and its answers from every ticket or asset.
