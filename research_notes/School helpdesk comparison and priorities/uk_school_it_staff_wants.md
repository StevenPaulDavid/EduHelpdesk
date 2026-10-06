# What UK school IT staff want (and dislike) in a school IT helpdesk

Scope note: compiled 2026-10-03. The main evidence base is 18 EduGeek.net threads read in full (EduGeek is the main UK school-IT community), plus a few EduGeek search-result snippets, the DfE Technology in Schools surveys (2022-23 and 2024-25) and the 2023 ANME/Salamander wellbeing survey. Reddit could not be reached (blocked to both fetch tools). Twitter/X, LinkedIn and the Spiceworks Community were not searched directly. **To stay within copyright limits, poster views are given as attributed close paraphrases (marked "para.") rather than verbatim quotes, apart from one short verbatim quote. Each paraphrase links to its thread, so the exact wording can be checked.** Tallies count distinct threads, not posters.

## 1. Which helpdesk tools do UK school techs use, and why did they choose them?

### Takeaway
UK school techs use a long tail of tools. The most common are Freshdesk/Freshservice, ManageEngine ServiceDesk Plus, osTicket, Spiceworks Cloud, GLPI, Jira SM and HaloITSM, plus DIY setups in Microsoft Lists or Trello. The deciding factors are almost always free or cheap (especially "free for N agents"), simple to set up, email-to-ticket through the school's M365 mailbox, and SSO/AD integration. Better-funded MATs pick paid ITSM tools (Halo, Freshservice, Jira SM), but price objections come up in every thread where those tools appear.

### Cited Findings

#### Source key (thread IDs used in the tallies)
Recent (2022-2026):
- T1 Helpdesk options (Sep–Nov 2024): https://www.edugeek.net/forums/topic/219505-helpdesk-options/ (and /page/2/)
- T2 Free Helpdesk recommendations (Apr 2023): https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/
- T3 Replacement for Spiceworks Help Desk (May–Jun 2025): https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/
- T4 Spiceworks Helpdesk (Aug 2022): https://www.edugeek.net/forums/topic/208957-spiceworks-helpdesk/ (and /page/2/)
- T5 MAT helpdesk recommendations (Sep 2023): https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/
- T6 Ticketing System (Jan–Feb 2022): https://www.edugeek.net/forums/topic/206184-ticketing-system/
- T9 Helpdesk recommendations to replace Jira Service Management? (Jun 2025 – Jun 2026, 19-school trust): https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/
- T10 Freshdesk – upcoming but undocumented end of Free plan? (Jun–Sep 2026): https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/
- T11 AI Agents (May 2026): https://www.edugeek.net/forums/topic/226464-ai-agents/
- T12 Getting staff to log tickets (Jan 2022, 23 replies): https://www.edugeek.net/forums/topic/205757-getting-staff-to-log-tickets/
- T13 Spiceworks Cloud Helpdesk (Jan 2026): https://www.edugeek.net/forums/topic/225520-spiceworks-cloud-helpdesk/
- T14 IT asset management software (Jan–Feb 2026): https://www.edugeek.net/forums/topic/225733-it-asset-management-software/
- T15 Looking for an asset management system (Nov 2025 – Jan 2026): https://www.edugeek.net/forums/topic/225193-looking-for-an-asset-managemnet-system/
- T19 Asset Management query (Apr 2026; **search snippet only**): https://www.edugeek.net/forums/topic/226391-asset-management-query/
- T20 What are your top priorities for your first week? (Feb 2025; **search snippet only**): https://www.edugeek.net/forums/topic/221572-what-are-your-top-priorities-for-your-first-week/

Older (FLAG: before 2022):
- T7 Helpdesk System – Every (Jun 2020): https://www.edugeek.net/forums/topic/195679-helpdesk-system-every/
- T8 Multi school Helpdesk system (Mar–Apr 2018): https://www.edugeek.net/forums/topic/174679-multi-school-helpdesk-system/
- T16 Looking for Helpdesk software for my team (May 2019; page 1 of 3 read): https://www.edugeek.net/forums/topic/186584-looking-for-helpdesk-software-for-my-team/
- T17 Helpdesk software for Site team (Mar 2021, with replies to Jul 2022): https://www.edugeek.net/forums/topic/200799-helpdesk-software-for-site-team/
- T18 Free Helpdesk (May 2020): https://www.edugeek.net/forums/topic/195210-free-helpdesk/

