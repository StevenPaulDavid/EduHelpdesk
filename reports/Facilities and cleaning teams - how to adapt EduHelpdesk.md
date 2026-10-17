# Facilities and cleaning can share EduHelpdesk if it gains "departments"

*Research and recommendations, 16 October 2026*

EduHelpdesk can serve the facilities and cleaning teams from the same install as IT. It should not be a second copy. The tools that do this well (Freshservice, Jira Service Management, HaloITSM and TOPdesk) all use the same pattern. Each department gets its own queue, settings and access. The departments share one staff directory and one portal, and one back end. EduHelpdesk already has most of the parts this needs: teams on tickets, SLAs set by category, recurring tickets, the portal tile launcher, a contracts register and an access register. What it lacks is a layer that **ties those parts to a department**. Today every account with ticket access sees every ticket. Every new portal ticket pings every technician. A portal ticket never gets a team. For a school this is more than an annoyance. IT tickets contain staff personal data and filtering and monitoring (KCSIE) matters, and cleaning staff should not see them. The recommendation is a new **Department** level above Team, built in five stages. Department routing and visibility come first, then notifications, then a phone-friendly job view with QR room codes, then planned premises checks with recorded readings, and last, cleaning rounds. The interim setup below works today with no code changes.

## How other systems separate departments

| Product | How a non-IT team is separated | What is shared | Notes |
|---|---|---|---|
| Freshservice | A **workspace** per team (IT, HR, Facilities). Each sets its own settings and controls access to its tickets | Requesters, back end; optionally one portal | Facilities staff can be cheaper "business agents". In IT workspaces they can view and add notes but cannot be assigned. A workspace field on the form routes the ticket. One user reported that a shared portal confused requesters |
| Jira Service Management | A **service project** per department, each with its own portal and request types | One "help center" listing all portals, one licence | Ships Facilities templates. One request type cannot appear in two portals, an open request for years |
| HaloITSM | Extra "service areas" managed with roles; separate queues per department | One system | Facilities module plans PAT tests and water checks. AO.com runs over 1,200 scheduled facilities tickets |
| TOPdesk | **Operator groups** with permission groups, so HR sees only HR incidents | One system | Sold as a single system for IT, facilities and HR |
| Every Compliance (IRIS), UK schools | IT and premises issues in one modular system | Site and IT | Site team logs all maintenance requests; recurring checks such as PAT are automated |

