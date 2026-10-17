---
title: Linking, merging and follow-ups
summary: Connecting tickets about the same thing, merging duplicates, and starting a follow-up job.
area: Tickets
kind: task
needs: Tickets:Edit
keywords: link related merge duplicate follow-up follow up ticket combine
order: 14
---
## Linking related tickets

When two tickets are about the same problem (two people reporting the same broken printer, say) but both should stay open, link them:

1. Open one of the tickets.
2. In the side panel, under **Related tickets**, press **Link or follow up**.
3. Type the other ticket's number under **Link an existing ticket** and press **Link ticket**.

Each ticket then lists the other under Related tickets.

## Creating a follow-up

For more work that comes out of a job (a part to order and fit next week, say), press **Link or follow up** and then **Create follow-up ticket**. A new ticket called *Follow-up: ...* is logged with the same requester, assets, category, team and technician, linked to this one, so you can close this one now.

When a premises check is closed with failures, its follow-up job is created for you. See [Doing a premises check](premises-checks).

## Merging a duplicate

When someone has reported the same thing twice, merge the duplicate into the main ticket:

1. Open the duplicate.
2. Choose **More → Merge into another ticket…**.
3. Pick the ticket to keep, and press **Merge ticket**.

Its comments, linked assets and parts are copied into the ticket you kept, and the duplicate is closed. Merging needs permission to delete tickets, and can't be undone.

> **Note:** A premises check can't be merged, because its checklist would be lost.

To merge several at once, use **Merge into a ticket** on the Tickets page. See [Updating many tickets at once](update-many-tickets).
