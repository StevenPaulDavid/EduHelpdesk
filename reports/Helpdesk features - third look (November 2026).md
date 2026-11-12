# Helpdesk features: third look (11 November 2026)

This follows the two earlier reports ([comparison and priorities](School%20helpdesk%20comparison%20and%20priorities.md), 6 October; [DfE processes](DfE%20guidance%20-%20helpdesk%20processes.md), 10 October). It does not repeat them. It covers what other helpdesks do that those reports did not look at, and what has changed in competitors since.

**The short version:** since October EduHelpdesk has closed most of the old gaps (desks and routing by desk, contracts, access control, compliance summary, recurring tickets, premises checks, help centre, phone app, device agent, dashboard). What other products have that it still lacks now falls into three groups: **small daily conveniences for technicians** (watching, snoozing, auto-assign, cover while away), **scanning** (every school-facing competitor now leads with it), and **booking** (equipment and rooms). None is large. The biggest unbuilt items are still the ones the earlier reports ranked first: Microsoft 365/Google sign-in and email-to-ticket.

## How this was done

I read the current feature and release pages of AssetTap (v3.7.0), Mojo Helpdesk, Zammad, TOPdesk, FMX, Freshservice, ManageEngine ServiceDesk Plus and HaloITSM, plus the newest EduGeek helpdesk threads, and checked each candidate against EduHelpdesk's help articles and code.

Limits:

- These are **vendors' own pages**, so they say a feature exists, not how well it works.
- **Incident IQ's site blocked me**; what I have on it is from search summaries only.
- Follett's and Halo's release pages gave only headlines.
- EduGeek: I read the search listing for October–November 2026, not the full threads.

## What changed in competitors since October

