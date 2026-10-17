---
title: Ticket queues and closing
summary: The due-soon window, how long a reply reopens a closed ticket, which tickets need a closing message, and the print template.
area: Settings
kind: page
pages: /Settings/TicketRules
needs: Settings:Edit
keywords: due soon window reopen replies closed closing message required print template word docx placeholders
order: 6
---
## Due soon

The **Overdue / due soon** queue and the overview show open tickets past their due date, and those due within this many **hours**. Overdue tickets are always included. A larger window gives more warning; a smaller one keeps the queue short.

## Replies to closed tickets

When a requester replies in the staff portal to a ticket closed within this many **days**, it reopens so the team see it. After that, the portal asks them to report it again as a new ticket, linked to the old one. **0** means a closed ticket never reopens.

## Closing requirements

Tick a **priority** or **category** to make technicians write a **closing message** before a matching ticket can be closed. The requester sees the message in the staff portal. Everything else can still be closed with one click. See [Closing a ticket](close-a-ticket).

## Ticket print template

To control how printed tickets look, upload a Word (**.docx**) document with placeholders in it. They're replaced with each ticket's details when it's printed:

| Placeholder | Becomes |
| --- | --- |
| `{{Job.Number}}`, `{{Job.Title}}`, `{{Job.Status}}` | The ticket's number, title and status |
| `{{Job.Comments}}` | Its replies (never internal notes) |
| `{{Requester.Name}}` | Who reported it |
| `{{Technician.Name}}` | Who it's assigned to |
| `{{Asset.Tag}}` | The linked asset's tag |

Without a template, tickets print in EduHelpdesk's own layout with the school's logo.
