# DfE guidance: the helpdesk's next job is running the school's compliance calendar

*Research date: 10 October 2026. Builds on [School helpdesk comparison and priorities](School%20helpdesk%20comparison%20and%20priorities.md) (3 October 2026) and only covers what that report missed, what has changed since, or what is still not built.*

## Where EduHelpdesk stands after the EduInventory merge

Since the 3 October report, EduHelpdesk has gained the DfE asset register fields and checks, a contracts register with approved apps and a personal-data flag, an access control register with a termly access review and leaver removal, a compliance summary with DfE-layout exports, and daily bell reminders. That closes the old ranks 3 and 5 and most of rank 8.

The DfE standards change log has no entries after 16 September 2026, so the standards themselves have not moved. What *has* moved is the guidance around them:

- **DfE Cyber Security Hub**: incident record-keeping, a response-plan checklist, and playbooks (ransomware page reviewed 17 July 2026).
- **Data protection in schools**: a new EdTech procurement section (9 July 2026) and a filtering and monitoring update (17 June 2026).
- **Data (Use and Access) Act 2025**: a statutory data protection complaints duty from 19 June 2026.
- **JCQ Instructions for conducting examinations 2026-27**: detailed IT requirements for word-processed and on-screen exams.
- **KCSIE 2026**: annual filtering and monitoring review across all devices; cyber security treated as safeguarding.

Almost every remaining DfE obligation that touches IT has the same shape: **something must happen on a schedule, someone must sign it off, and there must be evidence**. EduHelpdesk already has templates, checklists, attachments, a nightly scheduler, the bell and the compliance summary. The single most valuable addition is to join those into a compliance calendar (rank 1), then add the three case types the guidance describes in detail: cyber incidents, information rights requests, and new app approvals.

## Ranked recommendations

Scoring as before: DfE weight ×2 (statutory or "must" = 3, DfE "should" = 2, good practice = 1) + effort reuse (0–2, higher = more existing code reused).

| # | Recommendation | Score | Obligation served | Effort |
|---|---|---|---|---|
| 1 | **Compliance calendar: recurring tasks with sign-off and evidence** | 8 (6+2) | KCSIE 2026 filtering and monitoring review; cyber core (restore tests, firewall review, DR test); IT support standard annual review; RPA conditions | Medium. Recurring ticket templates on the nightly scheduler, a sign-off step, results on the compliance summary |
| 2 | **Cyber incident and data breach case** with a timed decision log, playbook checklists and reporting checklist | 8 (6+2) | Cyber core (incident log, near misses); UK GDPR Art 33 (72-hour ICO clock, breach record); Cyber Security Hub record-keeping; RPA response plan | Medium. A restricted category plus a structured log on the ticket |
| 3 | **Information rights requests**: SAR, FOI and data protection complaints with the right legal clocks | 8 (6+2) | UK GDPR Art 15 (one calendar month); FOIA (20 school days); DUAA s103 / DPA s164A (acknowledge complaints within 30 days, from 19 June 2026) | Medium. Calendar-month and school-day clocks; a "systems searched" list drawn from the access register |
| 4 | **New app and EdTech approval workflow** feeding the contracts register | 7 (4+3) | Data protection in schools: EdTech procurement (9 July 2026); cyber core (approved apps list); filtering standard (new AI tools trigger a review); generative AI product safety standards | Low–medium. A portal request, a checklist template, DPO/DSL sign-off, then "Add to contracts register" with Approved app ticked |
| 5 | **Filtering unblock and concern requests** with DSL/SLT approval recorded | 7 (6+1) | Filtering and monitoring standard (SLT-approved exceptions, staff report concerns); KCSIE 2026 | Low–medium. A portal form, an approver, an exceptions list with review dates |
| 6 | **Exam-season IT readiness** checklist per room and device | 6 (4+2) | JCQ ICE 2026-27 (word processors, on-screen tests) | Low. A recurring template; device rows generated from an asset group |
| 7 | **Electrical safety (PAT) as an asset check type** with pass/fail and removal from use | 6 (4+2) | Electricity at Work Regulations 1989; HSE HSG107 | Low. Asset checks already exist; add a check kind and a result |
| 8 | **Cyber response plan readiness panel** | 6 (4+2) | Cyber Security Hub response-plan checklist; RPA condition | Low. A settings page with contacts and dates, warnings on the compliance summary |
| 9 | **Training, AUP and RPA register on People** (old rank 21) | 6 (6+0) | RPA conditions (NCSC training); cyber core (governor training) | Low–medium. People now has types including governor, which makes this easier than before |
| 10 | **Privileged access approval** on the access register | 5 (4+1) | Cyber core (admin accounts approved and reviewed) | Low. An "admin" flag on a grant requires an approver and date |
| 11 | **Knowledge base and known-issues banner** (old rank 7) | 5 (4+1) | IT support standard (self-service, plain-English guides); KCSIE (every staff member knows how to report a filtering concern) | Medium |
| 12 | **Assistive technology requests and loans** | 4 (2+2) | DfE assistive technology training and lending-library pilot; SEND Code of Practice | Low. Mostly configuration: a catalogue item, a loan kit type, a "learner need" reason code |
| 13 | **Disposal route and sustainability figures** | 3 (2+1) | DfE sustainability and climate change strategy (climate action plan, digital infrastructure); WEEE | Low. A reuse / donate / recycle field on disposals and a yearly count |
| 14 | **Biometric consent register** | 3 (3+0) | Protection of Freedoms Act 2012 ss26–28; DfE biometrics guidance | Medium, and only if the school uses biometrics. Usually better kept in the MIS or catering system |

