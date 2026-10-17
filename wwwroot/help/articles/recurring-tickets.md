---
title: Recurring tickets
summary: Tickets logged automatically on a schedule - weekly checks, termly jobs, yearly renewals.
area: Settings
kind: page
pages: /Settings/RecurringTickets
needs: Settings:Edit
keywords: recurring repeating scheduled schedule weekly monthly termly yearly routine automatic log one now pause
order: 9
---
A **recurring ticket** logs a ticket by itself on a schedule: a weekly backup check, a termly clean of the projectors, a yearly licence renewal, or a premises check.

## The ticket it logs

- **Name** - what the schedule is called, and the ticket's title unless you give one.
- **Type**, **category**, **priority**, **location** - as on any ticket. The category decides its desk.
- **Requester** - who the ticket is for, often yourself or the team.
- **Team** and **technician** - who does it.
- **Assets** - optional asset tags, such as the server whose backup is checked.
- **Ticket title** and **description** - what to do. A checklist written in the description works well.

## The schedule

- **Repeats** - daily, weekly, monthly or yearly, and **Every** how many (every 2 weeks, every 3 months).
- **On** - which weekdays, or which day of the month (a date, or *the first Monday*). Days 29-31 fall on the last day of a shorter month.
- **At** - the time it's logged.
- **Starts** and **Ends** - every 2 weeks, 3 months and so on count from the start date. Ends is optional.
- **Move to the next school day** - ticked, a date that falls on a weekend or day off moves to the next school day. Unticked, it's logged on the day itself.
- **On** - untick to pause it. Switching it back on starts from then; nothing missed meanwhile is logged.

## How it behaves

**While the last ticket it logged is still open, the next date is skipped** rather than adding another, so an ignored check doesn't pile up. Each schedule shows when it last ran, its last ticket, and when it runs next.

**Log one now** logs a ticket straight away; the schedule carries on as it is. **Delete** stops it; tickets it already logged aren't affected.

**Checklist** gives its tickets lines to fill in, for premises checks. See [Setting up a premises check](set-up-premises-checks).