| Product | New since the last report | Source |
|---|---|---|
| AssetTap (closest UK rival, from £39/month) | **SmartCapture**: phone camera reads QR codes and barcodes to build asset records in delivery batches, with duplicate checks. **AI document import**: reads invoices and purchase orders into a review list. **Staff booking** of shared equipment. Notification snooze. "Ask a question about your data" assistant | [assettap.co.uk](https://assettap.co.uk/) |
| Mojo Helpdesk (popular with schools) | Asset scanning from anywhere in the app, draft autosave, weekly digests, ticket snooze, watch list, asset check-in/out with return reminders, projects. AI replies and summaries now **in every plan** | [mojohelpdesk.com](https://www.mojohelpdesk.com/), [blog](https://www.mojohelpdesk.com/blog) |
| HaloITSM | 2026 second release: log a ticket straight from an asset record, finer admin permissions, a custom help menu, more AI | [Halo release page](https://www.usehalo.com/product-updates/whats-new-in-haloitsm-2026-second-release) |
| Freshservice | Send a document for e-signature from a ticket, a document store for requests, status pages aimed at chosen audiences, surveys not tied to a ticket | [Freshservice updates](https://www.freshworks.com/freshservice/product-updates/) |
| Follett (US K-12) | Launched IT Asset Manager in January 2026; added a "date last accounted for" field and automatic barcodes | [Follett news](https://follettsoftware.com/news/follett-software-launches-it-asset-manager-to-transform-k-12-device-management/) |
| ManageEngine | AI agents and a connector so outside AI tools can work the helpdesk | [ManageEngine features](https://www.manageengine.com/products/service-desk/help-desk-features.html) |

Two things stand out:

- **Scanning is now the headline feature** of AssetTap, Mojo, FMX, TOPdesk and Follett. In October it was "common in school tools". EduHelpdesk prints labels but they carry no QR code, and nothing scans.
- **AI reply drafting and thread summaries are now standard**, including in mid-priced tools. In October they were a vendor push. I still found no UK school technicians asking for it, and one EduGeek poster in November complained about having to get past a supplier's "AI helpdesk".

One EduGeek thread from October is worth noting. A school using Freshworks for IT, facilities **and HR** asked for a replacement, and a reply praised ManageEngine for running several departments on one server. EduHelpdesk's desks now do exactly this. Another reply in the same thread valued staff being able to answer by email instead of opening the helpdesk, which is the email question again ([EduGeek search](https://www.edugeek.net/search/?q=helpdesk&sortby=newest)).

## Features not looked at before

Checked against EduHelpdesk today. "Missing" means I found nothing in the help articles or code.

### Daily work on tickets

| Feature | Who has it | EduHelpdesk today | Effort |
|---|---|---|---|
| **Watch a ticket** (get its updates without owning it) and **mention a colleague** in a note | Mojo, Zammad | Missing. The bell exists, so alerts have somewhere to go | Low |
| **Snooze** (hide a ticket until a date, then it comes back) | Mojo, AssetTap (for reminders) | Missing. *On Hold* pauses the SLA but nothing brings the ticket back | Low |
| **Automatic assignment**: a catalogue item sets the team or technician; otherwise take turns or give it to whoever has least | ManageEngine, Mojo, FMX, Incident IQ | Partly. Tickets reach the right desk, but the catalogue doesn't set a team or person | Low–medium |
| **Cover while away**: mark yourself out and your new tickets go to a named colleague | Zammad | Missing (*Working as* was removed) | Low |
| **Checklist on any ticket** or template | Zammad | Partly. Only premises checks and onboarding have them | Low (engine exists) |
| **Editing clash warning** and **draft autosave** on a half-written reply | Zammad, Mojo | Missing | Low |
| **Board view** (cards by status, drag between columns) | TOPdesk, ManageEngine | Missing | Medium |
| **Planner**: one calendar of tickets due, recurring checks, rounds and loans due back, by person | TOPdesk plan board, FMX calendar | Missing | Medium |
| **Likely duplicate** warning when logging ("3 open tickets for this room") | Zammad | Partly. Merge and link exist, but nothing warns at logging | Low |
| **Link straight to one comment** | Mojo | Missing | Very low |

### Staff portal

| Feature | Who has it | EduHelpdesk today | Effort |
|---|---|---|---|
| **Known problems shown while reporting**, with "this affects me too" | Freshservice status pages, Halo known issues | Missing (ranked 7 in October, still unbuilt) | Low |
| **Is it fixed? and a rating** on closed tickets | Mojo (5 stars on every ticket), Freshservice surveys | Missing (ranked 17 in October). The DfE IT support standard asks that staff can confirm a fix | Low |
| **Head of department view**: a named person sees their department's tickets | Mojo "group access rights" | Missing. Requesters see only their own | Low–medium |
| **Scan the label to report this device** | AssetTap staff QR portal | Missing. Needs QR on labels first | Low once QR labels exist |

### Assets

| Feature | Who has it | EduHelpdesk today | Effort |
|---|---|---|---|
| **QR on labels and scanning**: scan to open an asset, add a delivery in a batch, check a room off in a stocktake | AssetTap, Mojo, FMX, TOPdesk, Follett | Missing. The Android app and the QR library are already there, so the app is the natural scanner | Medium |
| **Book shared equipment ahead** (a trolley of laptops for period 3 next Tuesday) | AssetTap, TOPdesk reservations | Missing. Loans start today; there's no future booking | Medium |
| **Signed device agreement** when issuing a device | Freshservice (e-signature), Mojo | Missing (ranked 22 in October) | Low–medium |
| **Repair cost per device** and a "costing more than it's worth" flag | AssetTap, FMX | Partly. Job costs and a repeat-faults chart exist, but the asset page shows no running cost | Low |
| **Read an invoice or order into assets** with AI | AssetTap | Missing. Needs an AI service, so a privacy assessment and the school's own key | Medium, opt-in |

### Facilities desks

| Feature | Who has it | EduHelpdesk today | Effort |
|---|---|---|---|
| **Room and event booking**, where a booking raises the set-up job for the site team (chairs out, heating on) | FMX, Brightly Event Manager, Facilitron, TOPdesk | Missing | Medium–high |
| **Planned against reactive work** report, and **jobs open 15/30/60/90 days** | FMX | Missing as reports; the data is there | Low |
| **Estimated against actual hours** | FMX | Missing, and needs time recording, which you ruled out | - |
| Visitor sign-in, floor maps with equipment pinned | TOPdesk, FMX | Missing. Outside a helpdesk's job; not recommended | - |

## Suggested order

Ranked by how much a school team would feel it each day against how much work it is. Items from the earlier reports are included only where the market has moved.

1. **QR labels and scanning in the phone app.** Now the feature every school-facing rival leads with. Unlocks three things in one go: scan to open an asset, stocktake by room, and "scan to report this device" in the portal. You said "not wanted yet" in September; the phone app didn't exist then.
2. **Known problems banner with "me too".** Cheap, cuts duplicate tickets when the Wi-Fi is down, and ticks the DfE self-service expectation.
3. **Automatic assignment from the catalogue.** A catalogue item names a team or technician; anything else is shared out. Removes the triage step for portal tickets.
4. **Watch, mention and snooze.** Three small things on the existing bell. Snooze matters most for a team waiting on parts and suppliers.
5. **Is it fixed? and a rating.** Small, and gives the governors' annual review a satisfaction figure.
6. **Checklist on any ticket, cover while away, clash warning and draft autosave.** A tidy-up batch; each is a day or less.
7. **Planner and board views.** Useful for the site team, who now have recurring checks, rounds and jobs in three places.
8. **Equipment booking**, then **room and event booking** if the site team wants lettings and set-ups in the helpdesk. Ask them first; this is the largest item here.
9. **Facilities reports** (planned against reactive, aged jobs) and **repair cost on the asset page.**
10. **AI help** (invoice import, reply drafts, summaries). Now common, but still no demand from UK school technicians, and it needs a privacy assessment. Keep it off by default if built.

Still the two biggest gaps, unchanged from October: **Microsoft 365/Google sign-in** and **email-to-ticket** (which you ruled out; the October EduGeek thread is one more voice for it).

## Not worth copying

- Change, release and problem management, and a configuration database (Halo, ManageEngine, Freshservice): built for large IT departments.
- Live chat and WhatsApp intake: the DfE standard advises against instant messages for support requests.
- Visitor registration and floor maps.
- Canned replies and time recording: common everywhere, but you ruled both out.