Sources: [Freshservice business teams](https://support.freshservice.com/support/solutions/articles/50000006095-enabling-workspaces-and-business-agent-license); [Freshservice multiple portals](https://support.freshservice.com/support/solutions/articles/50000012091-manage-multiple-requester-portals); [Freshworks community request](https://community.freshworks.com/ideas/separate-workspaces-for-different-support-teams-within-customer-portal-36414); [Atlassian service projects](https://support.atlassian.com/jira-service-management-cloud/docs/what-is-a-service-project/); [JSDCLOUD-15485](https://jira.atlassian.com/browse/JSDCLOUD-15485); [Halo Facilities Service Management](https://haloitsm.com/?p=3914); [TOPdesk tutorial](https://page.topdesk.com/en/tutorial-supporting-files-part-2); [Every Compliance G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369); [Hinderton School case study](https://www.iris.co.uk/case-study/how-hinderton-school-uses-every-compliance-to-streamline-maintenance-evidence-compliance-and-stay-audit-ready/).

The lessons that carry over:

1. **One portal, with the department as the first choice or implied by the tile.** Staff should not have to know which team owns a dripping tap or a broken projector bracket. Separate portals confused requesters at Freshservice.
2. **Separate visibility by default, with deliberate crossing.** Every product above restricts a department's staff to their own tickets. IT can still look at or move a ticket into Site when that has been granted.
3. **Settings per department, so the options lists don't get polluted.** Categories, SLAs, templates and statuses are owned by a department, so IT staff never wade through "Blocked toilet".
4. **Planned work matters as much as reactive work.** The FM tools all lead on scheduled checks with evidence, not just fault reports.

Price is not a reason to look elsewhere. Freshservice charges per business agent, and EduHelpdesk has no seat cap, so adding ten cleaners costs nothing.

## What facilities and cleaning actually need

**Facilities and site team.** DfE's Good Estate Management for Schools (GEMS) divides maintenance into planned preventative maintenance (PPM) and reactive work, with a suggested **70:30 budget split**. It expects schools to keep certificates and records of all statutory examinations, testing and remedial work, and a schedule of maintenance contracts covering PAT, gas safety, boilers and fire measures ([GEMS: maintaining the estate](https://www.gov.uk/guidance/good-estate-management-for-schools/maintaining-the-estate)). In practice the site team runs a fixed rhythm of logged checks ([caretaker job specification](https://teaching-vacancies.service.gov.uk/jobs/caretaker-fdd29cc4-767f-4aa1-b09a-c19683ee7c2c/documents/ed98b6db-c644-48c9-ba64-dd8418a1943e); [Wigan school legionella documents](https://www.saintdavids.wigan.sch.uk/serve_file/4452270)):

| How often | Typical check | What gets recorded |
|---|---|---|
| Weekly | Fire alarm call point test, rotating through call points | Which call point, pass or fail |
| Weekly | Flushing little-used water outlets (legionella, HSG 274) | Which outlets |
| Monthly | Hot and cold water temperatures at sentinel outlets | **A reading per outlet**: hot at least 50°C, cold below 20°C |
| Monthly | Emergency lighting, fire extinguishers | Pass or fail per item |
| Quarterly | Fire door inspection | Pass or fail per door, defects |
| Yearly | PAT, gas safety, boiler service, fixed wiring (5-yearly) | Contractor certificate |

Reactive jobs (leaks, broken blinds, heating faults, furniture) look just like IT tickets. They are tied to a **room**, not a device, and often go to a contractor with a quote.

**Cleaning team.** Cleaning is often **outsourced or part-time, works early mornings or evenings, and many cleaners have no school login or PC**. If the work is contracted, the school still has to keep a COSHH folder on site ([HSE COSHH FAQ](https://hse.gov.uk/coshh/faq.htm); [Judicium on COSHH](https://judiciumeducation.co.uk/news/Health-and-Safety-coshh-beyond-the-cleaning-cupboard-and-science-department); [Three Counties cleaning policy](https://files.schudio.com/three-counties-academy-trust/files/documents/ST3_School_Cleaning_Policy.pdf)). Their requests fall into three kinds:

- **Urgent call-outs** such as spills, vomit and broken glass. These need minutes, not days. Several institutions route them through the helpdesk with a severity-based response, while others say biohazards should be phoned in ([Marist](https://teamdynamix.marist.edu/TDClient/92/Portal/Requests/ServiceDet?ID=2084); [UNLV](https://help.unlv.edu/TDClient/63/BA-CSP-Training-Portal/Requests/ServiceDet?ID=620)).
- **Standard complaints**, such as a classroom not cleaned or bins not emptied. Victoria's school cleaning programme expects minor issues to be fixed by the next cleaning cycle ([Victoria DET](https://www2.education.vic.gov.au/pal/cleaning/guidance/cleaning-metropolitan-schools-resolving-issues-with-the-schools-cleaning)).
- **Scheduled rounds**: which rooms were cleaned, when and by whom, plus periodic deep cleans and audits.

Dedicated cleaning tools (CleanScan, OrangeQC, Janitorial Manager, AssetWhere) are built around **QR codes in each room**. Scanning one opens a report form with the room already filled in, with no app or login needed, and the same code works for cleaner check-ins ([CleanScan](https://www.getapp.ie/software/2094953/cleanscan); [OrangeQC](https://www.orangeqc.com/roles/janitorial-inspection-software/); [AssetWhere QR reporting](https://www.tes.com/for-schools/assetwhere/qr-hotspot-incident-reporting)).

## What EduHelpdesk already has, and what is missing

Checked against the code on 16 October 2026:

| Need | Today | Gap |
|---|---|---|
| Separate queues | Technicians have one **Team**; tickets have an optional team; the list filters by team; reports group by team | A team is only a label. Portal tickets get **no team**, so nothing routes |
| Separate visibility | `Tickets` permission is all-or-nothing (`Pages/Jobs.cshtml.cs:93`) | **Anyone with Tickets: View sees every ticket**, including safeguarding and filtering tickets |
| Separate notifications | New portal tickets and replies go to the "staff" audience: every signed-in technician. The Teams webhook is one channel | IT would be pinged for every spill, and cleaners for every password reset |
| Department-specific options | Categories, statuses, priorities and templates are single global lists | IT's category list fills with "Blocked toilet" and the like |
| SLAs | `SlaDefinition` already targets categories and priorities, with minutes, hours, work days and periods | **Already fits**: "Spill: 15 minutes" and "Heating: 1 work day" work today |
| Portal | Category tiles, catalogue items, location required | Fits. A "Site & cleaning" set of tiles works today |
| Planned checks | Recurring tickets with team, location, assets, school-day shifting and skip-while-open | Fits weekly and monthly checks. **There is no way to record readings per outlet** or produce a compliance log |
| Plant and certificates | Asset register (DfE fields, Building + Room), contracts register with renewal reminders, attachments | Fits boilers, extinguishers and contracts. Asset reports and lists would mix IT and plant |
| Contractor jobs with quotes | Suppliers, Projects with per-supplier quotes | Fits as is |
| Keys and fobs | Access control register covers "areas" | Fits. Site staff could own the area rows |
| New-starter tasks | Onboarding tasks have owners | Site tasks (keys, locker, lanyard) can already be owned by site staff |
| Phone use by cleaners | Responsive web, "On my way" button | No "today's jobs" phone view, no QR codes, and cleaners need a login they can actually use |

## Three ways to do it

1. **Set it up with teams only (works now, no code).** Add teams "Site" and "Cleaning", categories, catalogue items and SLAs. Give site staff a role with Tickets access. This is fine for a pilot, but everyone sees everything, every ping goes to everyone, and someone must set the team on each portal ticket by hand.
2. **A Department layer in the one install (recommended).** This matches every product above, keeps one directory, portal and audit log, and lets a ticket move between IT and Site with its history intact.
3. **A second copy of EduHelpdesk on another port.** This separates fully but doubles the staff directory, logins, backups and upgrades. It gives requesters two portals to choose between and cannot pass a ticket from IT to Site. Not recommended.

## Recommended build, in stages

Each stage is usable on its own. The order puts privacy and routing first, because they are what make a pilot safe.

**Stage 1: Departments and routing.**
- A Departments list in Settings, seeded with IT, which existing data falls into.
- Each **category belongs to a department**, so a portal tile routes the ticket automatically. Each **team belongs to a department**.
- Tickets carry a department. A "Move to department" action re-picks the category, keeps the history and clears a technician who isn't in the new department, just as changing team does now.
- The ticket list, saved views, reports and CSV export gain a Department filter. Each account opens on its own department.
- Shared ticket numbering. An optional prefix could come later.

**Stage 2: Visibility and notifications.**
- Ticket permissions apply **within the account's own departments**. A new flag, "See other departments' tickets", lets IT or an SLT user look across.
- New-ticket and reply pings, the bell and Teams posts go only to the ticket's department. Each department can have its own Teams channel webhook.
- Optionally, option lists per department (statuses, templates, catalogue) shown only to that department's staff.
- *This is the stage that needs an interview first.* It changes authorisation everywhere, as the role-permissions work did.

**Stage 3: Site and cleaning on a phone.**
- A "My jobs today" view with big buttons: On my way, Done, add a photo from the camera.
- **A printable QR code poster for each room.** It opens the staff portal with the room already chosen, and later leads into a cleaner's round.
- Simple accounts for cleaners or contractor supervisors: a short PIN or an emailed code, limited to their department. The existing technician 2FA and role tools apply.

**Stage 4: Planned premises checks with evidence.**
- Recurring tickets gain a **checklist of readings**: outlets with a temperature box and pass mark, or fire call points with pass or fail. A failed reading raises a follow-up job.
- A **Premises compliance log** report (fire, water, lighting and doors by month) for governors and insurers. Certificate attachments sit on contracts or assets.
- Asset types are marked as IT or plant, so asset reports can be filtered by department.

**Stage 5: Cleaning rounds.**
- Rooms grouped into rounds, with a room ticked off by scanning its QR code or tapping it in a list. **These are not tickets**: a school with 60 rooms would otherwise create 300 tickets a week.
- A "missed rooms" report and a spot-check audit score.
- Requests to clean a room ("not cleaned", "bins") attach to the round, and spills stay as urgent tickets.
- A COSHH product register (products, SDS attachment, assessment review date) could hang off Parts. It is optional and later.

## Questions to settle before building

1. Is cleaning **in-house or contracted**? Do cleaners have school Microsoft 365 accounts or school phones? This decides how they sign in.
2. Who leads facilities: a site manager, a business manager, or a trust estates team? Should **IT see facilities tickets**, and should the site manager see IT's?
3. Should staff see **one "Report a problem" screen** with site and cleaning tiles mixed in, or pick IT, Site or Cleaning first?
4. Are urgent spills to come through the helpdesk at all, or by radio or phone, with the ticket logged afterwards?
5. Which statutory checks does the site team do itself, and which does a contractor do? Do they already use the DfE compliance tracker or a contractor's portal?
6. Do facilities jobs need **costs or budget codes**, such as reactive spend against the 70:30 PPM split?
7. Should departments have their own ticket number prefix, statuses or working hours (cleaners working 6–9 am and 3–6 pm affects SLA clocks)?

## Interview answers (16 October 2026) and the revised plan

| Question | Answer |
|---|---|
| Who cleans | A mix of in-house cleaners and a contractor |
| Who signs in | Caretakers and other site staff get **their own logins**. Cleaning **supervisors** sign in, and cleaners get **printed sheets** |
| Visibility | Each department sees only its own tickets; leads can see across all of them |
| Facilities lead | The site manager |
| Portal | **Pick the department first** (IT / Site / Cleaning), then that department's tiles |
| Urgent cleaning | Called in by **radio or phone first**, with the ticket logged afterwards |
| Statutory checks | Mostly done by the site team; contractors do the yearly ones |
| Cleaning rounds | Yes. The supervisor ticks off rooms |
| Costs | **Cost plus a budget code** on site jobs |
| SLA hours | **Each department sets its own** working hours |
| Numbering | **A prefix per department** on one shared sequence |
| Membership | An account can be in **several departments** |
| Plant | Goes in the asset register, owned by Site and kept out of IT's lists and reports |
| Own lists per department | Categories and catalogue, templates, statuses, Teams channel |
| Build order | Departments and privacy first |

**The revised stages:**

1. **Departments and routing.**
   - A Departments list with a ticket prefix each, seeded with IT.
   - Accounts can be in several departments.
   - Categories, catalogue items, templates and statuses belong to a department.
   - The portal's Report a problem screen opens on a department choice.
   - Tickets carry a department and show a prefixed number on the shared sequence.
   - Tickets can be moved between departments.
   - A Department filter on lists, reports and CSV export.
2. **Privacy and notifications.**
   - Ticket permissions apply within the account's own departments, plus a "See all departments" flag for leads.
   - Pings, the bell and Teams posts go only to the ticket's department, with one Teams webhook per department.
3. **How site and cleaning work.**
   - Working hours per department for the SLA clock.
   - A quick "log a job that's already been dealt with" form for radio call-outs.
   - A printable daily job sheet per team for cleaning supervisors.
   - Cost and budget code (e.g. PPM or reactive) on jobs, with a spend report.
4. **Premises checks and plant.**
   - Recurring checks with readings and pass marks, where a failed reading raises a follow-up job.
   - A premises compliance log report.
   - Plant assets owned by Site.
5. **Cleaning rounds.**
   - Rooms grouped into rounds, with a printable round sheet.
   - The supervisor ticks rooms on screen.
   - A missed-rooms report.

Dropped from the first draft: PIN or code sign-in for individual cleaners, and the phone-first "My jobs today" view. Supervisors and caretakers use ordinary accounts. Room QR posters stay on the list as an idea, not a stage.

## What to do now, before any code

To start a pilot this week:

1. Settings → Teams: add **Site** and **Cleaning**.
2. Settings → Categories and Service catalogue: add site categories (Heating, Water and leaks, Electrical, Doors and locks, Furniture, Grounds) and cleaning categories (Spill or urgent clean, Room not cleaned, Toilets, Bins and waste). Tick them into the portal with their own colour and icon.
3. Settings → SLAs: give "Spill or urgent clean" a short SLA in minutes, and site categories a work-day SLA.
4. Recurring tickets: the weekly fire alarm test, weekly flushing and monthly water temperatures, each assigned to the Site team, with readings typed into the comment for now.
5. Create a "Site staff" role with Tickets Access/View/Edit only. Agree with the team to filter the list by their own team.

The limits are the ones described above: no privacy between teams, pings to everyone, and portal tickets needing a team set by hand. That is why stages 1 and 2 come first.
