# International school IT helpdesk sentiment: what school IT staff and users praise, complain about and request (US K-12, Australia, Canada, etc.)

Research date: 2026-10-03. Scope: review sites, practitioner forums, public feature-request boards, CoSN/EdTech sources. Sources from before 2022 are marked **[OLD]**.

**How to read this:**
- Review and forum content is **opinion** and is paraphrased. Under the copyright rules that bind these notes, only one direct quote is used (in Q1).
- "Tally" figures are rough counts of mentions across the sources I read. Some per-page tallies for Capterra pages were produced by the fetch tool's summariser, so treat them as approximate.
- Several review samples are skewed by vendor-run review drives. For example, many Incident IQ G2 reviews dated 4-5 Jan 2024 are labelled "G2 invite on behalf of seller", and many Capterra reviews are dated June 2022.

**Access limits (affect coverage):**
- Reddit (r/k12sysadmin, r/sysadmin, r/ITManagers) was blocked to every tool I had: search, fetch and browser.
- The Incident IQ community idea board needs a login.
- G2's pros/cons tallies, TrustRadius (no Incident IQ listing) and Freshworks community pages returned 403s or login walls.
- To make up for this, Spiceworks Community (which is heavily used by US school-district IT staff) and GitHub issue trackers were used as the main forum and feature-request evidence.

---

## Q1. Review sites (G2, Capterra, GetApp, TrustRadius, Software Advice): top likes and dislikes of school/education helpdesks, with recurring theme tallies

### Takeaway
Education reviewers mostly praise **ease of use for teachers and technicians** and **tickets joined up with asset (1:1 device) records**. Incident IQ dominates the K-12-specific reviews.

The most repeated dislikes are:
- **poor search** (tickets and assets)
- **weak or inflexible reporting**
- **buggy mobile apps**
- **setup/migration effort**
- **price**. For free tools such as Spiceworks, price works the other way: it is the *main* reason they are chosen, and losing "free" triggers churn.

### Cited Findings

