---
title: EduNotify
summary: Connecting EduNotify, so tickets can put outage notices on staff PCs and ping technicians about urgent tickets.
area: Settings
kind: page
pages: /Settings/EduNotify
needs: Settings:Edit
keywords: edunotify notice outage staff pcs desktop message api key technician ping urgent groups template
order: 16
---
**EduNotify** shows messages on staff PCs. Connected to EduHelpdesk, it can:

- put an **outage notice** on staff PCs from a ticket (*Tell staff* on the ticket page), which comes down by itself when the ticket closes, and
- **ping technicians** when an urgent ticket comes in with nobody assigned.

## Connection

1. In EduNotify, make an **API key** (the steps are at the bottom of the page).
2. Enter the **EduNotify address** and paste the key. For safety a saved key is never shown again.
3. Tick **Let tickets send notices** - the master switch. Off stops sending and recalling, and keeps the key.
4. Press **Test the connection**. It lists the templates the key may send and the groups of PCs, without showing anything on any PC.

## Technician ping

When a ticket comes in at a ticked **priority** with nobody assigned (or an open, unassigned ticket is moved up to one), a notice goes to the technicians' PCs. It comes down once someone is assigned, the ticket closes, or its priority drops. It says only the priority and ticket number.

Tick, for each desk, the **groups** of PCs to ping. A desk with no group ticked doesn't ping, so the site team's PCs never show an IT ticket. Choose the **template** the ping uses, and press **Save the ping**.

The groups ticked for each desk are that desk's PCs for every technician alert below, not only the ping.

## Person matching

Pop-ups go to one person on whichever PC they're logged on to. EduNotify knows people by their Windows login, so enter the **staff email domain**: `jsmith@school.org.uk` is taken as `jsmith`. People with an email anywhere else are never sent anything of their own. **Take pop-ups down after** removes whatever is still showing (8 hours unless changed).

## Requester pop-ups and technician alerts

One row per event, each with **On**, a **template** and, with several desks, the **desks** it applies to:

- To the requester: *Ticket logged*, *Ticket assigned*, *Technician replied* (only with **Notify the requester** ticked), *Ticket resolved* and *Loan due back*.
- To technicians: *Assigned to you*, *SLA warning* (minutes before the due time), *SLA breached*, *Requester replied*, *Ticket reopened* and *Premises check failed*. On an unassigned ticket these go to the desk's PCs.

A pop-up says only the ticket number and what happened, never the ticket's words, because classroom screens get projected. With the helpdesk's address set under Sign-in security, it has an **Open ticket** button: the portal for requesters, the ticket for technicians. In EduNotify, the key needs the helpdesk's address under **Links may open**. A newer pop-up about the same ticket replaces the older one; someone who isn't logged on misses it.

## Technician calls from EduNotify

EduNotify's **Call technician** (an on-call template with *Also raise a helpdesk ticket*) alerts the technicians' PCs and raises a ticket here.

1. Press **Make the incoming key** and paste it into EduNotify under **Settings → Integrations → EduHelpdesk**.
2. In EduNotify, tick **Can manage technician calls** on the helpdesk's API key, then **Test the connection** here, so the reasons are listed.
3. Choose each reason's **category** (which sets the desk), **priority** and **urgent priority**, and the ones for **unknown reasons**.
4. Tick **Accept calls from EduNotify**.

The first technician to press **On my way** is assigned (switch off **Assign…** to do it by hand), and resolving the ticket takes the call down. Call tickets never send the technician ping or the *Ticket logged* pop-up. A teacher EduNotify can't match is logged under *EduNotify technician calls*, with their name in the description.

## Notices showing now

Lists every ticket that has a notice on staff PCs, with **Recall** to take one down early.

Sending a notice from a ticket also needs the *Tell staff through EduNotify* tick on the person's role.