#### Tools in use: number of recent (2022-2026) threads where a poster says they use or used the tool
- Freshdesk / Freshservice: 8 threads (T1, T2, T4, T5, T6, T9, T10, T15). Praised as "just works", free tier, mobile app, 365 SSO. Since 2024 it has also drawn complaints about free-tier cuts and add-on pricing — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/), [T10](https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/), [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- osTicket (self-hosted, free): 5 threads (T1, T2, T3, T6, T10). One MAT uses it trust-wide. Repeatedly called free but painful to set up — [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/), [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/)
- Spiceworks Cloud (current users): 5 threads (T1, T3, T5, T6, T13) — [T13](https://www.edugeek.net/forums/topic/225520-spiceworks-cloud-helpdesk/)
- GLPI (self-hosted; mostly used for asset inventory, sometimes for the helpdesk): 5 threads (T3, T6, T10, T14, T15) — [T14](https://www.edugeek.net/forums/topic/225733-it-asset-management-software/)
- Jira Service Management: 4 threads (T1, T6, T9, T11) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- Microsoft Lists / SharePoint / Power Automate as the helpdesk or job log: 4 threads (T1, T9, T14, T15) — [T15](https://www.edugeek.net/forums/topic/225193-looking-for-an-asset-managemnet-system/)
- ManageEngine ServiceDesk Plus: 3 threads (T1, T2, T6), with about 7 posters, some on it for 10–13 years. Free for up to 5 technicians — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/), [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
- HaloITSM: 3 threads (T1, T5, T9). Praised as the best and most flexible tool. Posters in T1 and T9 found the quotes too expensive (a team of 2; a 19-school trust), and T5 shows the same worry about price — [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/), [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- Trello + email plugin: 3 threads, all from the same poster (dhicks) — [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/)
- Parago / Civica EdOps (premises + assets + helpdesk): 4 threads (T2, T10, T14, T15), mostly for site/asset work; one school leaving it for helpdesk use — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
- Single mentions: SupportPal (self-hosted, multi-department, cheap), Zammad (self-hosted, free), Zoho Desk free, NeetoDesk (free for 10 agents), Desk365 (M365/Teams sync), LiveAgent, Mojo Helpdesk, Harmony PSA, ServiceNow, and a home-built helpdesk with Bromcom sync (T9, Jun 2026) — [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/), [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/), [T10](https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/), [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/), [T1 p2](https://www.edugeek.net/forums/topic/219505-helpdesk-options/page/2/), [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/)

#### Why they chose them (opinions)
- **Free / free-for-N-agents is the main driver.** The T1 OP (Sep 2024, a school team of 2–3) wanted to keep the cost as low as possible so they could justify any spend to SLT. In T16 (2019, older), one poster remarked that so many schools use Spiceworks only because it is free, and read that as a comment on school funding — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/); [T16](https://www.edugeek.net/forums/topic/186584-looking-for-helpdesk-software-for-my-team/)
- **Low setup and maintenance.** One poster (para., jthompson, Apr 2023) liked moving from osTicket to hosted Freshdesk because they no longer had to maintain their own helpdesk server — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
- **"Exactly enough."** One poster (para., Oaktech, Feb 2022) runs osTicket trust-wide because it has every feature they need and nothing they don't — [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/)
- **Familiar look and feel.** A school moving from 3 Spiceworks instances to 1 Freshservice found the two felt alike (para., timbo343, Sep 2024) — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
- **All-inclusive pricing and UK base for paid tools.** Halo was liked because one price covers everything, with no paid extras. One MAT lead noted Halo being UK-based eases data-protection concerns (para., Shaun_Dark_Lord, Jun 2025) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- **Tool imposed from above.** Examples: an MSP already used Parago before the school had in-house IT (para., itskdog, Jan 2026); a tech was made to use Spiceworks Cloud even though a Freshdesk trial found Freshdesk better (para., computer_expert, Aug 2022); a trust pushed Every onto school IT (2020, older) — [T14](https://www.edugeek.net/forums/topic/225733-it-asset-management-software/); [T4](https://www.edugeek.net/forums/topic/208957-spiceworks-helpdesk/); [T7](https://www.edugeek.net/forums/topic/195679-helpdesk-system-every/)

### Inferences
- In the UK school market the price ceiling is effectively zero for small teams and "a few hundred pounds" for most schools. Halo/Freshservice-level pricing only works for large MATs with about 10+ agents (in T1, a team of 10 found Halo reasonable while a team of 2 found the quote huge).
- Because 63% of primaries now get technical support from a managed service provider (DfE 2025, section 6), many primary schools probably never choose a helpdesk themselves; they use their MSP's. EduGeek voices therefore over-represent secondaries and MATs with in-house teams.

### Gaps
- No quantitative market-share data for helpdesk tools in UK schools was found. Counts above come from forum mentions only.
- Reddit (r/sysadmin, r/k12sysadmin, UK threads) could not be accessed: both the fetch tool and the browser blocked reddit.com. UK Reddit voices are missing.

## 2. What happened when Spiceworks Help Desk changed or was discontinued, and where did UK schools go?

### Takeaway
Spiceworks lost UK school users in three waves:
1. **2020–2022:** the on-prem desktop help desk reached end of life and lacked modern auth for Office 365 mail.
2. **2022–2024:** schools found the rewritten Cloud Help Desk dated. It also could not poll an O365 mailbox, so users had to email an external Spiceworks address.
3. **June 2025:** Spiceworks began charging accounts with 6+ technician seats.

Schools moved mainly to Freshdesk/Freshservice and to self-hosted osTicket/GLPI. Freshdesk then cut its own free tier in 2024–2026, which set off a second round of moves (to NeetoDesk, Zoho, osTicket).

What schools missed about Spiceworks: it was free, simple and email-driven, could run separate instances per department, and let a tech make any user the owner of a ticket.

### Cited Findings
- **On-prem end of life and mail auth (older background).** In May 2020, robyholmes said the Spiceworks on-prem email-authentication problem had no fix coming. The new Help Desk Server was beta with no ticket migration, so Spiceworks offered schools no long-term option. He added that Spiceworks Cloud being US-hosted was far from ideal for UK schools (para.) — [T18](https://www.edugeek.net/forums/topic/195210-free-helpdesk/)
- **Secondary sources** put the Spiceworks desktop help desk end of life at 31 Dec 2021. Not verified against a Spiceworks primary source — [eesel.ai (vendor blog)](https://www.eesel.ai/blog/spiceworks-ticketing-system); [Comparitech](https://www.comparitech.com/net-admin/spiceworks-review/)
- **Aug 2022 posters:** the on-site version was end of life, no longer developed and lacked O365 modern auth, and Microsoft was due to switch off legacy auth in Oct 2022. The cloud rewrite was judged poor (para., gaz350b). The cloud's workaround was to use Spiceworks' own mail server, so staff had to email an external address; one poster moved to a competitor for that reason (para., foofighterjim). A third (para., APMerry) called the external address a deal-breaker and found free Freshdesk much better. Another said Spiceworks' mail servers were unreliable and caused ticket delays (para., leegcvcc) — [T4](https://www.edugeek.net/forums/topic/208957-spiceworks-helpdesk/), [T4 p2](https://www.edugeek.net/forums/topic/208957-spiceworks-helpdesk/page/2/)
- **Lack of MFA.** One poster was frustrated that Spiceworks said MFA work was not imminent as of mid-2022 (para., computer_expert) — [T4](https://www.edugeek.net/forums/topic/208957-spiceworks-helpdesk/)
- **2025 pricing change.** Spiceworks told users that from 1 June 2025 all accounts with 6+ employees must be on the Premium plan. Other posters clarified this means admin/tech seats. Accounts with ≤5 seats can stay on the free Core plan, while Premium adds an ad-free experience and extra features. Responses in the thread: Desk365, self-hosted osTicket (two posters) and GLPI (one poster, 15 years of use, O365 SSO and user creation) — [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/)
- **Premium pricing** is about $5 per seat per month billed annually, or $6 monthly (secondary/vendor source, not verified with Spiceworks) — [eesel.ai](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- **A team of exactly 6** found the change very annoying. They felt Spiceworks Cloud was no longer very good and took it as the moment to move to osTicket, then found osTicket very hard to set up (para., Planehazza, Jun 2025) — [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/)
- **The exodus seen from inside.** A trust IT lead (4 organisations) noticed many people leaving Spiceworks, asked why, and was still setting it up with one helpdesk email per organisation (para., Theldron, Nov 2024) — [T1 p2](https://www.edugeek.net/forums/topic/219505-helpdesk-options/page/2/)
- **MAT replacing Spiceworks (Sep 2023).** Schools with IT teams were on Spiceworks and were unimpressed. They wanted a scalable multi-desk system where key people can see every desk and move jobs between desks — [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/)
- **Where they went:**
  - Freshdesk: T4 (2 posters plus 1 trial), T1 (OP leaning that way)
  - Freshservice: T1 (from 3 Spiceworks instances)
  - osTicket: T3
  - GLPI: T3
  - Trello with an email plugin: T6 (dhicks, who still rated Spiceworks highly but preferred Trello's ordered Kanban list)
  - [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/); [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/)
- **What they missed or valued in Spiceworks:**
  - A tech can make any user the owner of a ticket so they get emailed updates, which helped get staff used to the helpdesk (para., dhicks, Jan 2022) — [T12](https://www.edugeek.net/forums/topic/205757-getting-staff-to-log-tickets/)
  - AD integration and it was free (CHiLL, Feb 2022) — [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/)
  - Older: five Spiceworks instances for site, personnel, IT, finance and admin, where users just email in (leegcvcc, 2020); the contracts section and iPad access (dobbyit, 2020); wanted rich text and multiple attachments per comment (timbo343, 2020) — [T18](https://www.edugeek.net/forums/topic/195210-free-helpdesk/)
  - 2019 (older): US-only mmddyy date format in Spiceworks Cloud; a "free but very buggy" view — [T16](https://www.edugeek.net/forums/topic/186584-looking-for-helpdesk-software-for-my-team/)
- **Ongoing reliability (Jan 2026).** Schools reported trouble with users logging in to submit tickets during a Spiceworks outbound-email incident — [T13](https://www.edugeek.net/forums/topic/225520-spiceworks-cloud-helpdesk/)
- **Freshdesk's own cuts (the second wave):**
  - Freshdesk's free tier fell from 10 agents to 2 in 2024 — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - A school was told at short notice its helpdesk would drop to two agents; the Freshservice alternative was about £6k a year for 15 agents. In that poster's (Netwacky87) words: "Gone from £0/yr to £6k!" — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - By Jun 2026, existing free plans were being shut down. Several schools reported being removed the year before. Moves: paid Freshdesk, Zoho Desk (missing ticket merge on its free plan), NeetoDesk (free for 10 agents) and an osTicket VM on a 365 mailbox (Sep 2026) — [T10](https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/)

### Inferences
- Many UK schools now distrust free SaaS tiers. Over 2024–2026 they have seen Spiceworks start charging, Freshdesk cut and then end its free tier, Lansweeper triple its price, and estreamdesk go out of business taking a school's tickets and knowledge base with it. A self-hosted free tool with data export answers that fear directly. One poster on Zoho wondered whether it would eventually do the same (para., itskdog, T10).
- Ticket-history import is a plausible switching enabler. Losing history is a stated fear (estreamdesk, T1), and lack of ticket migration was a stated Spiceworks objection (T18, 2020).
- Schools leaving Spiceworks look for the same basics: email-in, free, simple, AD/365 login. Wanting "more ITSM" is not the reason they leave.

### Gaps
- No primary Spiceworks announcement page was fetched. Wording comes from the EduGeek quote of it, and prices from secondary sources.
- I found no count of how many UK schools left Spiceworks, and no Spiceworks Community threads from UK schools.
- Whether Spiceworks Cloud has since gained O365 OAuth mailbox polling or MFA was not verified.

## 3. Most requested features, with frequency tally for ranking

### Takeaway
By how often they come up, UK school techs most want:
1. Low or zero cost with no per-agent caps
2. Easy setup and running
3. Email-to-ticket through the school's own M365 mailbox
4. M365/Google SSO and user sync
5. Multi-site/MAT and multi-department queues
6. Asset management linked to tickets

The brief's niche school items — MIS sync, loans, QR codes, room capture — appear, but in only 1–3 recent threads each. Several items in the brief, such as laptop trolleys, projector/AV, exam-season workload and governor reports, did not come up in any helpdesk thread I read.

### Cited Findings

#### Frequency tally (distinct threads; Recent = 2022–2026 posts, Older = 2018–2021, flagged)
| Rank | Theme | Recent threads | Older threads | Total |
|---|---|---|---|---|
| 1 | Cost: free/cheap, dislike of per-agent pricing and paid add-ons, free tiers withdrawn | 9 (T1,T2,T3,T4,T5,T6,T9,T10,T15) | 3 (T16,T17,T18) | 12 |
| 2 | Easy to set up/run, "just works", no enterprise bloat | 8 (T1,T2,T3,T4,T5,T6,T9,T10) | 4 (T7,T8,T16,T17) | 12 |
| 3 | Email-to-ticket via the school's M365 mailbox (OAuth), reply by email, CCs, forwarded mail keeps the original sender | 7 (T1,T2,T4,T6,T10,T11,T12) | 4 (T8,T16,T17,T18) | 11 |
| 4 | SSO/directory: M365/Entra/AD login, Google SSO, MFA, user sync | 7 (T1,T2,T3,T4,T5,T6,T9) | 4 (T7,T16,T17,T18) | 11 |
| 5 | DIY in the existing tenant (MS Lists + Power Automate, SharePoint, Trello, Google Forms) as the default competitor | 7 (T1,T4,T6,T9,T11,T14,T15) | 3 (T7,T16,T17) | 10 |
| 6 | Multi-site/MAT and multi-department (site, HR, finance, reprographics) in one instance, scoped visibility, central overview, reassign between desks | 6 (T1,T3,T5,T6,T9,T12) | 4 (T7,T8,T16,T17) | 10 |
| 7 | Self-hosting: appeal (free, control, data) | 8 pro (T2,T3,T4,T6,T9,T10,T14,T15) | — | 8 |
| 7b | Self-hosting: burden (setup pain, upkeep, "technical debt") | 5 con (T1,T2,T3,T4,T9) | — | 5 |
| 8 | Asset management linked to tickets (agent inventory, QR, custom fields, PAT dates, licence expiry) | 7 (T1,T3,T5,T9,T14,T15,T19) | 1 (T7) | 8 |
| 9 | Vendor instability/lock-in (price hikes, free tiers removed, vendor collapse, outages) | 7 (T1,T3,T4,T9,T10,T13,T15) | 1 (T18) | 8 |
| 10 | Staff bypass the system (corridor, direct email, phone); need SLT backing; "no ticket, no job" | 3 (T2,T12,T20) | 4 (T7,T8,T16,T18), plus at least 8 more 2006–2014 threads in EduGeek search | 7+ |
| 11 | Mobile app for technicians (incl. offline) | 4 (T2,T5,T6,T14); 1 "not fussed" (T9) | 3 (T16,T17,T18) | 7 |
| 12 | Automation: rules, auto-assign by site/team, templates, macros, no usage limits | 3 (T1,T6,T9) | 3 (T8,T16,T18) | 6 |
| 13 | Structured intake: room/location required, categories, per-request-type forms, chasing missing info | 4 (T1,T5,T9,T12) | 2 (2010/2017 search snippets) | 6 |
| 14 | API/integrations (monitoring, other systems, "all integrations included") | 4 (T1,T5,T6,T9) | 1 (T7) | 5 |
| 15 | Knowledge base/self-help (lukewarm) | 4 (T1,T5,T9,T11) | 1 (T8) | 5 |
| 16 | Reporting/evidence/dashboards for heads and managers | 2 (T9,T12); 1 "don't need reports" (T2) | 3 (T7,T8,T17) | 5 |
| 17 | Data location/GDPR/UK-based vendor | 3 (T4,T5,T9) | 1 (T18) | 4 |
| 18 | Teams integration/notifications | 1 (T3) | 3 (T7,T17,T18) | 4 |
| 19 | Tool imposed by trust or MSP | 2 (T4,T14) | 1 (T7) | 3 |
| 20 | SLAs and prioritisation | 2 (T9,T12) | 1 (T8) | 3 |
| 21 | Rich text, screenshots and multiple attachments in replies | 0 | 3 (T7,T8,T18) | 3 |
| 22 | Device loans (check-in/out, parent responsibility forms) | 2 (T9,T14) | 0 | 2 |
| 23 | MIS sync of staff/students (Bromcom; "import from MIS") | 2 (T9,T19) | 0 | 2 |
| 24 | Tech can log on behalf of a user / change requester | 2 (T2,T12) | 0 | 2 |
| 25 | Ticket merge and CC handling | 2 (T1,T10) | 0 | 2 |
| 26 | Ticket history retention/migration | 1 (T1) | 1 (T18) | 2 |
| 27 | AI agent / out-of-hours answers (mixed views) | 1 (T11) | 0 | 1 |
| 28 | Notifications clear enough for non-technical staff | 1 (T10) | 0 | 1 |
| 29 | UK date format | 0 | 1 (T16) | 1 |

#### Feature-by-feature evidence (paraphrased voices with links)
- **Cost.**
  - A school was hit by a vendor going under, which took all its tickets and knowledge base. Looking for one product covering facilities, HR and IT, they found everything charges per agent per month at far more than before (para., gwendes, Oct 2024) — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - One buyer was irritated by competitors' paid "optional extras" (para., StephenPink, Sep 2024) — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - Freshservice keeps changing its prices and sells add-ons hard; assets cost extra above 100 items (para., TechMonkey, Jun 2025) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - The ServiceNow quote capped automation transactions, the same complaint as with Jira (Shaun_Dark_Lord, Jun 2025) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - One poster won't pay tens of pounds a month when free routes exist (para., jthompson, Jun 2026) — [T10](https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/)
  - A school can no longer afford Lansweeper (Bionic, Nov 2025) — [T15](https://www.edugeek.net/forums/topic/225193-looking-for-an-asset-managemnet-system/)
- **Simplicity.**
  - A primary-school OP wanted only a way to raise a ticket and close it when fixed, not asset registers, detailed reports or statistics (para., Torchwood, Apr 2023) — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
  - Jira's free tier is capable but a big headache to set up (dmj, 2024) — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - GLPI tickets felt fiddly, with hard-to-read email templates (jthompson, Jun 2025) — [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/)
  - Self-hosted Zammad gets about 95% of the way to replacing Jira but adds to the lead's workload (Shaun_Dark_Lord, Mar/May 2026) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - A MAT poster mocked feature creep (change and project management) in enterprise ITSM pitches (PotNoodleTech, Sep 2023) — [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/)
- **Email-to-ticket.**
  - An OP left Parago because staff kept emailing techs directly. Forwarded tickets were then logged under the tech's name, and Parago could not change the originator. Changing the originator was a hard requirement for any replacement (Torchwood, Apr 2023). Freshdesk fixes this by raising a forwarded email under the original sender (jthompson). osTicket needed OAuth2 work after Microsoft's mail-auth changes (FragglePete) — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
  - A DIY Lists helpdesk was challenged because a real helpdesk needs mailbox monitoring, auto-responses, templates and CC handling (para., jthompson, Nov 2024) — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - An osTicket VM raises tickets from emails to a 365 mailbox (KK20, Sep 2026). Any FOSS option must be easy to connect to the M365 mailbox (itskdog, Jun 2026) — [T10](https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/)
  - Counter-view: one school accepts tickets only through the portal so staff must give a room number (sideone, Nov 2024) — [T1 p2](https://www.edugeek.net/forums/topic/219505-helpdesk-options/page/2/)
- **SSO / identity.**
  - A 19-school trust (a Google shop) requires Google SSO — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - Halo praised for Azure sign-in plus user sync — [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/)
  - GLPI wired into O365 for SSO and user creation — [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/)
  - Older: no Google SAML login was one complaint against Every (2020) — [T7](https://www.edugeek.net/forums/topic/195679-helpdesk-system-every/)
- **Multi-site / multi-department.**
  - A 19-school trust wants several helpdesks in one instance with one manager dashboard, plus a front end where users see only their relevant request types — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - Putting facilities on the same system turned it into a one-stop shop and reduced the "please log it" repetition (slugshead, Jan 2022). Others are adding site staff, science/DT technicians, reprographics and admin (IrritableTech). IT ends up coordinating other teams unless they share the tool (dhicks) — [T12](https://www.edugeek.net/forums/topic/205757-getting-staff-to-log-tickets/)
  - Older: a MAT runs osTicket for 16 schools. The trust IT head sees all IT queues, the exec head sees IT plus facilities plus reporting, and tickets can move between departments (Oaktech, 2018) — [T8](https://www.edugeek.net/forums/topic/174679-multi-school-helpdesk-system/)
- **Assets and loans.**
  - A school leaving IRIS values QR codes on mobile devices (itgeek, Jan 2026). Snipe-IT is used for quick, simple device loans; GLPI's agent inventory is central to fleet management; one tech built an ad-hoc loans app that updates GLPI by API; loan agreements are scanned and attached to assets; the Civica EdOps mobile app became slow after losing local caching — [T14](https://www.edugeek.net/forums/topic/225733-it-asset-management-software/)
  - Wanted fields: location, purchase date, last PAT test, custom fields (e.g. IMEI). Snipe-IT is the most-voted option, GLPI second — [T15](https://www.edugeek.net/forums/topic/225193-looking-for-an-asset-managemnet-system/)
  - A school customised Snipe-IT to import staff and students from its MIS (XamPro, Apr 2026, snippet) — [T19](https://www.edugeek.net/forums/topic/226391-asset-management-query/)
  - Techs building their own helpdesk include trackable assets, a loans system, a parent-responsibility form for take-home laptops, M365 integration and Bromcom user sync (mukz, Jun 2026) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- **Mobile.**
  - ManageEngine's iOS/Android app is useful when out and about (chris11256, 2023) — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
  - Freshdesk's mobile app cited as a plus (2022, 2023) — [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/), [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/)
  - The 19-school trust lead was not fussed about an app — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- **Automation.**
  - Auto-assign to a technician, excluding others, suits split 1st/2nd/3rd-line teams (Davit2005); Freshservice automator rules praised (timbo343) — [T1](https://www.edugeek.net/forums/topic/219505-helpdesk-options/)
  - SupportPal macros plus an API to raise tickets from other systems (IrritableTech, 2022) — [T6](https://www.edugeek.net/forums/topic/206184-ticketing-system/)
- **Knowledge base and AI.**
  - The trust wanted KB integration only for the small share of users who self-serve (Shaun_Dark_Lord) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - On AI agents (May 2026): one poster saw them as a new version of FAQs and knowledge bases, well meant but with almost no impact (synaesthesia). A former school tech now elsewhere runs Jira's Rovo AI agent out of hours only, reviews its answers each morning, and notes the alternative is no answer until the desk opens (jmak) — [T11](https://www.edugeek.net/forums/topic/226464-ai-agents/)
- **Teams.** Desk365 chosen partly because it syncs with 365/Teams (DalekSec, May 2025). Older: Teams channel used as a helpdesk (2020); Teams plus Lists plus Power Automate helpdesk (2021) — [T3](https://www.edugeek.net/forums/topic/223340-replacement-for-spiceworks-help-desk/); [T18](https://www.edugeek.net/forums/topic/195210-free-helpdesk/); [T17](https://www.edugeek.net/forums/topic/200799-helpdesk-software-for-site-team/)
- **SLAs.** SLAs set per request type, with few missed deadlines and those having reasons (19-school trust) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/). An informal SLA, with urgent issues handled within 15 minutes of logging, used to win staff over (psydii) — [T12](https://www.edugeek.net/forums/topic/205757-getting-staff-to-log-tickets/)

### Inferences (ranked recommendations for EduHelpdesk; frequency × intensity)
1. **Free, with no technician-seat cap and no paid modules** (assets, automation, multi-site included). Cost is the most frequent theme and the most emotional, and it is driving 2024–2026 switching.
2. **Painless install, upgrade and email setup.** Setup and maintenance pain is the main objection to the free self-hosted rivals (osTicket, GLPI, Zammad), so a guided installer and in-place upgrades are differentiators.
3. **Email-to-ticket on the school's own M365 mailbox via OAuth:** reply-by-email threading, CC handling, and forwarded mail logged under the original sender. **Flag:** the project memory index says the earlier EduHelpdesk ticketing interview "ruled out email". The UK forum evidence makes this the third most common want, so the decision may need revisiting, or at least a "forward to helpdesk" path.
4. **M365/Entra SSO plus Google Workspace SSO, MFA, and directory/user sync.**
5. **Multi-site/MAT and multi-department queues** (IT, site, HR, reprographics) with scoped visibility, a central overview and cross-desk reassignment.
6. **Ticket-to-asset linkage** with QR scanning, loans and custom fields, or import/sync from Snipe-IT/GLPI, since many schools already run them.
7. **Adoption tools:** low-friction intake (email plus a short form), techs logging on behalf of users, required room/location, auto-chase then auto-close abandoned tickets, and a weekly summary for the head.
8. **Data ownership:** full export, imports from Spiceworks/Freshdesk/osTicket, local/UK data.
9. **A mobile-friendly technician UI;** offline tolerance for asset scanning.
10. **Rules, templates and auto-assign with no usage limits.**
11. **Reports and dashboards for heads/MAT leads.** Direct demand is low, but techs use reports as leverage.
12. **Knowledge base, Teams and AI:** low or mixed demand, so nice-to-haves.

### Gaps
No helpdesk thread I read gave evidence on these brief items:
- Intune/Google Admin device sync into the helpdesk (only GLPI agent inventory)
- Wonde/Groupcall MIS sync (only Bromcom and a generic "MIS import")
- Recurring/scheduled tasks
- Canned replies (only "templates" mentions)
- Laptop trolley/iPad cart management
- Printer/consumables tracking (GLPI network-scans printers, but nothing on consumables)
- Projector/AV fault tracking
- Exam-season/term-start workload
- Joiners/leavers (only a 2019 request for checklist templates for account creation, T16)
- Budget/cost tracking

These may matter, but they were not voiced in helpdesk-selection threads. Targeted searches might find them in other EduGeek sub-forums.

## 4. Pain points: with current tools and with requesters

### Takeaway
There are two clusters of pain:
- **Tools:** per-agent pricing, free tiers withdrawn, paid extras, setup and maintenance burden of free self-hosted options, poor M365 mail and auth support (Spiceworks), clunky premises suites imposed by trusts, and vendor outages or collapse.
- **People:** staff who won't log tickets (corridor requests, direct emails, phone) and tickets that lack detail or get no reply. Techs agree the fix is SLT backing plus fast service for logged tickets, but the tool can lower the friction.

### Cited Findings
- **Staff not logging tickets (Jan 2022, 23 replies).**
  - A primary tech offered a form and email, desktop shortcuts and staffroom posters, yet staff still didn't log. Refusing to work without tickets meant nothing got done (tmoon-mint).
  - Common responses: "no ticket, no job" (several posters); get the head and SLT to push it in staff meetings (EssentialRug, Tom_P, slim1986); without SLT backing it never works (paulkerton, Jan 2022).
  - Further tactics: fix logged issues first and fast; log corridor requests yourself and confirm by email; report abandoned tickets as evidence (psydii). Staff also fail to reply with more information or to confirm fixes (Davit2005).
  - [T12](https://www.edugeek.net/forums/topic/205757-getting-staff-to-log-tickets/)
- **Direct email bypass.** Users kept emailing support staff directly despite being asked not to (Torchwood, Apr 2023) — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
- **First-week advice for a new network manager (Feb 2025):** set up a helpdesk from the start to end post-it notes and corridor grabbing (PotNoodleTech; search snippet) — [T20](https://www.edugeek.net/forums/topic/221572-what-are-your-top-priorities-for-your-first-week/)
- **Long-running issue (older).** EduGeek search turns up corridor-request complaints in threads from 2006–2014 (e.g. "Getting staff to use an online helpdesk", 2008). In older replies, SLT backing is the recurring fix — [EduGeek search "corridor helpdesk"](https://www.edugeek.net/search/?q=corridor%20helpdesk&quick=1&type=forums_topic&search_and_or=and&sortby=relevancy)
- **Forms vs email.** A school joining a trust dropped ManageEngine for osTicket because staff disliked long forms (ITGURU, 2019, older) — [T16](https://www.edugeek.net/forums/topic/186584-looking-for-helpdesk-software-for-my-team/). This sits against sideone's portal-only, room-number-required approach (2024) — [T1 p2](https://www.edugeek.net/forums/topic/219505-helpdesk-options/page/2/)
- **Confusing notifications.** The Parago/EdOps helpdesk's notification style would confuse some staff, and its Helpdesk V2 slipped to next year (itskdog, Jun 2026) — [T10](https://www.edugeek.net/forums/topic/226668-freshdesk-upcoming-but-undocumented-end-of-free-plan/)
- **Premises suites imposed for IT.**
  - Every: called clunky, slow and missing basic ticketing features (several posters); premises staff liked its contract reminders. Parago for a MAT was jack of all trades, master of none, and the team moved to free Freshdesk plus SharePoint/Power Apps for inventory (mwnci). All 2020, older — [T7](https://www.edugeek.net/forums/topic/195679-helpdesk-system-every/)
  - Every accepts requests only through its portal, not by email (Griff, 2021, older) — [T17](https://www.edugeek.net/forums/topic/200799-helpdesk-software-for-site-team/)
- **Reliability.**
  - Jira SM: consistent reliability issues and odd design changes (Jun 2025); still described as glitchy, with queues vanishing for 30 minutes (jmak). By Mar 2026 the trust's lead said replacing it was no longer optional — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
  - Spiceworks Cloud login and email issue (Jan 2026) — [T13](https://www.edugeek.net/forums/topic/225520-spiceworks-cloud-helpdesk/)
- **Data hosted outside the UK.**
  - A small primary was told to keep the helpdesk local because of where data is stored; others argued O365 mail is already cloud-hosted (Aug 2022) — [T4](https://www.edugeek.net/forums/topic/208957-spiceworks-helpdesk/)
  - A MAT required GDPR compliance for any cloud option (2023) — [T5](https://www.edugeek.net/forums/topic/214671-mat-helpdesk-recommendations/)
  - A UK vendor eases legal headaches (2025) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- **Workload context (2023 survey of school IT staff in England).** Salamander and the ANME surveyed 3,600+ IT staff. Findings:
  - a quarter consider their workload unacceptable
  - nearly 40% feel undervalued
  - over 40% of organisations are not working with their team to reduce workload pressure
  - about 20% of school IT staff work alone
  - Secondary summary on a sponsor's blog; the full report was not seen — [SalamanderSoft/ANME](https://www.salamandersoft.co.uk/blog/anme-sponsor/)

### Inferences
- A helpdesk cannot fix the culture, but it can lower friction and give techs evidence:
  - accept email, so staff never need to "learn the portal"
  - let techs log a corridor request in seconds, on the user's behalf, with an automatic confirmation email
  - auto-chase and auto-close tickets with no reply
  - give the head a weekly summary
- Since about 20% of school IT staff work alone (ANME), single-tech schools need a tool that runs with near-zero admin.
- "MSP-imposed tools" is a real but low-frequency complaint on EduGeek (2 recent threads). Primaries on MSP contracts (63%) are under-represented there.

### Gaps
- No UK data found on the share of requests that arrive outside the helpdesk, on vague-ticket rates, or on after-hours demand. Only one 2026 anecdote addresses out-of-hours (T11).

## 5. What SLT / business managers want from IT helpdesk reporting

### Takeaway
There is little direct evidence of what SLT, governors or business managers ask for. In the forum evidence, reporting mainly serves techs: proof of workload, defence against complaints that something wasn't done, and per-site time accounting. MAT leads want a single overview dashboard across schools and departments.

### Cited Findings
- **Reports as evidence.**
  - A primary tech planned to send the head a weekly ticket report so low logging is visible (tmoon-mint)
  - Helpdesk records are evidence either way when someone complains (robyholmes)
  - Reporting abandoned tickets has helped explain why jobs weren't done (psydii)
  - One tech's site pays by the hour, so it insists on strict time accounting (Oaktech)
  - All Jan 2022 — [T12](https://www.edugeek.net/forums/topic/205757-getting-staff-to-log-tickets/)
- **MAT overview.** Managers want a single dashboard covering multiple helpdesks (Jun 2025) — [T9](https://www.edugeek.net/forums/topic/223860-helpdesk-recommendations-to-replace-jira-service-management/)
- **Older.**
  - An exec head views both IT and facilities queues plus the reporting module (2018) — [T8](https://www.edugeek.net/forums/topic/174679-multi-school-helpdesk-system/)
  - Power BI gives an overview of asset and audit data the team lacked in other tools (2020) — [T7](https://www.edugeek.net/forums/topic/195679-helpdesk-system-every/)
- **Counter-view.** Some small schools explicitly don't want detailed reports or statistics (2023) — [T2](https://www.edugeek.net/forums/topic/212531-free-helpdesk-recommendations/)
- **DfE digital and technology standards progress** is tracked by the DfE survey, not by schools' helpdesks. 81% of IT leads are aware of the standards (72% in 2023), and 25% of aware IT leads say they meet them all (16% in 2023) — [DfE TiSS 2024-25, ch. 2](https://assets.publishing.service.gov.uk/media/692834a6ce50d215cae9610e/Technology_in_schools_survey_2024_to_2025_research_report.pdf)

### Inferences
- The most defensible SLT reports are simple:
  - tickets by school, department and category
  - response and resolution times against an informal SLA
  - open and abandoned counts
  - time spent per site (MATs and MSP-style central teams)
  - a one-page weekly or termly summary
- Linking helpdesk categories to DfE standards (e.g. filtering, cyber, backups) could help IT leads report progress. This is an idea, not a voiced demand.

### Gaps
- No source found on what governors, business managers or SLT actually request from helpdesk reports. Nothing found on tickets by department or on evidence for governors.
- No BESA or Education Technology report on helpdesk reporting was found.

## 6. Surveys and reports on UK school IT support

### Takeaway
The DfE Technology in Schools Survey 2024-25 (published Nov 2025) is the best official data. Since 2023, primaries have shifted sharply to managed service providers (63%, up from 50%), while secondaries mostly keep in-house staff (87% in-house). "Support calls" (a helpdesk function) is provided to 92–93% of schools. 82% of IT leads are satisfied with their technical support. I found no UK survey specifically about helpdesk software.

### Cited Findings
All figures in this list are from [DfE Technology in schools survey 2024 to 2025 research report (Nov 2025)](https://assets.publishing.service.gov.uk/media/692834a6ce50d215cae9610e/Technology_in_schools_survey_2024_to_2025_research_report.pdf), Chapter 6 (Tables 6.1–6.4), unless noted. Sample: IT leads, 237 primary and 252 secondary.
- **Sources of technical support, 2025 (2023 in brackets):**

  | Source | Primary | Secondary |
  |---|---|---|
  | Managed service provider | 63% (50%) | 41% (31%) |
  | In-house, school-managed staff | 23% (30%) | 70% (72%) |
  | In-house, provided by trust/LA | 32% (36%) | 39% (31%) |
  | Total in-house | 50% (62%) | 87% (86%) |
  | No technical support | 1% | 1% |

- **MSP contracted hours** (schools with an MSP): "no minimum/maximum" fell to 26% for primaries (from 39%) and 47% for secondaries (from 69%). More primaries now have ≤5 hours a month (10%, from 4%). More secondaries have 21+ hours (25%, from 15%).
- **Types of support received (primary / secondary):**
  - Support calls: 92% / 93%
  - Maintenance: 94% / 83%
  - Procurement: 82% / 78%
  - Implementation/disposal: 81% / 73%
  - ICT planning/strategy input: 49% / 64%
  - Lesson support: 17% / 37%
- **Satisfaction:** 82% satisfied and 8% dissatisfied overall.
  - "Very satisfied": 56% primary, 57% secondary
  - Dissatisfied: 9% primary, 4% secondary
  - Consistent with 2023
- **Planned investment in the next 12 months** in "back-office systems and software technical support": 13% primary, 14% secondary (2023: 9% / 18%). In a network management tool: 7% / 14% (Table 8.10).
- **Standards and backups.**
  - 81% of IT leads are aware of the digital and technology standards (72% in 2023)
  - 25% of aware IT leads meet all of them (16%); 20% of all schools are aware and meet all (11%)
  - 14% don't meet them and have no plans to (23% in 2023); 15% are unsure
  - Backups to the 2-device / 1-offsite guideline: 47% primary, 76% secondary; 23% of primary IT leads don't know their backup position
  - Source: ch. 2 and ch. 6 of the same report
- **2022-23 survey:** primary academies were more likely than LA-maintained primaries to have in-house technical support (79% vs 50%) — [DfE Technology in schools survey 2022 to 2023 (Nov 2023)](https://assets.publishing.service.gov.uk/media/655f8b823d7741000d420114/Technology_in_schools_survey__2022_to_2023.pdf)
- **Barriers to technology (2024-25 survey):** 95% of leaders cite budget constraints and 93% the high cost of technology (secondary summary; not checked in the PDF) — [EdTech Innovation Hub summary](https://www.edtechinnovationhub.com/news/dfe-tech-survey-reveals-major-shifts-in-school-ai-use-digital-strategy-and-infrastructure-gaps)
- **ANME/Salamander 2023 wellbeing survey** of school IT staff in England (3,600+ approached): figures as in section 4 — [SalamanderSoft/ANME](https://www.salamandersoft.co.uk/blog/anme-sponsor/)

### Inferences
- The MSP shift in primaries (+13 points in two years) means a growing share of UK primary schools have little say over their helpdesk tool. A self-hosted helpdesk's natural market is secondaries, in-house MAT central teams (32–39% get trust/LA in-house support), and MSP-like trust teams serving several schools.
- High stated satisfaction (82%) fits with forum complaints being about tools and cost, not support quality. Value and simplicity arguments will land better than "better service" arguments.
- Very high budget-barrier figures (95%) support the cost-first ranking in section 3.

### Gaps
- The DfE survey does not ask about helpdesk software, ticket volumes or response times.
- No EduGeek poll on helpdesk tools from 2022–2026 was found.
- No BESA report on school IT support or helpdesks was found.
- The ANME survey's full report (response count, methodology) was not seen; only a sponsor summary.
- LGfL/edtech conference talks on helpdesks were not found in the time available.