**Incident IQ (K-12-only product)**
- G2 rating and review profile:
  - 4.6/5 from 221 reviews. Star split: 167 five-star, 49 four-star, 4 three-star, 1 two-star, 0 one-star.
  - G2's AI summary says users praise ease of use and customisation, and that search could be better.
  - G2 user-insight averages: implementation time 2 months, return on investment 23 months, average discount 8%.
  - Integrations named by reviewers: ClassLink and Infinite Campus.
  - [G2 Incident IQ reviews](https://www.g2.com/products/incident-iq/reviews)
- G2 reviewer mix for Incident IQ: 38.4% Primary/Secondary Education, 21.8% Education Management, 22.7% IT Services. Company size: 51.8% mid-market, 33.9% enterprise. So this sample genuinely represents school districts. — [G2 Incident IQ vs Spiceworks](https://www.g2.com/compare/incident-iq-vs-spiceworks-cloud-help-desk)
- G2 sub-scores, Incident IQ vs Spiceworks Cloud Help Desk:

  | Measure | Incident IQ | Spiceworks CHD |
  |---|---|---|
  | Meets requirements | 9.0 | 8.6 |
  | Ease of use | 8.9 | 8.7 |
  | Ease of setup | 8.8 | 8.7 |
  | Ease of admin | 8.9 | 8.8 |
  | Quality of support | 9.1 | 8.4 |
  | Product direction | 9.3 | 8.1 |
  | Time to implement | 2 months | 1 month |
  | Return on investment | 23 months | 5 months |

  — [G2 compare](https://www.g2.com/compare/incident-iq-vs-spiceworks-cloud-help-desk)
- Capterra Incident IQ: 4.6/5 from 42 reviews. — [Capterra Incident IQ](https://capterra.com/p/186247/Incident-IQ/reviews/)
- Capterra Incident IQ page 1 (22 reviews, mostly 2020–2023, many June 2022), summariser tally:
  - Pros: ease of use 12, asset management/1:1 11, reporting 7, customer support 6, integrations 4, teacher/staff submission 3.
  - Cons: mobile app 4, setup complexity 3, search/filter limits 3, label printing 1, price 1.
  - Individual cons (opinions):
    - Mobile app buggy and feature work slow (Systems Support Technician, Mar 2023).
    - Notifications overwhelming (Sys Admin, Jul 2022).
    - Cannot edit a submitted description (Help Desk Support, Jun 2022).
    - Limited data export (Director, Nov 2020 [OLD]).
    - Search poor and vendor unresponsive to feedback (Assistant Technology Director, Mar 2026).
  - [Capterra Incident IQ reviews p1](https://capterra.com/p/186247/Incident-IQ/reviews/)
- Capterra Incident IQ page 2 (17 reviews, mostly 2020–2022), summariser tally:
  - Pros: ease of use 10, asset/inventory 8, integrations 3 (Google, Jamf, SIS), notifications 3, K-12 focus 3, support 2.
  - Cons: learning curve 3, search 2, limited customisation 2, migration difficulty 2, missing chat/ticket merging 2, navigation 2.
  - One sysadmin says extra clicks reduce staff adoption. A clerk says keyword search is unreliable.
  - [Capterra Incident IQ reviews p2](https://www.capterra.com/p/186247/Incident-IQ/reviews?page=2)
- Software Advice summary of Incident IQ reviews:
  - Label printing only works with thermal printers; users want Avery-style sheet labels.
  - Some say the mobile app is very buggy, and that updates removed features for the sake of simplicity.
  - [Software Advice Incident IQ](https://www.softwareadvice.com/cafm/incidentiq-profile/) (from a search-result summary, not a full page read)
- G2 Incident IQ individual reviews (opinions):
  - **Rick P., Assistant Technology Director, 9 Mar 2026, 3/5:** "The ticket search engine is effectively useless." He also says idea-portal complaints about search were ignored, and that a promised January fix slipped because the vendor prioritised a more advanced feature.
  - **Verified Education Management user, 31 Mar 2026, 2/5:** clunky, not intuitive, no spell-check.
  - **Angelene S., 25 Mar 2026, 4.5/5:**
    - Likes that staff *and parents* can raise tickets with student and device details.
    - Dislikes that only 5 quick-menu buttons are allowed, and that migration took weeks of back-and-forth.
  - **Andrew L., IT Systems Administrator, Feb 2023:** wants attachment preview, @-tagging a tech without making them follow the ticket, and a global keyword search across all fields.
  - **Luis L., Jan 2024:**
    - Asset search only works on tag/serial, not friendly name.
    - Likes that the knowledge base lets teachers self-solve, and reports showing which device models break most.
  - **Niki S., Jan 2024:**
    - Cannot check an asset out to a location or as a shared device.
    - One system replaced SchoolDude + Spiceworks + a separate paid asset site + paper policies.
  - **Ron B., 1:1 Coordinator, Jan 2024:**
    - Valued guest ticket portals, student repair-fee tracking, device disable via Google, SIS/SSO sync, and a paid add-on for Google password resets.
    - Wants an idea-voting board to get niche features prioritised.
  - **Cris W., district of 12,000+, Jan 2024:** least favourite parts are the fees tracker, rules simulation and knowledge base. Praises the vendor for listening.
  - **Stacey C., Jan 2024:** the main con is staff resistance to change, which needs training.
  - **Ryan C., Jan 2024:** values SLA tracking, bulk updates via rules, and trend analysis of tickets, parts and labour. Hard to tell whether a requested feature has shipped.
  - Vendor reply on G2 links idea threads "a better keyword search would be a huge improvement" (#802) and "tagging other techs in tickets" (#477). This confirms these as standing community requests.
  - [G2 Incident IQ reviews](https://www.g2.com/products/incident-iq/reviews)

**Spiceworks Cloud Help Desk (very common in small US districts)**
- G2 rating: 4.3/5 from 311 reviews. Reviewers are mostly non-education: Education Management 8.5%, Higher Education 4.9%. — [G2 compare](https://www.g2.com/compare/incident-iq-vs-spiceworks-cloud-help-desk)
- Capterra Spiceworks page 2 (25 reviews, 2019–2026), summariser tally:
  - Pros: free/price 11, ease of use 10, quick setup 6, cloud access 5, email-to-ticket 4, built-in inventory 3, customisation 3, mobile 2.
  - Cons: limited reporting 7, limited customisation (e.g. cannot make multiple form templates) 6, ads 3, features lost in the cloud migration 3, mobile 2, no modern SSO 2, knowledge base 2.
  - Education reviewers (opinions):
    - IT Specialist, Education Management, Sep 2025: the mobile app is useful when walking a campus taking requests.
    - ICT Coordinator, Feb 2026: free matters for schools in poorer countries.
    - IT Support Technician, Primary/Secondary, May 2020 [OLD]: walk-up requests cut by about 50%.
    - Professor, Nov 2025: uses it to teach help-desk skills.
  - [Capterra Spiceworks p2](https://www.capterra.com/p/102709/Spiceworks-IT-Help-Desk/reviews/?page=2)

**SolarWinds Web Help Desk (long-standing in K-12; self-hosted)**
- Capterra (25 reviews, mostly 2017–2019 [OLD], a few 2022–2026), summariser tally:
  - Pros: ticketing 12, customisation 10, ease of use 9, asset management 8, affordability 6, reporting 6.
  - Cons: outdated UI 11, slow development 8, asset-module issues 7, limited integrations 6, setup complexity 6, performance/memory leaks 5, pricing 5, Java/self-hosting burden 3.
  - Recent reviews: interface now feels dated (Leisure/Travel, Jun 2026); hard setup, no cloud option, steep price (May 2024).
  - Education reviewers are few and old: an Education Management reviewer in 2018 called it too complicated and cumbersome.
  - [Capterra Web Help Desk](https://www.capterra.com/p/179421/Web-Help-Desk/reviews/)
- G2/aggregator summary: users like the customisation and the K-12 fit for structured ticketing with knowledge-base links. The interface is called clunky and updates "sporadic". — [G2 WHD](https://www.g2.com/products/solarwinds-web-help-desk/reviews) (search-result summary)
- A school-district tech compared Jitbit and WHD **[OLD, Jan 2019]**: Jitbit was much cheaper and easier to use than WHD. — [Spiceworks Community search results](https://community.spiceworks.com/search?q=school%20district%20help%20desk%20ticketing&order=latest)

**Freshservice / Zendesk / others**
- Freshservice is G2's top-listed alternative to Incident IQ: 4.6/5 from 1,359 reviews. — [G2 Incident IQ](https://www.g2.com/products/incident-iq/reviews)
- In an **[OLD, 2015]** Jamf Nation K-12 thread, the deciding features were:
  - Freshservice: Google for Education user import, multi-department use, easy UI.
  - Web Help Desk: hooks into Casper/SCCM.
  - Zendesk: easy and customer-focused.
  - [Jamf Nation thread](https://community.jamf.com/t5/jamf-pro/ot-work-ticketing-help-desk-software-for-k-12-environment/m-p/94132)

### Inferences

#### Cross-source theme tally (approximate mentions; reviews + forum posts + feature-board items read for these notes)

| Rank | Theme | Approx. mentions | Direction / notes |
|---|---|---|---|
| 1 | Ease of use / simplicity for teachers and techs | ~45 praise, ~17 complaint | Most-praised attribute. Complaints are mostly "dated UI" (WHD) and "clunky / learning curve" (iiQ) |
| 2 | Price / free / budget fit | ~40 | Spiceworks free = top pro (11). About 25 negative posts when Spiceworks started charging. Budget-cycle fit (annual PO, tax-exempt, notice) about 8 |
| 3 | Asset / 1:1 device records linked to tickets | ~35 praise, ~10 gaps | Gaps: checkout to location/shared device, search by friendly name, label printing, parent/child locations |
| 4 | Reporting / analytics | ~16 praise, ~13 complaint | Wanted: simple built-in, per-school, scheduled, audit-ready. Rejected: "go use Power BI" |
| 5 | Integrations & SSO (Google, Entra/Azure AD, SIS, MDM) | ~20+ (plus high-vote board items) | Most-discussed Spiceworks request. Top-5 on Snipe-IT (OIDC/SAML). Email modern-auth on osTicket |
| 6 | Customisation / workflow (mandatory fields, categories, sub-tickets, checklists, rules, status options) | ~18 | Mostly Spiceworks complaints, plus iiQ menu/field limits |
| 7 | Search (tickets and assets) | ~14 complaint | Small in volume but the sharpest anger, in both paid (iiQ) and free (Spiceworks) tools |
| 8 | Vendor support & listening / development pace | ~25 mixed | iiQ support praised; WHD and Spiceworks "ignored for a decade" |
| 9 | Setup / migration effort | ~12 | iiQ 2 months vs Spiceworks 1 month to implement |
| 10 | Mobile app quality | ~11 (mostly negative) | Bugs, keyboard covering the comment box, ads, broken notifications |
| 11 | Self-hosting / on-prem wanted | ~11 | Spiceworks refugees; privacy (HIPAA/PHI) cited |
| 12 | Location/school-based routing, filtering, per-building notifications | ~8 (plus Snipe-IT 53 votes for parent/child locations) | Multi-site district need |
| 13 | Knowledge base / self-help | ~6 | Valued when teachers actually use it. Often weak |
| 14 | Multi-department (IT + facilities/maintenance) | ~6 | Districts separate or merge queues |
| 15 | Requester visibility (log in, see own history) | ~4 | See Q4 |
| 16 | Ads in free tools | ~6 | Spiceworks |
| 17 | AI features | ~0 in reviews | Vendor-driven. See Q6 |

- For a UK self-hosted tool this suggests:
  - Ease of use, ticket↔asset linkage, good search and simple built-in reports are the "hygiene" items that drive satisfaction.
  - Price and self-hosting are strong differentiators, because school IT teams are price- and budget-cycle-sensitive.
- Incident IQ's high scores come from a K-12-specific design: SIS/Google/MDM sync, guest/parent portals, repair fees. Its loudest 2026 complaints, though, are basic: search, spell-check, clunkiness. This suggests incumbents are vulnerable on fundamentals.

### Gaps
- Education-filtered review sets were not obtainable for these products, so findings rest on general samples: Freshservice, Zendesk, ManageEngine, TeamDynamix, Jitbit, osTicket, HaloITSM, TOPdesk.
  - G2 industry filters needed login.
  - TrustRadius has no Incident IQ page (404).
  - GetApp and TrustRadius were not fetched successfully.
- G2's own "pros and cons" theme counts for Incident IQ are behind login.
- Mention counts are my approximate tallies, and some per-page counts came from an automated summariser. They are not exact.

---

## Q2. Practitioner forums (Reddit r/k12sysadmin, r/sysadmin, Spiceworks Community): which tools are recommended, which features decide it, what do people regret?

### Takeaway
**Reddit could not be accessed at all**, so this section relies on Spiceworks Community, where many US school districts run Spiceworks Cloud Help Desk.

The strongest 2025–2026 signal is **regret and churn after Spiceworks introduced per-seat charges** (April 2025):
- Districts objected to unbudgeted mid-year costs, monthly-only billing and no tax-exempt route.
- Many said the cloud version lost features compared with the old on-prem edition.

Recurring "decider" features:
- usable search
- reporting
- SSO with Google/Entra
- mandatory fields/categories
- checklists
- per-school routing
- a mobile app that works
- self-hosting

### Cited Findings

**Reddit**
- Reddit is not accessible: the search tool rejected reddit.com, and both the fetch tool and the browser pane blocked it. No r/k12sysadmin or r/sysadmin thread content could be verified. — (tool restriction; no URL)

**Spiceworks Cloud Help Desk paid tier announcement (21 Apr 2025)**
- What changed: organisations with 6+ owner/admin/manager/tech seats must pay $6/seat/month from 1 June 2025 (originally 1 July). Organisations with 5 or fewer seats stay free with ads. The Premium tier adds bulk actions, repeatable task lists and no ads. MFA was added to the free Core tier. — [Spiceworks announcement thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- **Reaction tally (my reading of the first ~100 posts, opinions):**
  - About 25+ posts negative or saying they will leave or evaluate alternatives.
  - About 6–8 willing to pay, some only if bugs are fixed.
  - The most-upvoted reply (89 "spice ups") laments the end of "free forever" and asks for an on-prem paid version.
  - [same thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- **School-specific reactions in that thread (opinions):**
  - A school-district IT manager with 6 users:
    - Hadn't budgeted for it, and the switch fell at fiscal year end.
    - Thought the price fair, at about half the nearest comparable cloud helpdesk.
    - Found no tax-exempt purchase option.
    - Had created a fake "Status Pending" user because the product lacks a pending status.
  - A school using Spiceworks as a teaching tool, with no budget, said it would move about 30 users elsewhere and asked for an education discount.
  - A tech-centre teacher asked for an educational version.
  - Another poster called it awful timing for educational budgeting.
  - A follow-up thread asked how schools can pay yearly and tax-exempt. Spiceworks later added annual plans with quotes and invoices.
  - [same thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- **Recurring requests and complaints in that thread (approx. counts, opinions):**
  - Ticket search poor: ~5
  - Want on-prem/self-hosted back, or the cloud lost features vs on-prem: ~8
  - Mobile app (keyboard covers the comment box, 38 spice ups; full-screen ads; notification errors; daily sign-out): ~4
  - Reporting/audit reports weak: ~2
  - Entra/Azure AD SSO: ~2
  - Mandatory categories/attributes: 1
  - Persistent checklists: 2
  - End users find email-link login cumbersome and can't see ticket history: 1
  - Email distribution lists for category notifications: 1
  - Approval workflow and audit trail: 1
  - Bulk actions without ads: 1
  - [same thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- **Alternatives named by people leaving:**
  - Zammad (one org already migrating on-prem)
  - FreeScout (an evaluator's shortlist also had Znuny, Request Tracker, OTOBO, ManageEngine and osTicket)
  - Zendesk
  - Gogenuity (about $30/month unlimited)
  - helpdesk features built into existing RMM tools
  - [same thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- **Reporting regret (Mar 2023):** a user who had just migrated from Spiceworks server to cloud said relying on Power BI for reports removed the main reason they chose Spiceworks, which was easy reporting. — [Spiceworks Community search listing](https://community.spiceworks.com/search?q=school%20district%20help%20desk%20ticketing&order=latest)

**School-district posts in the Spiceworks Community (feature needs)**
- Dec 2021: a district office with 20+ schools needs ticket rules that assign by location.
- May 2023: a district wanted to filter tickets by school, a custom attribute (since implemented). This mattered especially on the phone while on site.
- May 2017 [OLD]: principals want ticket emails for their own buildings (implemented).
- Nov 2016 [OLD]: each principal wants a weekly per-location report emailed every Friday.
- Jan 2021 [OLD]: restrict the portal to staff only.
- Aug 2021 [OLD]: sub-organisations for building tech aides.
- Jun 2026: a school can't pre-create users because it doesn't know every address that will email a ticket.
- Dec 2024 and Jul 2026: schools separate IT and maintenance into different organisations because mixed queues get cluttered.
- Source: [Spiceworks Community search listing](https://community.spiceworks.com/search?q=school%20district%20help%20desk%20ticketing&order=latest)
- Jun 2025: a small district asked whether there is a cap on end users or ticket volume in the free tier. — [Cloud Help desk end user cap?](https://community.spiceworks.com/t/cloud-help-desk-end-user-cap/1212446)
- **[OLD, 2018]** "Do you let students submit tickets?" ran to 79+ replies. Opinion split by school size and how the support chain is designed. — [thread](https://community.spiceworks.com/t/do-you-let-students-submit-tickets/628658/79)

### Inferences
- The Spiceworks episode is a large, documented churn event among small school IT teams in 2025. They are looking for:
  - free or cheap tools
  - self-hostable tools
  - "on-prem-like" depth: custom fields, reports, scripting
  - purchasing that fits school finance: annual invoice/PO, tax exemption, notice before fiscal year
- This is directly relevant to a self-hosted, no-per-seat tool like EduHelpdesk.
- Multi-site routing and filtering by school/building, and per-head-teacher notifications and reports, are recurring district needs. In UK terms these map to multi-academy trusts.

### Gaps
- No Reddit evidence (blocked). The Reddit-specific questions remain unanswered: "Incident IQ worth it", "what helpdesk do you use" (2022–2026).
- No forum evidence found for Australian or Canadian schools' tool choices. ForceDesk markets itself as an Australian-school helpdesk/asset platform (ticketing, device tracking, IPAM, bookings, wiki), but no user reviews were found. — [ForceDesk](https://forcedesk.io/) (vendor)

---

## Q3. Public feature-request boards and roadmaps: which school-relevant requests have the most votes?

### Takeaway
Across open-source and freemium boards, the highest-voted requests cluster around:
1. **identity integration** (SSO: SAML/OIDC/Google/Entra; modern email auth)
2. **flexible data and locations** (custom fields everywhere, parent/child locations, checkout to location)
3. **reservations/booking and self-service checkout**
4. **workflow building blocks** (sub-tickets, checklists, mandatory fields, automation, webhooks)
5. **reporting** (saved/scheduled/custom)

### Cited Findings

**Snipe-IT (asset management, widely used by schools), open/closed issues by 👍** — [GitHub API query](https://api.github.com/search/issues?q=repo:grokability/snipe-it+is:issue+sort:reactions-%2B1-desc&per_page=40)

| 👍 | Request | Created | Status |
|---|---|---|---|
| 112 | Custom fields for non-assets (licences, accessories, users, etc.) | 2017 | open |
| 79 | Check out accessories/consumables in quantities | 2018 | closed |
| 61 | **Reservations / schedule requests** (book equipment) | 2018 | open |
| 53 | **Parent and child locations** | 2017 | open |
| 51 | **Sale of assets** (disposal) | 2023 | open |
| 50 | Subscription licences | 2018 | — |
| 45 | **OpenID Connect** | 2023 | open |
| 33 | SAML | 2015 | closed |
| 28 | **Self-service asset checkout** | 2018 | open |
| 26 | Predefined kits should include accessories/consumables | 2020 | open |
| 20 | **Built-in helpdesk/ticket system** | 2015 | open |
| 20 | Bulk check-in | — | closed |
| 19 | Scheduled maintenance with notifications | — | — |
| 18 | Link user accounts to Google Apps | 2014 | — |
| 18 | Webhooks | — | — |
| 14 | MS Teams integration | — | — |
| 12 | In-person asset-acceptance flow | — | — |
| 10 | Saved custom reports | — | — |

- Snipe-IT school-specific requests from a Danish school, **[OLD, Nov 2014]**:
  - users without email addresses
  - users with no login
  - locations without postal addresses (for classes)
  - viewing users by location
  - bulk CSV user import
  - [Snipe-IT #398](https://github.com/grokability/snipe-it/issues/398)
- A public school network asked how to automate a Chromebook export from Google Admin into Snipe-IT. — [Snipe-IT #8197](https://github.com/snipe/snipe-it/issues/8197)

**Spiceworks Cloud Help Desk feature-request tag (all-time "top", ranked by activity; vote counts are low because voting arrived late)** — [Spiceworks CHD feature requests, top](https://community.spiceworks.com/tag/spiceworks-cloud-help-desk-chd-feature-request/752/l/top?period=all)
- SSO with Google Apps / O365 / Azure AD: 17 votes, 117 replies, open since 2017, still active Jun 2026.
- Helpdesk admin role: 71 replies.
- Edit comments / change visibility: 88 replies, implemented.
- MFA: 60 replies, implemented 2025.
- Persistent checklists: 60 replies. Implemented only in Premium (2025).
- Sub-tickets: 10 votes, 53 replies, open.
- Print tickets: implemented.
- Custom SQL reports: 42 replies.
- Date format: 48 replies.
- International options: 33 replies.
- Filter views by custom attributes: implemented.
- Sub-categories: 38 replies.
- Actual date/time on comments.
- Users view their own tickets: implemented.
- Auto-refresh: 5 votes.
- Better search operators: announced/released 2026.
- REST API: 11 votes.
- Scheduled reports: 3 votes.
- Business hours.
- Mandatory fields: 5 votes, released.
- Regex in ticket rules.

**osTicket (👍, low overall)** — [GitHub API query](https://api.github.com/search/issues?q=repo:osTicket/osTicket+is:issue+sort:reactions-%2B1-desc&per_page=30)
- Automation system RFC: 12
- Combo-tree help topics: 11
- Workflow/change management: 10
- Help topics restricted to organisations: 9
- **Microsoft retiring basic auth for POP/IMAP:** 8 👍 but **107 comments**, the most-discussed
- Snipe-IT integration: 7
- SLA with business hours: 6
- "Last activity" column: 6
- Merge users: 5

**GLPI (👍)** — [GitHub API query](https://api.github.com/search/issues?q=repo:glpi-project/glpi+is:issue+sort:reactions-%2B1-desc&per_page=30)
- Webhooks: 39
- GUI modernisation: 15
- IPAM: 8
- Modern auth: 5 (open, 2023)
- Notification rework: 5
- Self-service portal: 4
- User closes ticket by email: 4
- GDPR compliance: 4

**Freshservice community**
- A prominent request is pasting screenshots into service-catalogue request forms. This already works on "Report an issue" but not on catalogue items. Rich-text fields were reportedly targeted for GA by end of Q2 2026. — [Freshworks idea](https://community.freshworks.com/ideas/feature-request-pasting-screenshots-when-submitting-service-requests-23988) (search-result summary; page itself returned 403)

**Incident IQ community ideas** (login-gated)
- Confirmed standing ideas:
  - better keyword search (#802)
  - tagging other techs in tickets (#477)
  - a 2026 reviewer says search complaints in the idea portal went unaddressed
- [G2 vendor reply](https://www.g2.com/products/incident-iq/reviews)

### Inferences
- Requests that recur across several boards, and so are likely to be wanted by EduHelpdesk users too:
  - SSO (Entra/Google)
  - modern-auth email intake
  - location hierarchy (trust → school → building → room)
  - equipment reservations/loans with self-service
  - disposal/sale records
  - sub-tickets/checklists
  - mandatory fields
  - saved/scheduled reports
  - webhooks/Teams integration
- Asset-tool users are asking for a helpdesk (Snipe-IT #663), and helpdesk users are asking for asset integration (osTicket #2423). This supports the integrated approach.

### Gaps
- Incident IQ, TOPdesk, HaloITSM, Jitbit and TeamDynamix idea boards are private or were not found publicly. No vote counts were obtained.
- The Freshservice/Zendesk boards could not be ranked by votes (403).

---

## Q4. What do teachers/staff (requesters) want from submitting tickets?

### Takeaway
Requesters want:
- **to ask for help in seconds without leaving the class**: one-click buttons, QR codes that pre-fill room/location, minimal fields
- **not to repeat themselves**
- **visible status and history**, with no "false resolutions" when tickets are transferred
- **easy self-help that actually finds answers**
- **help outside 9–5**

Teachers are the most time-poor requesters. Office/admin staff raise the most requests on behalf of others.

### Cited Findings

**NSW Department of Education (Australia) service-design research on school support** (undated case study, likely pre-2023)
- Staff could not find or receive support quickly or easily.
- Intranet content was scattered or outdated, and search returned irrelevant results.
- Self-help pointed to outdated PDF quick-reference guides.
- People faced long call waits and had to repeat details on each transfer.
- "False resolution" notifications appeared when a ticket was merely transferred to another team.
- As a result staff distrusted the support teams and leaned on colleagues instead.
- [Good HCD case study](https://goodhcd.com/portfolio/how-service-design-improved-support-for-nsw-schools/)

**Same study, by role**
- **Teachers' needs are urgent:** they have no time to wait on calls, search for information or fill in forms between classes. They can often only seek help after school, while guided support ran 9am–5pm.
- **Principals** delegate requests to admin staff.
- **School Administrative Managers** raise the most requests, often on behalf of principals and teachers.
- The project adopted **trust** as the primary experience indicator for measuring customer satisfaction (CSAT).
- [Good HCD case study](https://goodhcd.com/portfolio/how-service-design-improved-support-for-nsw-schools/)

**One-click / QR submission (Michigan State University, higher ed, Dec 2024)**
- MSU added an IT Help button on podium PCs and QR codes on teaching carts.
- Requests take under a minute, and a ticket is generated in about 40 seconds.
- In the first weeks, 25% of 1,749 tickets came through the button or QR code.
- Instructors valued not leaving the podium and getting a faster technician dispatch. Three more colleges asked for it.
- [MSU IT news](https://tech.msu.edu/news/2024/12/it-help-button-and-qr-code-saving-time-reducing-stress-in-the-classroom)

**QR room tags and asset QR scanning (vendor claims)**
- K-12 vendors market QR room tags that pre-fill school and location (SupportStudioK12) and scanning a failing asset to report it (ManageEngine). — [SupportStudioK12](https://supportstudiok12.com/); [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html) (vendor, via search summary)

**Teacher-friendly submission and self-help (Incident IQ reviewers, opinions)**
- Ticketing is designed with teachers in mind and the knowledge base lets teachers self-solve before raising a ticket (Luis L., 2024).
- Staff and parents can raise detailed tickets, including student and device (Angelene S., 2026).
- Extra clicks reduce adoption (Nathan K., 2022).
- Requesters cannot edit a description after submitting (2022).
- Sources: [G2](https://www.g2.com/products/incident-iq/reviews); [Capterra p2](https://www.capterra.com/p/186247/Incident-IQ/reviews?page=2); [Capterra p1](https://capterra.com/p/186247/Incident-IQ/reviews/)

**Status visibility and login friction (Spiceworks)**
- End users find email-link login cumbersome and say they cannot see ticket history (Apr 2025). — [Spiceworks thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- In Spiceworks guest mode, requesters cannot attach files or see history (Dec 2024 answer). — [Spiceworks search listing](https://community.spiceworks.com/search?q=AI%20help%20desk%20school%20tickets%20after%3A2024-01-01)

**Screenshots and email-only requesters**
- Requesters want to paste screenshots directly into request forms. — [Freshworks idea](https://community.freshworks.com/ideas/feature-request-pasting-screenshots-when-submitting-service-requests-23988) (search summary)
- A school notes it cannot pre-create every user who emails tickets (Jun 2026), so email intake from unknown senders matters. — [Spiceworks search listing](https://community.spiceworks.com/search?q=AI%20help%20desk%20school%20tickets%20after%3A2024-01-01)

**Getting tickets submitted at all** **[OLD, 2015–2017]**
- K-12 techs said the biggest problems are getting staff to raise tickets at all and getting accurate descriptions. Corridor requests get forgotten. — [Spiceworks search listing](https://community.spiceworks.com/search?q=school%20district%20help%20desk%20ticketing&order=latest)

**Vendor framing of the same problem**
- Incident IQ's 2026 AI release says teachers can describe issues in plain language while AI captures the details. It positions incomplete teacher submissions as the core friction. — [Business Wire via Morningstar](https://www.morningstar.com/news/business-wire/20260225688335/incident-iq-introduces-ai-that-accelerates-k12-it-support-and-protects-instructional-time) (vendor claim)

### Inferences
- For EduHelpdesk's portal, ranked by evidence:
  1. Fastest-possible submission: room/asset QR with pre-filled location, minimal fields, an "anything else" escape.
  2. Requester status and history without friction: no email-link-only logins.
  3. Honest status changes: never show a transfer as a resolution.
  4. Screenshot paste.
  5. Self-help that is current and searchable.
- Outage/known-issue banners are logically implied by the "network outage floods the queue" challenge, but they are not directly evidenced by requester voices.
- Admin/office staff raising tickets for others ("on behalf of") is a real pattern. The NSW finding that admin managers raise the most requests supports that.

### Gaps
- No quantitative K-12 teacher survey specifically about help-desk submission preferences was found for 2022–2026.
- A search snippet claimed "30% of educators cite technical problems as the biggest integration challenge", but I could not confirm which publication it came from, so it is excluded.
- No student-requester evidence beyond the old "should students submit tickets?" debate and student-run help desks:
  - Bethlehem Central (NY) student help desk: 500 device repairs since Sept 2023, 32 student interns. — [EdTech Magazine 2024](https://edtechmagazine.com/k12/article/2024/07/schools-give-students-career-head-start-help-desk-opportunities)
- No direct evidence found on requesters wanting outage notices, or on whether they do or don't know asset tags. The asset-tag point is only implied by vendor QR/auto-fill marketing.

---

## Q5. What do IT directors / senior leaders want (dashboards, workload metrics, lifecycle cost, budget planning, satisfaction)?

### Takeaway
District leaders are **understaffed, budget-constrained and accountable**, so they want data that justifies resources:
- ticket trends by school, device model and issue type
- technician workload and resolution times
- repair/parts and fee tracking
- audit-ready asset records
- simple scheduled reports for principals

Satisfaction measurement is rarely discussed by practitioners. NSW used "trust" as its CSAT indicator.

### Cited Findings

**CoSN 2026 U.S. State of EdTech** (607 respondents, 44 states, Jan–Mar 2026)
- 58% of districts are understaffed for supporting teaching-and-learning technology, while 66% say core technical functions are adequately staffed.
- Budget constraints and lack of resources are the top challenge, followed by organisational silos.
- 65% cite insufficient budget as the biggest cybersecurity barrier.
- 79% have AI guidelines (57% in 2025).
- 64% use AI in operations (37% the prior year).
- 56% require vendors to provide product safety information.
- Sources: [GovTech](https://www.govtech.com/education/k-12/cosn-report-cybersecurity-is-top-concern-ai-guardrails-needed); [CoSN PDF](https://www.cosn.org/wp-content/uploads/2026/05/U.S.-State-of-EdTech-2026.pdf) (search summary)

**CoSN 2025 District Leadership**
- 94% see AI as positive, and 80% have GenAI initiatives.
- 61% fund cybersecurity from general funds.
- 59% use approved-app lists (up from 42% in 2023).
- [eSchool News](https://www.eschoolnews.com/it-leadership/2025/05/13/edtech-leaders-see-expanding-roles/)

**Support ratios and turnover**
- Connecticut fall-2024 survey of K-12 tech leaders: support staff-to-device ratios ranged from 1:300 to 1:2,667, averaging 1:670 (statewide 1:1,008). — [CT Commission for Educational Technology PDF](https://portal.ct.gov/das/-/media/das/ctedtech/publications/2024/cet2024k12staffdevices.pdf) (search summary; PDF not machine-readable here)
- EdTech Magazine reports K-12 IT turnover taking institutional knowledge with it. This is an argument for knowledge bases and documented ticket history. — [EdTech Magazine Nov 2024](https://edtechmagazine.com/k12/article/2024/11/dont-lose-valuable-it-knowledge-amid-k-12-turnover) (search summary)

**What leaders value in tooling** (G2 Incident IQ reviews, opinions)
- Reports used for data-driven decisions (Stacey C.).
- Reports on which device types fail most, used for proactive fixes and audits (Luis L.).
- Trend analysis of tickets, parts usage and labour distribution, plus SLA tracking (Ryan C.).
- Detailed reports holding families accountable for returning 1:1 devices, and repair-fee tracking (Ron B.).
- Single platform replacing several tools and paper policies (Niki S.).
- [G2 Incident IQ](https://www.g2.com/products/incident-iq/reviews)

**Reporting complaints**
- Limited data mining/export (Incident IQ Director, Nov 2020 [OLD]). — [Capterra p1](https://capterra.com/p/186247/Incident-IQ/reviews/)
- Spiceworks:
  - Clients wanted to move off because audit reports can't be generated (Apr 2025).
  - Power BI reliance criticised (Mar 2023).
  - Long-running requests for scheduled reports and custom SQL reports.
  - Principals wanted per-location weekly emailed reports [OLD 2016].
  - Sources: [Spiceworks thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true); [Spiceworks FR list](https://community.spiceworks.com/tag/spiceworks-cloud-help-desk-chd-feature-request/752/l/top?period=all); [Spiceworks search listing](https://community.spiceworks.com/search?q=school%20district%20help%20desk%20ticketing&order=latest)

**Budget planning and procurement fit**
- Leaders objected to unbudgeted mid-year subscription costs, monthly-only billing and the lack of a tax-exempt route. Annual quotes/invoices were added later. — [Spiceworks thread](https://community.spiceworks.com/t/spiceworks-cloud-help-desk-announces-platform-enhancements-and-a-new-paid-offering/1187160?print=true)
- G2 reviewers rate Incident IQ's perceived cost at the high end, with a 23-month payback vs 5 months for Spiceworks. — [G2 compare](https://www.g2.com/compare/incident-iq-vs-spiceworks-cloud-help-desk)

**Device lifecycle and disposal**
- Disposal/sale records are a top-5 Snipe-IT request (51 👍, 2023). — [Snipe-IT #12554](https://github.com/grokability/snipe-it/issues/12554)
- Leaders use reports of failure-prone models to steer procurement (Luis L.). — [G2](https://www.g2.com/products/incident-iq/reviews)
- Vendor material says help-desk data should reveal repair spikes by model, Wi-Fi clusters by building, and training gaps by user group. — [Incident IQ blog](https://www.incidentiq.com/blog/k12-help-desk-data-strategic-insights) (vendor; search summary)

**Satisfaction scores**
- NSW made "trust" the primary CSAT experience indicator. — [Good HCD](https://goodhcd.com/portfolio/how-service-design-improved-support-for-nsw-schools/)

### Inferences
- Priority leadership outputs for EduHelpdesk:
  - per-school/site dashboards: volume, open backlog, time to first response and resolve
  - technician workload
  - asset failure/repair cost by model, and age to support replacement planning
  - disposal and finance reports
  - scheduled emailed summaries for head teachers and SLT
  - CSV export
- Simplicity matters more than BI-tool sophistication: users rejected "use Power BI".
- Because staffing is short (58% understaffed, ratios around 1:670–1:1,000), features that cut technician time rank highly with directors:
  - bulk actions
  - templates/checklists
  - auto-routing by location
  - self-help

### Gaps
- No independent survey of what K-12 IT directors want from helpdesk dashboards specifically. Evidence comes from reviews and vendor content.
- Practitioners barely discuss satisfaction-survey (CSAT) usage. No data on response rates or value in schools.
- The CoSN 2026 PDF could not be text-extracted here, so its figures come from news coverage. Canada's first CoSN national survey exists but was not reviewed. — [CoSN Canada news](https://www.cosn.org/cosn-news/cosn-releases-first-nationwide-survey-of-canadian-edtech-leaders/)

---

## Q6. How have AI features in helpdesks (2024–2026) been received by school IT teams?

### Takeaway
District leaders are broadly optimistic about AI and increasingly use it in operations (CoSN: 64% in 2026, up from 37%). However, **independent practitioner evidence on helpdesk AI is thin**:
- Most claims are vendor-sourced. Incident IQ, for example, claims resolution time fell from 5 days to 1, without a stated method.
- In the review samples, AI is essentially absent from school reviewers' likes and dislikes.
- Where sysadmins comment on vendor helpdesk AI (Freshservice Freddy), reception is lukewarm: it feels like rules-based automation, is locked to expensive tiers, and needs tuning.
- Privacy (FERPA, "not used to train models") is a precondition.

### Cited Findings

**Incident IQ AI Ticket Assistant (25 Feb 2026, vendor press release)**
- Teachers describe issues in plain language. The AI captures details, classifies, prioritises and routes the ticket, and offers guided self-service before a ticket is created.
- Claims tickets are resolved up to 30% faster and initial average resolution time fell from 5 days to 1.
- Says district data is never used to train an LLM, with FERPA compliance support.
- Sources: [Business Wire via Morningstar](https://www.morningstar.com/news/business-wire/20260225688335/incident-iq-introduces-ai-that-accelerates-k12-it-support-and-protects-instructional-time); [Incident IQ newsroom](https://www.incidentiq.com/newsroom/product-updates/ai-ticketing-assistant-k-12) (vendor claims; not independently verified)

**Brevard Public Schools (Florida)**
- IT Director Barrett Puschus said a Copilot Studio chatbot sharply cut help-desk calls about the student information system and gave staff time back.
- The district planned an IT help-desk chatbot next.
- In the same article, other district leaders mainly used AI for coding and troubleshooting assistance (Val Verde USD, Mount Pleasant ISD).
- [EdTech Magazine Apr 2025](https://edtechmagazine.com/k12/article/2025/04/how-ai-transforming-business-operations-k-12)

**CoSN figures**
- 2026: 64% of districts use AI in operations (up from 37%), 96% believe AI could benefit education, and 75% are concerned about AI-enabled cyberattacks. — [GovTech](https://www.govtech.com/education/k-12/cosn-report-cybersecurity-is-top-concern-ai-guardrails-needed)
- 2025: 94% see AI as positive, with productivity the top benefit. — [eSchool News](https://www.eschoolnews.com/it-leadership/2025/05/13/edtech-leaders-see-expanding-roles/)

**Freshservice Freddy AI (general IT, not school-specific)**
- A summary of user feedback reports:
  - Cooler reception on r/sysadmin, where it is seen as automation rather than intelligence.
  - Deflection quality was criticised.
  - Advanced AI is locked to Enterprise tiers.
  - Costs draw the most criticism.
  - Out-of-the-box quality needs tuning.
- [eesel.ai Freshservice AI review](https://www.eesel.ai/blog/freshservice-ai-review) and [eesel.ai Freddy verdict](https://www.eesel.ai/blog/freshdesk-freddy-worth-it) — **caution:** eesel sells a competing AI product, and the underlying Reddit quotes could not be verified because Reddit is blocked.

**Absence of AI in practitioner channels**
- A Spiceworks Community search for AI + school helpdesk posts since 2024 returned no practitioner discussion of helpdesk AI, only routine CHD support threads. — [Spiceworks search](https://community.spiceworks.com/search?q=AI%20help%20desk%20school%20tickets%20after%3A2024-01-01)
- Incident IQ carries an "AI Enabled" badge on G2, but none of the 2026 reviews shown on page 1 mention AI. — [G2 Incident IQ](https://www.g2.com/products/incident-iq/reviews)

**Educator trust in AI**
- Educators' average trust in AI was 58.4/100 in 2025, up about 18% from 2024. — [Michigan Virtual 2025](https://michiganvirtual.org/research/publications/ai-in-education-a-2025-snapshot-of-trust-use-and-emerging-practices) (search summary)
- EdWeek reported growing public scepticism of AI in K-12 in Aug 2025. — [EdWeek](https://www.edweek.org/technology/americans-grow-more-skeptical-of-ai-in-k-12-schools-poll-finds/2025/08) (headline only)

### Inferences
- In school helpdesks, AI is currently a vendor-led selling point rather than a user-demanded feature. The verified user demand is for the problems AI is pitched to solve:
  - incomplete teacher submissions
  - mis-categorised tickets
  - slow routing
  - repeat questions
- Cheaper, deterministic fixes address these too:
  - service catalogue
  - QR/location pre-fill
  - mandatory fields
  - routing rules
  - knowledge-base suggestions
- For a UK self-hosted product, any AI should be optional, privacy-preserving (no training on school data, UK GDPR) and not tier-locked. Its value will be judged on time saved, not novelty.

### Gaps
- No independent (non-vendor) measurement of helpdesk AI outcomes in schools was found.
- No Reddit r/k12sysadmin sentiment on AI ticketing could be accessed.
- No Australian or Canadian school-IT evidence on helpdesk AI was found.