Martyn's Law and the mobile phones guidance are covered by rank 1 (drills and system tests as recurring tasks) rather than needing their own features.

## 1. Compliance calendar

**What the guidance asks for.** The obligations below are each a recurring task that needs a named person, a date and evidence:

| When | Task | Source | Evidence |
|---|---|---|---|
| Every term | Test-restore a backup and log it | Cyber core standard; RPA condition | Date, what was restored, success, who |
| Every term | Access review (**built**) | Cyber core standard | Already on the access register |
| Every term | Asset and contracts register review | Leadership core standard; Plan technology | "Register reviewed" stamp |
| Every term | Firewall rule review | Cyber core standard | Notes, changes made |
| Yearly | Filtering and monitoring review across all device types and locations | KCSIE 2026; filtering and monitoring standard | Checks per device and location, DSL and SLT sign-off |
| Yearly | IT support standard review (with staff feedback) | IT support standard | Review document, governor report |
| Yearly | Cyber response plan test (tabletop exercise) | Cyber Security Hub; cyber core | Exercise notes, actions |
| Yearly, by 1 Sept | NCSC training for staff and governors renewed | RPA conditions | Training dates (rank 9) |
| Every 1–2 years | PAT / electrical inspection | HSE HSG107 | Per-asset results (rank 7) |
| Each exam series | Exam IT readiness | JCQ ICE | Per-room checklist (rank 6) |
| Yearly or after change | Lockdown and invacuation drill, including PA, alert and comms systems | Martyn's Law (Terrorism (Protection of Premises) Act 2025; standard tier for 200+ people; enforcement expected around spring 2027) | Drill date, systems tested, faults raised as tickets |
| Yearly | Data protection audit, ROPA and retention schedule review | Data protection in schools: record keeping | DPO sign-off |

**How it fits EduHelpdesk.** The old rank 4 proposed recurring tickets. Now that the compliance summary and daily reminders exist, the shape is clearer:

- A **Compliance calendar** page under the Compliance menu, seeded with the tasks above, each with an interval (term, year, custom), an owner role, a ticket template and the evidence it needs.
- The nightly scheduler opens a ticket from the template when a task falls due. The checklist is the template's tasks; evidence goes on as attachments.
- Closing it needs a **sign-off** (name, role, date). DSL for filtering, SLT digital lead for the response plan, the business manager for registers.
- Overdue tasks show on the compliance summary and ring the bell, using the existing ComplianceFindings and SendComplianceReminders.
- The filtering review generates one check row per device type and location from the asset register, which KCSIE 2026 now expects ("all devices").
- Term dates (old rank 14) would let "every term" mean the real term rather than a fixed four months. Without them, use fixed dates the school edits.

The tasks should be editable, not hard-coded, because DfE revises these often.

