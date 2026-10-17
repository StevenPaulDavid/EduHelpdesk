---
title: Updating many tickets at once
summary: Selecting tickets on the Tickets page and changing, closing, merging or exporting them together.
area: Tickets
kind: task
needs: Tickets:Edit
keywords: bulk select many several multiple tickets change status assign close merge export csv
order: 13
---
## Selecting tickets

On the Tickets page, tick the box at the start of each row. The box in the heading ticks every ticket on the page; you're then offered to select **every ticket that matches** the filters, not just this page.

## Choosing what to do

Under **With selected tickets**, choose an **Action**, fill in what it asks for, and press **Apply to selected**:

| Action | What it does |
| --- | --- |
| Change status | Sets a new status on every selected ticket |
| Assign technician | Assigns one person. A ticket whose team they're not in is skipped |
| Assign team | Moves them to a team. A technician not in that team is unassigned |
| Change priority, category or type | Sets the new value. A new category can move a ticket to another desk |
| Add a comment | The same comment on each, as a reply or an internal note |
| Close with a message | Closes them. Tickets that need a closing message are skipped if it's blank |
| Merge into a ticket | Copies comments, assets and parts into the ticket you name, and closes the selected ones. This can't be undone |
| Export to CSV | Downloads the selected tickets as a spreadsheet |

When it's done, a message says how many were changed and how many were skipped, and why.

> **Note:** Premises checks are skipped when closing in bulk until their checklist is filled in.
