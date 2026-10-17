---
title: Due dates and SLAs
summary: How a ticket's due date is worked out, what pauses the clock, and changing it by hand.
area: Tickets
kind: task
needs: Tickets
keywords: sla due date deadline overdue due soon pause clock on hold working hours periods override
order: 12
---
An **SLA** (service level agreement) is how long a ticket has, such as *4 working hours* for a high-priority fault. The ticket's **due date** is worked out from it when the ticket is logged.

## Where the SLA comes from

Your school sets up SLAs in **Settings → SLAs**, each for some priorities and categories. A new ticket gets the SLA that matches its priority and category, and you can choose a different one, or **No SLA** for no due date at all.

SLAs can count in:

- **minutes, hours or days** - plain clock time, nights and weekends included,
- **work days** - school days only, so a ticket logged on Friday afternoon isn't late by Sunday,
- **periods** - lessons, so *1 period* means by the end of the next full lesson.

School days and periods come from **Settings → School day and periods**. A desk can have working hours of its own, such as a site team that works 06:00 to 09:00 and 15:00 to 18:00; its minute and hour SLAs then count only inside those hours. See [Desks: IT, Site and Cleaning](desks#working-hours).

## Statuses that pause the clock

Some statuses stop the SLA clock while a ticket waits on someone else, such as *On Hold* while you wait for the requester or a part. Which ones is set in **Settings → Statuses**. While paused, the ticket shows **SLA paused** instead of a countdown and never counts as overdue. When the status changes back, the due date moves on by the time it was paused.

## Overdue and due soon

A ticket past its due date is **overdue**: a red bar in the list, a red badge on the ticket, and it appears under **Overdue / due soon**. It's **due soon** within the window set in **Settings → Ticket queues & closing** (an amber bar).

## Changing the due date

To set a date by hand, click the pencil beside **Due date** on the ticket (or fill in **Due date override** when logging it). It then shows as an **Override** and stays put. Without an override, changing the ticket's priority, category or SLA works the due date out again. To drop an override, clear the date and save, then choose the SLA again.