## 2. Cyber incident and data breach case

**What the guidance asks for.** The DfE Cyber Security Hub sets out:

- **Incident phases**: detection, containment, eradication, recovery, post-incident review.
- **What to record for every action**: time (24-hour clock), the action, any decision with its rationale, who did it, evidence kept, and **negative findings** (what was checked and found clean).
- **Separate logs**: initial report, timeline, communications log, decision log, forensic log, data breach records (DPO and ICO), meeting notes, post-incident review, follow-up actions.
- **Immediate notifications**: SLT digital lead, incident lead, IT provider, safeguarding lead where relevant; the RPA if a member; police and Report Fraud (0300 123 2040) for financial or safeguarding impact.
- **Playbooks** for ransomware, business email compromise and AI-enabled extortion.

The data protection guidance adds: record every breach however small; assess risk from the affected people's point of view; notify the ICO within 72 hours where there is a risk; tell the people affected; document lessons learned, including near misses.

**How it fits EduHelpdesk.**

- A restricted **Security incident** category, visible only to roles with a new permission, with its own ticket template.
- A structured **incident log** on the ticket: each entry has a time, kind (action, decision, communication, evidence, negative finding), who, and text. Decisions require a rationale. This is different from comments: entries can't be edited after saving, and the log exports as a timeline.
- **Clocks**: "became aware at" sets a 72-hour ICO deadline, shown prominently, independent of the SLA.
- A **reporting checklist**: ICO (with reference), RPA, police, Report Fraud, DfE sector reporting, parents and staff, each with "not needed" as a valid answer with a reason.
- **Playbook checklists** as templates (ransomware, BEC, lost device), editable by the school.
- **Links**: affected assets, affected people, affected systems from the access register. Marking an asset lost or stolen prompts "Is this a data breach?".
- A **near miss** tick, and a post-incident review section that becomes follow-up tickets.
- A **breach register** view and export for the DPO.

## 3. Information rights requests

**What the guidance asks for.**

- **Subject access requests**: one calendar month from receipt (to the same date next month, or the last day if shorter; weekends and bank holidays roll to the next working day; school holidays do *not* extend it). The clock can stop while waiting for ID or clarification, and can extend by up to two more months for complex requests if the requester is told within the first month. Record: date received, pauses and reasons, systems searched and what was found, what was redacted and why, the response and its date, and any refusal or exemption with reasons.
- **Freedom of information**: 20 school days (or 60 working days if sooner).
- **Data protection complaints (new)**: since 19 June 2026 every controller must make complaining easy (an electronic form is the example given), acknowledge within 30 days, investigate proportionately, keep the complainant updated, give the outcome without undue delay, and keep records.

**How it fits EduHelpdesk.** These are usually handled by the DPO or business manager, but schools often run them on spreadsheets and miss the clocks.

- A restricted **Information request** category with types SAR, FOI, complaint, other rights (rectification, erasure, objection).
- **Legal clocks** instead of SLA clocks: calendar month with the end-of-month rule, school days for FOI (needs term dates), 30 days for complaint acknowledgement. **Stop the clock** and **extend** actions that record a reason and recalculate the due date.
- A **search log** listing systems searched, pre-filled from the access register's systems so nothing is forgotten, each marked found, nothing found or not applicable.
- A **redaction record** and the final response as an attachment, kept with the unredacted version.
- A **portal form** for data protection complaints and SARs from staff, which meets the "make complaining easy" duty for staff at least.
- EduHelpdesk already has a subject access export for its own data; it becomes one line in the search log.

## 4. New app and EdTech approval

**What the guidance asks for.** DfE's EdTech procurement section (9 July 2026) lists what to check *before* a school starts using a product: whether a DPIA is needed, data categories with a justification for each, lawful basis, data flows and whether the supplier is a processor or controller, sub-processors, a signed data processing agreement, storage location and international transfers, retention and an exit plan, security (encryption, authentication, audit logs, certifications) reviewed by IT, support for data subject rights and breach notification, and for **AI features** whether pupil data trains the model and how outputs are moderated. The DPO and DSL should both be consulted and the approval decision recorded. The generative AI product safety standards and the filtering standard add that new AI tools should trigger a filtering and monitoring review.

