---
title: Disposing of assets through a ticket
summary: Holding assets as Awaiting disposal on a ticket while they wait to go, then disposing of them all when it's closed.
area: Assets and inventory
kind: task
needs: Assets.Dispose
keywords: dispose disposal awaiting recycle weee collection batch ticket close certificate contractor undo reopen
order: 6
---
When assets are going but haven't gone yet - a batch of old laptops waiting for the recycling company, say - put them on a ticket that disposes of them when it's closed. They wait as **Awaiting disposal**, the ticket keeps track of the job, and closing it records the disposal for every asset in one go.

Everything here needs the **Dispose of assets** tick on your role (**People → Roles**, under *Assets*). Anyone who can work tickets can fill in the details and close the ticket without disposing.

## Starting one

Any of these makes a ticket that disposes of its assets when it's closed:

- A ticket of type **Disposal**, chosen on the New ticket form, the ticket page or **Change type** on the ticket list.
- **Dispose via ticket** on an asset's page.
- **Dispose of them via one ticket** on the asset list, with the assets ticked (up to 100).
- **Dispose of them when closed** in the **Disposal** box on a ticket's **Assets** tab.
- The **Dispose of the linked assets when this ticket is closed** tick on the New ticket form.

The first three make it a **Disposal** ticket. A Disposal ticket always disposes of its assets when closed; to stop it, change its type, which puts the assets back. The tick on its own works on an incident or request too. Changing a ticket to or from Disposal needs the **Dispose of assets** tick.

## While it's open

- Each linked asset shows as **Awaiting disposal**. It can't be lent out, put in a kit, given to a student or issued to a new starter, and its page says which ticket it's waiting on.
- Linking another asset to the ticket adds it; unlinking one puts it back to the status it had. **Don't dispose of them** in the Disposal box puts them all back.
- Fill in the **Disposal** details as you get them: **how** (recycled, sold, donated...), who it's **disposed of by**, the **certificate** number and any **proceeds**. Press **Save details**.
- An asset already waiting on another disposal ticket stays with that one, and is marked **Waiting on** it.

## Closing it

Press **Mark as closed**. It asks what becomes of the assets:

- **Dispose and close** records each one as disposed of, with the date (today unless you change it) and the details from the ticket. An asset that's still out on loan or in a kit can't be, so it's named, put back to its own status and left for you to deal with.
- **Close without disposing** puts every asset back to the status it had.

The ticket gets an internal note of what happened to each asset. While assets are waiting, the ticket can't be closed any other way - from the ticket list, the board, the status box, a reply or the phone app - so the question is never skipped. Deleting the ticket puts them back too.

## If it's reopened

Reopening a ticket that disposed of assets asks whether to **undo the disposal**. Undoing it clears their disposal records and puts them back to **Awaiting disposal** on the ticket, ready to be closed again. Anyone who held them before isn't put back. You can also undo it later from the **Disposal** box while the ticket is open.

See also [Disposing of an asset](dispose-an-asset) for one asset at a time.
