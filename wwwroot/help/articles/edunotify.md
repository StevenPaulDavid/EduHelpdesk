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

## Notices showing now

Lists every ticket that has a notice on staff PCs, with **Recall** to take one down early.

Sending a notice from a ticket also needs the *Tell staff through EduNotify* tick on the person's role.