**How it fits EduHelpdesk.** Teachers already ask the helpdesk "can I use this app?". Make that a process:

- A **Request a new app** portal tile.
- An **app approval** template whose checklist follows the DfE list, with DPO, DSL and IT sign-offs (uses rank 9 of the old report, approvals, or a simpler sign-off section).
- Outcomes: approved, approved with conditions, refused. Approval offers **Add to contracts register** with *Approved app* and *Processes personal data* ticked, a review date, and the DPIA attached, as approved projects already do.
- A public **approved apps list** in the portal so staff can check before asking. This also answers the cyber standard's approved applications requirement.

## 5. Filtering unblock and concern requests

The filtering and monitoring standard expects exceptions to be approved by SLT and reviewed, and staff to know how to report a concern. KCSIE 2026 reinforces this.

- A **Website unblock** portal form: URL, reason, who it's for, how long. Approval by the DSL or a named SLT member, recorded on the ticket. Approved exceptions go on an **exceptions list** with an expiry date that feeds the annual review in rank 1.
- A **Report a filtering concern** tile that signposts the DSL and captures only the minimum (old rank 20). Safeguarding details belong in the school's safeguarding system, not the helpdesk.

## 6. Exam-season IT readiness

JCQ's 2026-27 instructions set out what IT staff must do for word-processed and on-screen exams. For **word processors**: spell check, grammar check, predictive text and **AI tools disabled in the application, the operating system and the network**, and the set-up tested; devices cleared of stored data; no internet or network access to other files; autosave set by the technician; batteries charged and checked. For **on-screen tests**: the latest version of the exam platform, a password per session, a spare PC, software tested beforehand, a control-centre PC monitored by IT, technical help available throughout, and the seating plan recording which device each candidate used. Afterwards, candidates' access to their exam work areas is removed, and Bluetooth hearing aids are unpaired.

- An **Exam series** template with a room-by-room checklist and one row per exam device, generated from an asset group or tag (for example "Exam laptops").
- A **device-to-candidate** record per session is the exams officer's job; the helpdesk only needs to record which devices were prepared and checked.
- Exam days as a **known-issues banner** and a priority rule: faults in an exam room go straight to the top.

## 7. Electrical safety on assets

The Electricity at Work Regulations 1989 require electrical equipment to be maintained safe; HSE's HSG107 guidance says the frequency depends on risk, and schools commonly use visual checks plus a formal test every one to two years for portable kit. Failed items must be labelled and taken out of use.

Asset checks already have a next-check date and bulk "Record a check". Add a **check kind** (general, PAT/electrical, other), a **pass/fail result**, the tester and a certificate attachment. A fail sets the asset to out of use and opens a ticket. A PAT report by room helps the contractor plan a visit.

## 8. Cyber response plan readiness

The Cyber Security Hub's response-plan checklist asks for: a recovery team led by the SLT digital lead with deputies, contact details for the team, IT provider, RPA, police and DPO, escalation criteria, when IT may act without waiting for authorisation, and **printed copies** kept off-site because systems may be down.

- A **Response plan** settings page holding those contacts and the plan document, with "last reviewed", "last tested" and "printed copies updated" dates.
- Warnings on the compliance summary when the plan is older than a year or untested, and the annual test as a rank 1 task.
- A **print view** of the contacts and first steps, since the helpdesk itself may be unavailable during an incident.

## 9–14. Smaller additions

- **Training, AUP and RPA register (9).** Dates per person for NCSC training and AUP acceptance, an expiry report before 1 September, and a readiness panel with the Police CyberAlarm reference. People now include governors and contractors, which this needs.
- **Privileged access approval (10).** On the access register, an *admin rights* tick that requires an approver and approval date, and highlights admin grants at the termly review.
- **Knowledge base (11).** Unchanged from the 3 October report; add DfE's staff-facing topics plus "how to report a filtering concern" and "can I use this app?".
- **Assistive technology (12).** DfE has published AT training materials and trialled AT lending libraries. A catalogue item for AT requests and an AT loan kit type, with a "learner need" reason code, cover most of it with configuration.
- **Disposal route (13).** Recording reuse, donation, refurbishment or recycling on disposals gives the figures a climate action plan's digital section asks for. Waste transfer notes are already attachments.
- **Biometric consent (14).** Only relevant if the school uses biometrics; consent records usually belong in the MIS or the catering system. Offer a simple per-person consent date and objection flag only if asked.

