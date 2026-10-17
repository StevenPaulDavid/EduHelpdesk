---
title: SLAs
summary: Setting how long tickets have - duration and unit, and which priorities and categories each SLA covers.
area: Settings
kind: page
pages: /Settings/Slas
needs: Settings:Edit
keywords: sla service level agreement target response time duration unit priority category work days periods
order: 4
---
An **SLA** sets how long a ticket has. EduHelpdesk works out each new ticket's due date from it. See [Due dates and SLAs](due-dates-and-slas) for how it looks on a ticket.

## Adding an SLA

![Adding an SLA](sla-form.png)

1. **Name** it, such as *High priority - 4 hours*, and add a **description** if it helps.
2. Set the **duration** and the **unit**.
3. Tick the **priorities** and/or **categories** it applies to. One SLA can cover several.
4. Press **Add SLA**.

## Units

| Unit | Counts |
| --- | --- |
| Minutes, Hours | Clock time, nights and weekends included - unless the ticket's desk has working hours of its own, when only those hours count |
| Days | Calendar days |
| Work days | School days only, skipping weekends and any day not ticked in School day and periods |
| Periods | Lessons, from the next one to start, skipping break, lunch and after school |

Work days and periods follow **Settings → School day and periods**. Set up the periods there before using a periods SLA.

## Which SLA a ticket gets

When a ticket is logged, EduHelpdesk looks for an SLA that lists its **category** first, then one that lists its **priority**. So a category SLA (*Safeguarding: 1 hour*) wins over a priority SLA (*Normal: 3 work days*). If none matches, the ticket has no SLA and no due date.

Anyone can still choose a different SLA, or none, on the ticket.

## Changing or deleting an SLA

Click an SLA in the list to change its name, duration, unit or what it applies to, then **Save SLA**. Tickets already logged keep the due dates they have; new tickets use the changed SLA. An SLA used by any ticket can't be deleted. Deleting one that a ticket template named sends that template back to working the SLA out from the priority and category.
