---
title: EduNotify
summary: Pairing with EduNotify, so tickets can put notices and pop-ups on staff PCs, alert technicians, and take technician calls from teachers' PCs.
area: Settings
kind: page
pages: /Settings/EduNotify
needs: Settings:Edit
keywords: edunotify pair pairing code notice outage staff pcs desktop message pop-up technician ping call on my way test link desk
order: 16
---
**EduNotify** shows messages on staff PCs. Linked to EduHelpdesk, it can:

- put an **outage notice** on staff PCs from a ticket (*Tell staff* on the ticket page), which comes down by itself when the ticket closes
- **pop up** ticket news for the person it's about, and **alert technicians**
- **ping technicians** when an urgent ticket comes in with nobody assigned
- take a teacher's **Call technician** as a ticket, assigned to whoever presses **On my way** first.

## Pairing

The two are linked with a one-time code; there are no keys to copy.

1. In EduNotify, open **Settings → EduHelpdesk** and press **Pair with EduHelpdesk**. It shows a code such as `4F7-K2Q`, which works once, for 15 minutes.
2. Here, enter **EduNotify's address** and the **code**.
3. Check the **staff email domain** (guessed from staff emails). A person's email there is taken as their Windows login, so `jsmith@school.org.uk` is `jsmith`: that's how pop-ups find people and how the first technician to press On my way is assigned.
4. Check **EduNotify reaches the helpdesk at**. EduNotify's server must be able to open it.
5. Press **Pair**.

Pairing checks both servers can reach each other before anything is saved. If one can't, it says which, and nothing changes. EduNotify makes the *Helpdesk notice*, *Helpdesk ping* and *Helpdesk pop-up* templates the helpdesk sends with; they can be restyled there.

After pairing, technician calls are accepted straight away, every reason going to the IT desk's general category. The desk PCs are guessed from EduNotify's groups (names with *technician* or *IT*, and *site* for Facilities), and every pop-up starts off.

## Link and tests

Once linked, the top of the page shows when each side last reached the other, and four tests:

- **Test the link**: both directions, and fetches EduNotify's templates, groups and call reasons.
- **Pop-up to my PC**: a test pop-up on whichever PC you're logged on to. Your email must be at the staff email domain.
- **Test technician call**: a TEST ticket made through EduNotify exactly as a teacher's call would be, assigned to you, resolved and removed again, with a tick for each step. Nothing shows on anyone's PC.
- **Desk PCs test**: a silent message to each desk's PCs, then how many received it.

## Pop-ups and alerts

Tick the ones you want, and with several desks, which desks they apply to:

- To the requester: *Ticket logged*, *Ticket assigned*, *Technician replied* (only with **Notify the requester** ticked), *Ticket resolved* and *Loan due back*.
- To technicians: *Assigned to you*, *SLA warning* (minutes before the due time), *SLA breached*, *Requester replied*, *Ticket reopened* and *Premises check failed*. On an unassigned ticket these go to the desk's PCs.

A pop-up says only the ticket number and what happened, never the ticket's words, because classroom screens get projected. With the helpdesk's address set under Sign-in security, it has an **Open ticket** button. A newer pop-up about the same ticket replaces the older one; someone who isn't logged on misses it. **Take pop-ups down after** removes whatever is still showing.

## Technician calls

**Accept calls**, **Assign on On my way** and **Take the call's alert down when resolved** are on after pairing. Calls go to the **category** and **priority** chosen here (the urgent priority when the teacher ticks Urgent). **Give reasons their own category** lists the reasons EduNotify offers, to send, say, *Printer* somewhere else. Call tickets never send the technician ping or the *Ticket logged* pop-up. A teacher EduNotify can't match is logged under *EduNotify technician calls*, with their name in the description.

## Desk PCs

**Desk PCs** are each desk's technicians' PCs, as EduNotify groups. The ping and alerts about unassigned tickets go to them. A desk with none gets no group alerts, so the site team's PCs never show an IT ticket.

## Technician ping

The **technician ping** sends the desk's PCs a notice when a ticket comes in at a ticked priority with nobody assigned. It says only the priority and ticket number, and comes down once someone is assigned, the ticket closes, or its priority drops.

## Notices showing now

Lists every ticket that has a notice on staff PCs, with **Recall** to take one down early. Sending a notice from a ticket also needs the *Tell staff through EduNotify* tick on the person's role.

## Advanced

The staff email domain, other templates for pop-ups and the ping than the ones EduNotify made, and **Unlink**, which ends the link on both sides (pair again to put it back).

An install that was linked by hand before pairing existed is unlinked once on upgrade, and the page asks you to pair again. Its pop-up, technician call and desk settings are kept.