## Suggested order

1. **Compliance calendar** (rank 1) with the response plan readiness page (rank 8): the backbone the other items plug into.
2. **Cyber incident and breach case** (rank 2): the highest-stakes gap, and DfE now spells out exactly what to record.
3. **Information rights requests** (rank 3): the DUAA complaints duty is already in force.
4. **App approval and filtering unblock requests** (ranks 4–5): both use a sign-off step, so build them together.
5. **Exam readiness and PAT** (ranks 6–7): cheap once the calendar exists.
6. The rest as wanted.

Restricted categories (ranks 2, 3 and 5) need a category-level visibility permission. Building that once, with the first of them, makes the others cheap.

## Previously ruled out, not re-proposed

Email-to-ticket, canned replies, time tracking, auto-chase/auto-close and first-response targets were ruled out by the owner and are left out here. Nothing in the newer guidance changes the case for them.

## Sources

- DfE, [Meeting digital and technology standards in schools and colleges](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges), including the [cyber security core standard](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/cyber-security-core-standard)
- DfE Cyber Security Hub: [home](https://cyber-security-hub.education.gov.uk/), [incident response process](https://cyber-security-hub.education.gov.uk/incident-response-process), [record keeping in an incident](https://cyber-security-hub.education.gov.uk/record-keeping-in-an-incident), [getting your cyber response plan ready](https://cyber-security-hub.education.gov.uk/getting-your-cyber-response-plan-ready), [ransomware playbook](https://cyber-security-hub.education.gov.uk/ransomware-playbook), [ransomware reporting](https://cyber-security-hub.education.gov.uk/ransomware-reporting), [Risk Protection Arrangement](https://cyber-security-hub.education.gov.uk/risk-protection-arrangement)
- DfE, [Data protection in schools](https://www.gov.uk/guidance/data-protection-in-schools): [procuring EdTech](https://www.gov.uk/guidance/data-protection-in-schools/procuring-educational-technology-edtech), [managing breaches of data](https://www.gov.uk/guidance/data-protection-in-schools/managing-breaches-of-data), [dealing with SARs](https://www.gov.uk/guidance/data-protection-in-schools/dealing-with-subject-access-requests-sars)
- [Data (Use and Access) Act 2025, section 103](https://www.legislation.gov.uk/ukpga/2025/18/section/103/enacted) (complaints to controllers); [CMS summary: complaints rules from 19 June 2026](https://cms.law/en/gbr/legal-updates/data-use-and-access-act-2025-new-statutory-rules-on-handling-data-protection-complaints-from-19th-june-2026)
- DfE, [Keeping children safe in education](https://www.gov.uk/government/publications/keeping-children-safe-in-education--2); [SWGfL summary of KCSIE 2026](https://swgfl.org.uk/magazine/keeping-children-safe-in-education-2026-what-do-schools-need-to-know/)
- DfE, [Generative AI product safety standards](https://www.gov.uk/government/publications/generative-ai-product-safety-standards)
- JCQ, [Instructions for conducting examinations](https://www.jcq.org.uk/knowledge-hub/intructions-for-conducting-examinations/)
- DfE, [Protection of biometric information of children in schools](https://www.gov.uk/government/publications/protection-of-biometric-information-of-children-in-schools)
- Browne Jacobson, [Martyn's Law: practical guidance for schools in England](https://www.brownejacobson.com/insights/martyns-law-practical-guidance-for-schools-in-england)
- HSE, Electricity at Work Regulations 1989 and HSG107 *Maintaining portable electrical equipment*
- DfE, Sustainability and climate change strategy for education (climate action plans)
- DfE assistive technology training materials and lending-library pilot (reported by [Tes](https://www.tes.com/magazine/news/general/dfe-edtech-plans-for-schools-mandatory-assistive-tech-training-all-new-teachers-2025))

Not verified: the exact DfE sector incident-reporting route for maintained schools (the Hub's notification page returned 404), and any hour-based deadline for contacting the RPA.
