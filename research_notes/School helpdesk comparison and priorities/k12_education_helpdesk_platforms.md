# Education-specific (K-12) IT helpdesk and asset platforms: school-specific features generic helpdesks lack

Research date: 2026-10-03. Scope: US-led K-12 products, with notes on AU/NZ/Canada. Focus: features, workflows and design ideas that could carry over to a self-hosted UK school helpdesk (EduHelpdesk).
Source note: incidentiq.com, yespress.io, businesswire.com, morningstar.com and boarddocs.com all returned HTTP 403 to the fetch tool. Where Incident IQ facts are cited to incidentiq.com, the wording comes from the search engine's indexed copy of that vendor page, not from a full read of it. Vendor claims are marked as vendor claims.

## Q0. Which purpose-built education platforms exist in 2026, and who owns them?

### Takeaway
The US K-12 market has one dominant "school operations platform" (Incident IQ, PE-backed and expanding beyond IT) and a long tail of cheaper 1:1-focused tools (Manage1to1, One To One Plus, VIZOR, GoGuardian Fleet, Asset Panda, Follett Destiny Resource Manager + Destiny Help Desk). Brightly/SchoolDude now belongs to Siemens and is about facilities, not IT. Generic ITSM tools (TeamDynamix, Freshservice, Zendesk, KACE, Spiceworks) sell into schools but have no student-device or guardian workflows built in. New in 2025-26: a K-12 AI-first SaaS (SupportStudioK12) and an open-source, self-hosted K-12 stack (Chalk). Both are worth benchmarking because, like EduHelpdesk, they can be self-hosted.

### Cited Findings
- **Incident IQ (iiQ)**: in Feb 2024 Cove Hill Partners joined JMI Equity and the founders as investors (amount not disclosed). At that point iiQ was "used by over 1,500 school districts" across IT, facilities, HR and other operations — [FinSMEs](https://www.finsmes.com/2024/02/incident-iq-receives-strategic-investment-from-cove-hill-partners.html); [Incident IQ announcement](https://www.incidentiq.com/newsroom/announcements/incident-iq-to-accelerate-growth-with-strategic-investment-from-cove-hill-partners)
- Incident IQ revenue estimates conflict: Latka claims $21.3M revenue and 194 staff in 2025 ([Latka](https://getlatka.com/companies/incidentiq.com)), while a 2026 profile is headlined as a "$40 Million Business" ([YesPress](https://yespress.io/incident-iq), not fetchable). Neither is primary.
- Tracxn lists an "M&A offer" for Incident IQ in April 2025 with no details — [Tracxn](https://tracxn.com/d/companies/incident/__GRBSqqNGJztAD-uz7jf2KNeMztIWeQ23ir-z-cxcM8E). Unverified. I found no announcement that iiQ was acquired.
- **Brightly (formerly SchoolDude, founded 1999)** is now "a Siemens Company", and its products are offered under the Siemens Asset Management portfolio — [Brightly blog](https://www.brightlysoftware.com/blog/from-schooldude-to-brightly-25-years-of-building-brighter-futures); [CalSAVE](https://calsave.org/2016/04/26/schooldude/); [Siemens Asset Management login page](https://www.assetmanagement.siemens.com/login). Brightly says more than 7,000 public and private schools, colleges and universities use it — [Brightly](https://www.brightlysoftware.com/resource/facility-asset-management-education)
- The Siemens/Brightly K-12 page now lists only Asset Essentials (CMMS), Energy Manager, Event Manager and Origin (capital planning). No IT help desk or IT asset product is listed — [Siemens Asset Management K-12](https://www.assetmanagement.siemens.com/en-gb/industries/education/k-12)
- **Manage1to1**: founded in 2012 by former school IT staff and employee-owned (ESOP). Claims 2,100+ districts, 18M+ devices and 43 US states — [Manage1to1](https://manage1to1.com/); [Manage1to1 compare page](https://www.manage1to1.com/pricing/compare/)
- **One To One Plus** is K-12 asset management and help desk software "built specifically for schools by former K-12 technology professionals". It is not an insurance product — [One To One Plus](https://onetooneplus.com/). Parent-paid device insurance is a separate category sold by firms such as One2One Risk Solutions, GoCare and Securranty — [GoCare K-12](https://gocare.com/k-12); [Securranty Education](https://securranty.com/Education.aspx); [Slippery Rock SD insurance page](https://www.slipperyrock.k12.pa.us/apps/pages/index.jsp?uREC_ID=1427369&type=d&pREC_ID=2202098)
- **VIZOR** (K-12 page gives an Atlanta address) sells IT asset management with an optional help desk module — [VIZOR K-12](https://www.vizor.cloud/k12/)
- **GoGuardian Fleet**: a device inventory and repair-ticket tool for K-12 that stays in sync with Google Admin Console — [GoGuardian docs](https://docs.goguardian.com/products/fleet/track-device-repairs)
- **Follett Software**: launched Destiny Help Desk inside Destiny Resource Manager on 20 Feb 2025 ([PR Newswire](https://www.prnewswire.com/news-releases/follett-software-announces-launch-of-follett-destiny-help-desk-to-elevate-customer-support-302380502.html)) and Destiny AI for Resource Manager on 30 Jul 2025 ([Follett](https://follettsoftware.com/news/follett-software-introduces-destiny-ai-in-destiny-resource-manager-to-power-smarter-district-asset-management/))
- **Eduphoria Helpdesk** (Texas-centred) is a school ticketing app for technology, maintenance and other campus services, with routing by campus, building or room — [Eduphoria support](https://support.eduphoria.net/docs/helpdesk)
- **FMX** sells one school platform for IT, facilities, events and transportation — [FMX](https://www.gofmx.com/school-help-desk-software/)
- **Gopher (CDW Amplified for Education)**: Gopher for Chrome brings Google Admin device data into a Google Sheets add-on and web app for bulk actions. The Gopher Buddy extension logs user/IP/session data per Chromebook and needs a Gopher for Chrome Premium licence. It is a device admin tool, not a helpdesk — [CDW Amplified help centre](https://amplifiedlabs.zendesk.com/hc/en-us/articles/12144743506707); [Amplified Gopher Buddy FAQ](https://amplifiedlabs.zendesk.com/hc/en-us/articles/360051865934-Frequently-Asked-Gopher-Buddy-Questions-for-End-Users); [Gopher licensing](https://amplifiedlabs.zendesk.com/hc/en-us/articles/7414741653651-Gopher-for-Chrome-Gopher-for-Chrome-Premium-licensing)
- **Jamf**: agreed on 29 Oct 2025 to be taken private by Francisco Partners for about $2.2B, expected to close in Q1 2026 — [Jamf press release](https://www.jamf.com/resources/press-releases/jamf-enters-into-definitive-agreement-to-be-acquired-by-francisco-partners-in-2-2-billion-transaction/)
- **Lightspeed Systems**: launched Lightspeed Signal (device/app/network health monitoring) in Jan 2025 and acquired STOPit Solutions in Feb 2025 — [Wikipedia](https://en.wikipedia.org/wiki/Lightspeed_Systems). Its IT management page lists Signal, MDM, Filter and Insight, but no ticketing or repair module — [Lightspeed](https://www.lightspeedsystems.com/challenges/it-management/)
- **TeamDynamix**: a no-code ITSM/ESM vendor with a K-12 practice. It claims to rank No. 1 in the Info-Tech 2026 Enterprise ITSM Data Quadrant and to be a Challenger in the 2026 Gartner MQ for ITSM — [TeamDynamix K-12](https://www.teamdynamix.com/industries/k12-districts/)
- **Spiceworks Cloud Help Desk** is still sold in 2026. There is a free ad-supported Core plan and a Premium plan launched in 2025. The Windows desktop edition ended with 7.5 at the end of 2021 — [Siit review](https://www.siit.io/tools/trending/spiceworks-review) (competitor blog); [Help Desk Migration](https://help-desk-migration.com/spiceworks-desktop-vs-spiceworks-cloud/)
- **Quest KACE SMA** is marketed to education as UEM plus service desk. SMA 15.0 entered full support on 2 Dec 2025 and 15.1 on 13 Jul 2026 — [Quest KACE Education](https://www.quest.com/kace/education.aspx); [Quest support](https://support.quest.com/kace-service-desk/13.2)
- **SupportStudioK12**: a K-12 help desk "built by K12 IT professionals", with per-school pricing and a free self-hosted Community edition — [SupportStudioK12](https://supportstudiok12.com/)
- **Chalk (usechalk/chalk)**: an open-source (AGPL-3.0) K-12 device inventory, help desk, 1:1 lifecycle, rostering and SSO stack. It ships as one Rust binary on SQLite and is free to self-host. It is very young (2 GitHub stars, 298 commits when checked) — [GitHub](https://github.com/usechalk/chalk)
- Other open-source helpdesks surfaced (Zammad, Peppermint, chris-jasztrab/openhelpdesk with status banners, roles, templates and reports) are generic, not school-specific — [GitHub topic: helpdesk](https://github.com/topics/helpdesk); [openhelpdesk](https://github.com/chris-jasztrab/openhelpdesk)

### Inferences
- The "K-12-native" products split into two groups. The first is operations platforms: iiQ, FMX and Brightly/Siemens, which sell multiple departments per student. The second is 1:1 lifecycle tools: Manage1to1, One To One Plus, VIZOR, GoGuardian Fleet, Destiny RM and Asset Panda. Their selling point is the student-device-guardian chain, not ticketing sophistication.
- Chalk and SupportStudioK12 are the closest analogues to EduHelpdesk: small, self-hostable and school-specific. Chalk's architecture (single binary + SQLite) closely mirrors EduHelpdesk's SQLite approach.

### Gaps
- "SchoolCNXT" could not be found as a helpdesk or asset product. It may not exist under that name.
- "Cloud Ready" appears to be Neverware CloudReady (now ChromeOS Flex), an OS product rather than a repair portal. I did not research it further.
- I found no K-12 ticketing product from Securly, and none from GoGuardian beyond Fleet.
- I could not confirm whether the Jamf–Francisco Partners deal closed, or the exact date Siemens acquired Brightly. One search summary said Brightly was "acquired by ALPHA Facilities Solutions" in Nov 2020, which looks wrong.
- I found no AU/NZ/Canada-specific K-12 helpdesk product (see Q9).

## Q1. How do they handle 1:1 student device programmes (deploy/collect, bulk scan, loaners, damage/repair, fees to parents, parts, RMA)?

### Takeaway
This is the clearest difference from generic helpdesks. The K-12 tools model a student → device → guardian chain and support:
- bulk barcode check-out/in at the start and end of the year
- loaner pools that are linked to a repair ticket
- photo-documented damage records that turn directly into invoices to guardians (often with online payment)
- per-device lifetime repair cost
- in-tool warranty/RMA submission to repair vendors

Self-service smart lockers for loaner swaps are a growing add-on.

### Cited Findings
**Deployment, collection and bulk scanning**
- iiQ Assets supports bulk check-in and check-out, so teams can "scan and process hundreds of devices in minutes" with barcode scanners or mobile devices — [Incident IQ device & inventory page](https://www.incidentiq.com/products/school-asset-management-software/manage-devices-inventory) (vendor page, via search index)
- iiQ's teacher "My Classes" lets teachers use a webcam to assign devices to homeroom students during deployments and run "device spot-checks" so missing devices are found before the end of the year — [Incident IQ My Classes](https://www.incidentiq.com/my-classes)
- Asset Panda district customer: "Each fall they deploy over 1,000 laptops", scanning each laptop as it is assigned to a student so the line moves faster — [Asset Panda success story](https://www.assetpanda.com/resource-center/success-stories/asset-tracking-for-school-district-campuses/). Asset Panda also records assignment dates, transfer history and condition, with time-stamped, user-attributed changes (a "defensible custody trail") — [Asset Panda school districts](https://www.assetpanda.com/solutions/education/school-districts/)
- One To One Plus can "flag damaged devices during the collection process", "generate damage invoices tied to each device" and "create help desk tickets automatically for reported damage". It also audits assets by site, type and funding source — [One To One Plus asset management](https://onetooneplus.com/asset-management/)
- VIZOR: mass device allocation to students with barcode scanning, automated return reminders, and an "acknowledgement of equipment receipt" workflow — [VIZOR K-12](https://www.vizor.cloud/k12/)
- Chalk: check-out/check-in with due dates, a scan-to-reconcile physical audit mode ("all keyboard-wedge, no special hardware"), QR label sheet printing, and family email notifications — [Chalk GitHub](https://github.com/usechalk/chalk)
- Manage1to1 frames a K-12 "school year rhythm": the August deployment scramble, steady damage in October, grade-promotion roll-up in December, and June returns needing bulk check-in and automated reporting — [Manage1to1 buying guide](https://manage1to1.com/learn/help-desk-software-for-schools/) (vendor content)

**Loaners**
- iiQ: checking out a loaner removes it from the loaner pool, and checking it in returns it. A student under repair can hold two devices at once — [iiQ community](https://community.incidentiq.com/assets-33/questions-about-what-is-the-best-practice-for-showing-devices-are-off-for-repair-6239)
- GoGuardian Fleet: when a repair is logged, staff can "assign an available loaner device to the student while the device is being repaired". The UI allows one device plus one loaner per user (more needs a CSV import) — [GoGuardian repairs](https://docs.goguardian.com/products/fleet/track-device-repairs); [GoGuardian FAQ](https://docs.goguardian.com/products/fleet/fleet-faqs)
- Smart lockers: FUYL lockers integrate with iiQ for permanent replacements, spare-device exchanges (swap now, swap back after repair), temporary loans, new issues and returns, all with "self-authenticated, self-serve access". Districts named: Hamilton County, Maritime Academy CS, Monticello CSD — [LocknCharge](https://www.lockncharge.com/smart-locker-systems-with-incident-iq). In a kiosk-based repair intake, the student scans their ID, chooses "Repair", describes the issue and leaves the device in a locker bay — [LocknCharge blog](https://www.lockncharge.com/blog/mobile-device-repair-in-schools)

**Damage, repair, parts and lifetime cost**
- GoGuardian Fleet repair ticket:
  - device found by serial, asset ID, user or location
  - damage-type dropdown
  - eight statuses: Needs Repair, On Hold, Awaiting Parts, Destroyed, Replaced, Repaired, Completed, Other
  - "Add Cost" line items with minutes spent, giving "a running total of what each device has cost to maintain over its lifetime"
  - photos up to 10 MB each
  - invoice generation and previous-repairs history

  — [GoGuardian repairs](https://docs.goguardian.com/products/fleet/track-device-repairs)
- iiQ technicians log components (power adapters, keyboards, replacement screens) and track part usage, warranty and repair cost per device — [Incident IQ Chromebook management](https://www.incidentiq.com/school-asset-management-software/chromebook-management-software) (via search index)
- Manage1to1: photo documentation and repair history, insurance claim integration, and "replacement queue routing", all linked to the device profile — [Manage1to1](https://manage1to1.com/)
- VIZOR: repair diagnostics with costs, "lemon device" identification for chronic problems, and flagging of students with excessive repairs — [VIZOR K-12](https://www.vizor.cloud/k12/)
- Chalk: repair tracking with costs, plus lost/stolen records that capture a police report — [Chalk GitHub](https://github.com/usechalk/chalk)

**Fees and billing to parents/guardians**
- iiQ Fee Tracker attaches fees to users, assets and tickets. It can generate fines, record payments and produce one-click PDF invoices, and sends invoices to guardians using contact details imported from the SIS — [Incident IQ Fee Tracker](https://www.incidentiq.com/apps/fee-tracker); [Fee Tracker launch](https://www.incidentiq.com/newsroom/product-updates/introducing-fee-tracker)
- iiQ payment integrations: Square, Stripe, MySchoolBucks and Vanco — [Incident IQ "What's New: May enhancements"](https://www.incidentiq.com/newsroom/product-updates/whats-new-in-incident-iq-may-22-enhancements-22) (the URL suggests 2022, so likely older)
- VIZOR: chargeback to schools, students or families, plus family notifications about repair costs and insurance requirements — [VIZOR K-12](https://www.vizor.cloud/k12/)
- Manage1to1 says the family, not the student, is the party responsible for a repair fee. It integrates with payment processors such as ConnexPoint and PayPal — [Manage1to1 buying guide](https://manage1to1.com/learn/help-desk-software-for-schools/)
- One To One Plus includes invoices and payments plus e-signatures (for device agreements) in its base subscription — [One To One Plus pricing](https://onetooneplus.com/pricing/); [asset management](https://onetooneplus.com/asset-management/)
- Chalk keeps a fees/fines ledger (assessment and settlement only, no payment processing) — [Chalk GitHub](https://github.com/usechalk/chalk)
- Follett Destiny Resource Manager:
  - districts can create fees "for things such as item usage or insurance" at district level and assign them to patrons by criteria
  - a lost item's fine defaults to its replacement price, or its purchase price if no replacement price is set
  - the Current Checkouts/Fines report sends notices to students with overdues or fines

  — [Destiny Help: Fines and Fees](https://destinyhelp220en.follettsoftware.com/content/c_manage_fines.htm); [Current Checkouts/Fines report](https://destinyhelp200en.follettsoftware.com/content/t_check_out_report.htm)
- Parent-paid device protection plans are common: families enrol by state, district, school and student ID; plans cover accidental damage, liquid, loss/theft and loaner damage, and loss claims need a police report — [Slippery Rock SD](https://www.slipperyrock.k12.pa.us/apps/pages/index.jsp?uREC_ID=1427369&type=d&pREC_ID=2202098); [GoCare K-12](https://gocare.com/k-12)

**Warranty, repair vendors and RMA**
- iiQ + Trafera: submit covered devices for warranty repair from iiQ, file parts claims from Asset Details for in-house repair, generate shipping labels, track the repair, and see warranty eligibility, history and expiry synced into iiQ — [Trafera](https://www.trafera.com/incident-iq)
- Repair-vendor portals offer:
  - Gophermods: serialized intake, repair status, approvals, RMA history — [Gophermods](https://gophermods.com/k-12/repairs/chromebook-repair)
  - Secured Tech: repair portal with prepaid labels — [Secured Tech](https://securedtech.com/services/k-12-device-repair-service/)
  - CTL: free asset software with repair tracking and parts-used inventory — [CTL](https://ctl.net/pages/chromebooks-for-schools-ctl)
  - CDW DeviceCycle "RepairEngine": repair ticketing and fleet analytics — [Lexicon/CDW DeviceCycle](https://lexiconk12.com/)
  - FTG: 24-48h depot repair, 300K+ parts — [FTG](https://www.ftgparts.com/k12)
- VIZOR auto-populates OEM warranty for Dell and Lenovo — [VIZOR K-12](https://www.vizor.cloud/k12/)

### Inferences
- The core data model these tools share is Device ↔ Assignment (student, due date) ↔ Incident/Damage (photos, cause, cost lines) ↔ Fee/Invoice (guardian, payment state) ↔ Repair (status, parts, vendor/RMA). Loaners hang off the repair. EduHelpdesk already has loans, loan kits with reason codes, a repeat-borrower report and parts linked to assets (per project memory). The likely missing pieces are a damage record with guardian billing, per-device lifetime repair cost, repair-vendor/RMA tracking, and bulk start/end-of-year scanning sessions.
- "Lemon device" and "excessive student repairs" reports are cheap to build from existing ticket and parts data, and they read well with UK SLT and finance teams.
- Smart-locker self-service is hardware-dependent. The transferable idea is a kiosk-style repair intake page (scan badge → choose fault → leave device), which needs no lockers.

### Gaps
- I could not confirm whether iiQ has a native RMA object for non-Trafera vendors.
- Example insurance prices (for example a $20 damage waiver with a $200 limit) surfaced in search summaries but could not be tied to a specific primary page.

## Q2. How do they integrate with SIS/rostering, Google Admin, Intune, Jamf and directories?

### Takeaway
K-12 tools treat a nightly SIS/roster sync (directly or via OneRoster/ClassLink/Clever) as the backbone that supplies students, classes, teachers and guardians. They also treat MDM/Google Admin sync as two-way: they read device status and serials and write back actions (disable, deprovision, OU moves, asset tags). Generic ITSM tools need iPaaS or custom work to do this.

### Cited Findings
- iiQ syncs user and course data from ClassLink's SIS integration and offers ClassLink SSO (released Sept 2022). "My Classes" fills itself from the SIS roster and course codes — [iiQ ClassLink SIS](https://www.incidentiq.com/apps/classlink-sis-integration); [eSchool News 2022](https://www.eschoolnews.com/newsline/2022/09/14/incident-iq-releases-integration-with-classlink-single-sign-on/); [iiQ My Classes](https://www.incidentiq.com/my-classes)
- The iiQ Google Devices integration can disable, re-enable and deprovision Chromebooks from the asset page once "Write device status to Google" is enabled. Status changes made in Google sync back into iiQ. Users note a tension: a "lost" iiQ status can be overwritten by Google's "disabled" state on the next sync — [iiQ community](https://community.incidentiq.com/integrations-73/can-i-deprovision-and-disable-devices-in-iiq-with-google-devices-integration-386); [iiQ blog](https://www.incidentiq.com/blog/how-to-deprovision-a-k-12-chromebook-with-ease)
- iiQ has an app-catalogue integration for Mosyle Manager — [iiQ Mosyle app](https://www.incidentiq.com/apps/mosyle-manager)
- Manage1to1 integrations: Jamf Pro, Jamf School, Apple School Manager, Google Workspace, Intune, SCCM, FileWave and Mosyle (MDM); PowerSchool, Genesis, Infinite Campus, Skyward, ClassLink, Veracross and Entra ID (SIS/identity); PayPal; Microsoft 365; a REST API at all tiers — [Manage1to1](https://manage1to1.com/)
- VIZOR: two-way Google Admin Console Chromebook sync, PowerSchool/SIS sync, Jamf and Intune, Google SSO — [VIZOR K-12](https://www.vizor.cloud/k12/)
- FMX: Jamf, Intune, Mosyle and Google Workspace, with Google Admin device syncing — [FMX](https://www.gofmx.com/school-help-desk-software/)
- One To One Plus: Google Admin, Intune, Mosyle and Jamf, plus SIS and SSO — [One To One Plus](https://onetooneplus.com/asset-management/)
- Chalk has the broadest open integration list:
  - SIS: PowerSchool, Infinite Campus, Skyward
  - OneRoster 1.1 CSV/API
  - Clever and ClassLink OAuth-compatible endpoints
  - SAML/OIDC IdP
  - provisioning to Google Workspace, AD (LDAP) and Entra ID
  - Google write-back (OU moves, asset tags) behind an approval workflow
  - webhooks

  Intune and Entra connectors were validated only against mocked APIs — [Chalk GitHub](https://github.com/usechalk/chalk)
- SupportStudioK12: Mosyle and Intune MDM sync, OneRoster SIS, Entra ID and Google SSO — [SupportStudioK12](https://supportstudiok12.com/)
- Destiny Resource Manager supports SIS/rostering integration and SSO — [Follett](https://follettsoftware.com/library-suite/destiny-resource-manager/)
- Gopher for Chrome pulls Google Admin device data into Sheets for bulk OU and metadata changes. Gopher Buddy links user sessions (username, device ID, IP, timestamp, duration) to each Chromebook to help trace lost devices and answer "who last used this Chromebook?" — [CDW Amplified Gopher Buddy settings](https://amplifiedlabs.zendesk.com/hc/en-us/articles/360002516793-Install-and-Setup-Gopher-Buddy-Gopher-Buddy-Settings); [Gopher for Chrome web app](https://amplifiedlabs.zendesk.com/hc/en-us/articles/12144743506707)
- Jamf School provides Lost Mode (locks a supervised iPad and reports its last coordinates), the Jamf Teacher app and the Jamf Parent app for guardians to manage school devices after hours — [Jamf support: Lost Mode](https://support.jamf.com/en/articles/11022491-lost-mode-for-ios-devices-in-jamf-school); [Jamf Teacher](https://apps.apple.com/us/app/jamf-teacher/id1458800229); [Jamf blog](https://www.jamf.com/blog/what-is-jamf-school/)
- Mosyle Manager is built around K-12 hierarchy (students, class periods, courses). Mosyle offers Screen View remote viewing for support (2021) — [AFNTS](https://www.afnts.ca/blog/mosyle-manager); [Mosyle newsroom, 2021](https://mosyle.com/news-room/personalized-screen-sharing-and-support-easier)

### Inferences
- In England, the equivalent of OneRoster/Clever/ClassLink is MIS data via Wonde or Groupcall Xporter (SIMS, Arbor, Bromcom). For EduHelpdesk, a roster import of students, classes, tutor groups and guardian contacts would unlock "My Classes"-style teacher views, guardian notifications and fee invoices.
- Write-back to Google/Intune (disable a lost device, move an OU) is a K-12 differentiator. It needs credentials stored in the app, so it fits the existing secrets-masking approach but adds risk. An approval step before write-back, as Chalk does, is a sensible pattern.

### Gaps
- I could not see full details of iiQ's Intune or Jamf connectors (vendor pages were blocked).
- No vendor documents OneRoster 1.2 support specifically.

## Q3. What portal and self-service designs do they use for teachers and students?

### Takeaway
School-specific portal ideas:
- teacher submits on behalf of a student from a roster view ("My Classes")
- "My Assets" views listing devices assigned to a teacher or classroom
- room/location as a required field
- QR room tags or device QR codes that pre-fill a ticket
- a guided ticket wizard with category-specific troubleshooting
- walk-up kiosks and SMS submission
- picture-password and QR-badge login for young pupils
- status banners for known incidents
- knowledge bases written at a level students can read

### Cited Findings
- iiQ My Classes / Ticket Wizard: teachers submit tickets "on behalf of their students" through a step-by-step Ticket Wizard, with each student's device shown — [iiQ My Classes](https://www.incidentiq.com/my-classes)
- iiQ community/district guidance: teachers' "My Assets" lists the Chromebooks and devices assigned to their classroom, and room number is a required ticket field. Teachers have asked to be able to click a listed device to start a ticket — [iiQ community index](https://community.incidentiq.com/tickets-32/index7.html) (snippet-level evidence)
- iiQ users are asking for device QR codes that open the iiQ mobile app straight onto the device record — [iiQ community](https://community.incidentiq.com/ask-the-community-31/index30.html)
- The iiQ mobile app supports submitting tickets, scanning assets and approving work orders — [iiQ mobile](https://www.incidentiq.com/incident-iq-mobile)
- Typical iiQ district category trees are Devices/Hardware, Software/Online Systems (e.g. Clever, Google, Munis), Network/Wi-Fi, Provisioning (logins, lockouts, password resets) and "Other Requests" — [Lawrence PS KB](https://kb.lawrence.k12.ma.us/article.php?id=1091); [Montclair PS](https://www.montclair.k12.nj.us/departments/technology/incident_i_q)
- iiQ promotes a self-service knowledge base for students, teachers and staff as a key ticket-reduction tool — [iiQ blog](https://www.incidentiq.com/blog/how-to-build-a-help-desk-knowledge-base-the-5-step-guide-for-k-12-it-admins)
- SupportStudioK12: ticket creation by email, SMS (Twilio) and "walk-up kiosk"; a mobile staff portal; QR room tags for location-based tickets; auto-assignment by school coverage area — [SupportStudioK12](https://supportstudiok12.com/)
- Chalk: a staff portal with magic-link sign-in and inbound email, a KB visible in the public portal, CSAT surveys, device-to-ticket linking, and "QR badge and picture-password login for young students" — [Chalk GitHub](https://github.com/usechalk/chalk)
- Eduphoria Helpdesk: staff submit and track work orders and complete surveys on completed work orders. Requests route by campus, building or room — [Eduphoria](https://support.eduphoria.net/docs/helpdesk)
- VIZOR help desk module: a self-service portal for issues and asset requests, and a KB for "students, families, and staff" — [VIZOR K-12](https://www.vizor.cloud/k12/)
- Manage1to1 argues K-12 needs a self-service KB "at reading-level appropriate for students, embedded in ticket creation". Students "can't necessarily describe what's wrong", and volume peaks at school start times — [Manage1to1 buying guide](https://manage1to1.com/learn/help-desk-software-for-schools/)
- San Diego Unified (Freshservice): a staff and student portal with simplified submission, self-service password reset, KB, real-time tracking, and automatic routing to whoever supports each location — [SDUSD IT](https://itd.sandiegounified.org/it_resources/service_desk_technical_support)
- Freshservice's service catalogue is "shopping"-style (add to cart) — [eesel](https://www.eesel.ai/blog/freshservice-for-schools) (secondary, AI-vendor blog)
- openhelpdesk (open source) includes status banners for known issues — [openhelpdesk](https://github.com/chris-jasztrab/openhelpdesk)

### Inferences
- EduHelpdesk already has a tile launcher with category and item buttons, room selection and a requester portal (per project memory). The highest-value additions borrowed from K-12 tools would be:
  - a "My Classes"/"My Room" view so a teacher can raise a ticket against a pupil's or room's device in two taps
  - QR codes on room doors and asset labels that deep-link to the portal with room or asset pre-filled
  - a known-issues banner shown at ticket submission to deflect duplicates
- Picture-password/QR-badge login and kiosk intake suit primary schools and library/IT-office counters in English secondaries.

### Gaps
- I found no evidence of "my devices" pages for students themselves (as opposed to teachers) in iiQ.
- I found no hard data on how much KB deflection helps in schools.

## Q4. What workflow automation is offered?

### Takeaway
K-12 tools offer the usual no-code rules (conditions → actions), SLAs and round-robin assignment. The school-specific twists are:
- routing by building/room/site and "coverage area"
- business calendars that follow the school year
- automatic priority bumps for instruction-blocking failures
- parent/guardian notifications and invoices driven from the SIS
- cross-department ticket spawning (subtickets into facilities, HR and so on)

### Cited Findings
- iiQ rules fire when tickets or assets are created or changed, with no coding. Routing can use location, requestor, asset, issue type, device category, keywords or department. SLAs are assigned dynamically by ticket type. Rules can elevate priority for critical device failures and email specific users when certain ticket types arrive — [iiQ Automations](https://www.incidentiq.com/platform/automations); [iiQ ticketing automation](https://www.incidentiq.com/products/school-help-desk-software/improve-operations)
- iiQ Advanced Ticketing: parent tickets with nested subtickets, and rules that create new tickets "even across products" (e.g. IT → Facilities) — [iiQ Advanced Ticketing](https://www.incidentiq.com/apps/advanced-ticketing)
- Manage1to1: a visual condition/action workflow builder; routing by building, room, department, type, custom fields and time of day; round-robin assignment with vacation opt-out; business hours and holiday calendars per district — [Manage1to1](https://manage1to1.com/)
- FMX: automated technician assignment and approval chains across departments — [FMX](https://www.gofmx.com/school-help-desk-software/)
- SupportStudioK12: SLA monitoring and escalation rules, change tracking with risk assessment, and incident logging with response runbooks — [SupportStudioK12](https://supportstudiok12.com/)
- Chalk: first-response and resolution SLAs, routing/auto-assignment, and family email notifications — [Chalk GitHub](https://github.com/usechalk/chalk)
- VIZOR: automated emails for device returns, repairs and charges, and family notifications — [VIZOR K-12](https://www.vizor.cloud/k12/)
- Destiny Help Desk: customisable workflows, team assignment, and automated stakeholder notifications — [PR Newswire](https://www.prnewswire.com/news-releases/follett-software-announces-launch-of-follett-destiny-help-desk-to-elevate-customer-support-302380502.html)
- iiQ Facilities: recurring preventive maintenance with templates, checklists and reminders — [iTechGuides review](https://www.itechguides.com/products/incident-iq-facilities/)
- TeamDynamix markets K-12 automation for password resets, on/off-boarding and software provisioning — [TeamDynamix K-12](https://www.teamdynamix.com/industries/k12-districts/)
- Spiceworks Cloud has "no automatic ticket routing, and no service level tracking" on any plan — [Siit review](https://www.siit.io/tools/trending/spiceworks-review) (competitor blog)

### Inferences
- EduHelpdesk already counts SLAs in school periods (per project memory), which is more school-aware than most US tools' generic business calendars. Term-date calendars (rather than weekends only) would close the gap with Manage1to1's "holiday calendars".
- "Spawn a facilities subticket from an IT ticket" and "auto-priority if the ticket blocks a lesson" are cheap, high-value rules for English schools, where IT and site teams often share one helpdesk.

### Gaps
- I found no detail on iiQ approval workflows for IT purchases, or on how parent notifications are triggered beyond Fee Tracker invoices.

## Q5. What analytics and reporting do they lead with?

### Takeaway
They lead with:
- resolution time and ticket volume by school/building
- technician productivity
- repair cost per device (lifetime) and repair trends by location or model
- "lemon" devices and students with repeat damage
- asset counts by funding source for audit
- lifecycle and refresh planning

AI natural-language queries over asset data (Follett) are new in 2025.

### Cited Findings
- iiQ Analytics Explorer tracks resolution times, recurring issues and ticket volume across schools, positioned as "clean, reliable reporting to justify budgets and allocate resources" — [iiQ help desk page](https://www.incidentiq.com/products/school-help-desk-software)
- iiQ asset reports cover device deployment by grade and repair trends by location, and are used to track underused devices, control repair costs and inform replacement purchases. One asset view combines tickets, repair history, parts and labour, warranty and assignments — [iiQ blog: asset reports](https://www.incidentiq.com/blog/asset-management-reports); [iiQ asset management](https://www.incidentiq.com/products/school-asset-management-software)
- iiQ publishes a K-12 ROI calculator and content on "device lifecycle planning and operational data" — [iiQ ROI calculator](https://www.incidentiq.com/roi-calculator); [iiQ blog](https://www.incidentiq.com/blog/k-12-operational-data)
- Manage1to1 reporting: resolution time trends (30/90/365 days), technician productivity, first-touch resolution, asset utilisation dashboards, and CSV export "for board reports" — [Manage1to1](https://manage1to1.com/)
- VIZOR: repair trend analysis, lemon devices, excessive student repairs, location by school and classroom, insurance tracking, and ESEA Title fund monitoring — [VIZOR K-12](https://www.vizor.cloud/k12/)
- GoGuardian Fleet: per-device lifetime repair cost and repair history filtered by status and type — [GoGuardian](https://docs.goguardian.com/products/fleet/track-device-repairs)
- Destiny Resource Manager: reports by school, program or funding source; surplus reallocation before purchasing; "audit-ready summaries on demand" for budget hearings — [Follett](https://follettsoftware.com/library-suite/destiny-resource-manager/)
- Destiny Help Desk: support-ticket reports to "identify trends and forecast needs" and spot capital expense trends — [PR Newswire](https://www.prnewswire.com/news-releases/follett-software-announces-launch-of-follett-destiny-help-desk-to-elevate-customer-support-302380502.html)
- FMX: IT equipment maintenance summaries, equipment cost and downtime, team performance, and ticket trends — [FMX](https://www.gofmx.com/school-help-desk-software/)
- SupportStudioK12: anomaly detection against 30-day baselines, plus service monitors for district infrastructure — [SupportStudioK12](https://supportstudiok12.com/)
- Lightspeed Insight: app licence and usage reporting for cost analysis — [Lightspeed](https://www.lightspeedsystems.com/challenges/it-management/)

### Inferences
- E-Rate reporting is US-only, but the underlying idea transfers: tag assets and spend by funding source (VIZOR's "Title funds", Destiny's "funding source"). In England the equivalent tags would be DfE capital, devolved formula capital, pupil premium, PTA donations and MAT central funds. EduHelpdesk's finance asset report (per project memory) is a natural place for a funding-source dimension.
- "Repair cost per device per year" and "devices by age and refresh year" are the metrics heads and business managers are most likely to recognise.

### Gaps
- I found no vendor that publishes benchmark repair rates (for example the percentage of Chromebooks damaged per year). Public benchmarks would need a separate search.

## Q6. Do facilities and other non-IT departments share the platform, and how is that priced?

### Takeaway
Multi-department use is mainstream. iiQ (IT, Facilities, Events, Resources/textbooks, HR Service Delivery), FMX (IT, facilities, events, transport), Eduphoria (tech and maintenance) and TeamDynamix ESM all pitch one platform across departments. iiQ and FMX price per student enrolled plus selected modules. Brightly/Siemens covers facilities only.

### Cited Findings
- iiQ modules:
  - Facilities: work orders, preventive maintenance, labour, parts, inspections
  - Events: requests, approvals, room setup, fees, payments
  - Resources (launched Nov 2024): textbooks and instructional materials
  - HR Service Delivery: benefits questions, leave, forms, onboarding/offboarding

  — [YesPress profile](https://yespress.io/incident-iq) (search snippet; not fetchable); [Incident IQ/Cove Hill](https://www.incidentiq.com/newsroom/announcements/incident-iq-to-accelerate-growth-with-strategic-investment-from-cove-hill-partners)
- iiQ Facilities pricing is "aligned to student enrollment and selected products", and its mobile app needs a licensed iiQ account — [iTechGuides](https://www.itechguides.com/products/incident-iq-facilities/)
- FMX is one platform for IT, facilities, events and transportation, priced "based on the number of students enrolled or the total number of users" plus add-ons — [FMX](https://www.gofmx.com/school-help-desk-software/)
- Eduphoria Helpdesk is used by some districts for IT only and by others across departments (technology, maintenance) — [Eduphoria](https://support.eduphoria.net/docs/helpdesk); [Granbury ISD](https://www.granburyisd.org/apps/pages/index.jsp?uREC_ID=3757391&type=d&pREC_ID=2438460)
- TeamDynamix: districts can "incrementally expand service management to other departments", including facilities, HR and media services — [TeamDynamix K-12](https://www.teamdynamix.com/industries/k12-districts/)
- Brightly/Siemens K-12: Asset Essentials CMMS (work orders, PM, IoT, inventory), Event Manager, Energy Manager and Origin capital planning — [Siemens K-12](https://www.assetmanagement.siemens.com/en-gb/industries/education/k-12); [Columbus School for Girls case study](https://www.assetmanagement.siemens.com/case-studies/the-columbus-school-for-girls-drives-behind-the-scenes-efficiency-with-asset)
- Follett Destiny RM tracks devices, textbooks, musical instruments and Wi-Fi hotspots in one inventory — [Follett](https://follettsoftware.com/news/follett-software-introduces-destiny-ai-in-destiny-resource-manager-to-power-smarter-district-asset-management/)

### Inferences
- In English schools the site team (premises), AV, reprographics and sometimes the HR/onboarding function often sit close to IT. EduHelpdesk's onboarding module and projects/quotes already go beyond IT (per project memory). A "department" dimension with its own categories, queue and permissions would match the iiQ/FMX pattern without per-module pricing.
- Lending non-IT kit (musical instruments, PE kit, calculators, hotspots) through the same loan engine is a direct transfer from Destiny RM.

### Gaps
- I found no published per-module price split for iiQ or FMX.

## Q7. What pricing models and indicative prices do they use?

### Takeaway
K-12-native tools price per student enrolled, with unlimited technicians, users and assets: Manage1to1 publishes $1.20–$1.55 per student per year, with a $1,000/yr minimum. iiQ, FMX and One To One Plus quote per enrolment. Generic ITSM tools price per agent: Freshservice about $19–$99+ per agent per month, Spiceworks Premium $5 per seat per month. Asset Panda prices by users plus asset count. SupportStudioK12 prices per school.

### Cited Findings
- iiQ pricing is "based on student enrollment and product needs", with "no added costs for users, requests, or assets". Agreements are annual with multi-year options and include implementation, training and support. There is no public rate card — [Incident IQ pricing](https://www.incidentiq.com/pricing) (via search index); [PricingNow](https://pricingnow.com/question/incidentiq-pricing/). PricingNow also claims 4–8% annual renewal increases without citing a source (treat as unverified).
- Manage1to1 published tiers:

  | Students | Price |
  |---|---|
  | Up to 700 | $1,000/yr |
  | 701–2,500 | $1.55 per student per year |
  | 2,501–4,500 | $1.35 per student per year |
  | 4,501–7,500 | $1.20 per student per year |

  All features are included and it claims "no modular add-ons for ticket routing, fee tracking, or loaner pools". It cites $6,000/yr for a 5,000-student district and says bundled K-12 platforms cost "$1.20 to $5.00 per student per year" — [Manage1to1 compare](https://www.manage1to1.com/pricing/compare/); [buying guide](https://manage1to1.com/learn/help-desk-software-for-schools/)
- One To One Plus: a quote-based subscription with unlimited users, assets and tickets and all modules included (asset, help desk, invoices/payments, mobile, dashboards); consortium and multi-year discounts — [One To One Plus pricing](https://onetooneplus.com/pricing/)
- VIZOR: two editions priced via a calculator, with free setup, support and training — [VIZOR K-12](https://www.vizor.cloud/k12/)
- SupportStudioK12: per-school (not per-technician) pricing with all features in every paid plan, a free self-hosted Community edition, a 14-day trial, and card or PO payment — [SupportStudioK12](https://supportstudiok12.com/)
- Chalk: free self-hosting (AGPL), with an optional paid hosted service — [Chalk GitHub](https://github.com/usechalk/chalk)
- Freshservice per agent per month: Starter $19, Growth $49, Pro $99 (ITAM/ESM), Enterprise custom with Freddy AI. Education discounts by negotiation. Day passes from $3 per day for occasional agents — [eesel](https://www.eesel.ai/blog/freshservice-for-schools) (secondary; check against Freshworks' own price page)
- Spiceworks Cloud: Core is free with ads. Premium (2025) costs $5 per seat per month billed annually or $6 monthly, and removes ads and adds tasklists, bulk actions and custom reports — [Siit review](https://www.siit.io/tools/trending/spiceworks-review)
- Asset Panda: priced by plan tier, full users and asset count. Aggregators report about $3,000/yr for 5 users up to $18,000/yr for 20 users, with extra assets at about $0.50 each per year — [Desk365](https://www.desk365.io/blog/asset-panda-pricing/); [Capterra](https://www.capterra.com/p/142562/Asset-Panda/pricing/) (secondary; not verified with the vendor)
- Zendesk sells per-agent plans with an education solution page. Education discounts are by negotiation — [Zendesk education](https://www.zendesk.com/service/ticketing-system/education/)

### Inferences
- Per-student pricing explains why US tools bundle fee/guardian/roster features: the student, not the technician, is the billing unit. For a self-hosted UK product the comparison point is about $1.20–$1.55 per pupil per year (roughly £1–£1.25). A 1,200-pupil English secondary would pay about $1,860/yr to Manage1to1.

### Gaps
- No verified iiQ per-student figures. A $95,715.99 Wentzville SD (Missouri) purchase in Apr 2024 (work orders, events, assets) appeared in a search summary, but the primary board document was not retrievable. A Dec 2024 Kansas board enclosure for iiQ returned 403.
- No GBP pricing for any US K-12 tool, and no confirmation that any of them sell in the UK.

## Q8. What AI features were introduced 2024–2026, and how have schools received them?

### Takeaway
AI in K-12 helpdesks arrived mainly in 2025–26, in four forms:
- intake classification and routing (iiQ AI Ticket Assistant, Feb 2026)
- natural-language queries and anomaly alerts over asset data (Follett Destiny AI, Jul 2025)
- suggested replies, KB drafting and troubleshooting assistants (SupportStudioK12)
- virtual agents for ticket deflection (TeamDynamix, Freshservice Freddy)

Vendors stress that district data is not used to train models. I found no independent evidence of how schools received these features; only vendor claims exist.

### Cited Findings
- iiQ AI Ticket Assistant (press release 25 Feb 2026): teachers describe issues in plain language, and the assistant "automatically captures critical details, classifies requests, prioritizes urgency, and routes tickets". It also surfaces patterns and recurring issues. Vendor-claimed results: average resolution time from 5 days to 1 day ("initial insights") and "up to 30% faster" resolution. It runs natively in iiQ, district data "is never used to train an LLM", and it is positioned for FERPA compliance — [Incident IQ](https://www.incidentiq.com/newsroom/product-updates/ai-ticketing-assistant-k-12); [Business Wire](https://www.businesswire.com/news/home/20260225688335/en/Incident-IQ-Introduces-AI-that-Accelerates-K12-IT-Support-and-Protects-Instructional-Time)
- Follett Destiny AI in Resource Manager (30 Jul 2025):
  - natural-language questions such as "How many Chromebooks are overdue at each school?"
  - AI alerts for overdue items, ageing assets and usage anomalies
  - surfacing of missing data (barcodes, purchase prices)
  - reorder and surplus suggestions
  - trained on K-12 terminology
  - available immediately to hosted customers

  — [Follett news](https://follettsoftware.com/news/follett-software-introduces-destiny-ai-in-destiny-resource-manager-to-power-smarter-district-asset-management/); [Follett RM page](https://follettsoftware.com/library-suite/destiny-resource-manager/); [SLJ](https://www.slj.com/story/Follett-Software-Launches-AI-Features-Destiny)
- SupportStudioK12 includes a "Troubleshooting assistant, suggested replies with cited sources, KB drafting, [and] change-risk assessment" in every plan, with toggles and an optional customer-supplied API key — [SupportStudioK12](https://supportstudiok12.com/)
- TeamDynamix claims AI virtual agents deflect 30–60% of tickets and cut resolution time by 40–90% (vendor marketing) — [TeamDynamix K-12](https://www.teamdynamix.com/industries/k12-districts/)
- Freshservice Freddy AI (agent, copilot, insights) requires Enterprise (custom pricing) — [eesel](https://www.eesel.ai/blog/freshservice-for-schools) (secondary)
- Spiceworks Cloud has "no AI" as of Aug 2026 — [Siit review](https://www.siit.io/tools/trending/spiceworks-review)
- For general context, many teachers report unclear district AI policies and little formal guidance — [Tech Policy Press](https://www.techpolicy.press/in-the-us-caution-rules-on-ai-ahead-of-k12-school-year/); [NPR, Sept 2026](https://npr.org/2026/09/28/nx-s1-5759718/ai-schools-experiment-research)

### Inferences
- The AI features most relevant to a small UK school helpdesk are:
  - auto-categorising teacher free text into the existing catalogue (EduHelpdesk already has a "Something else" escape hatch, which is the obvious place to apply it)
  - suggested replies drawn from the KB
  - natural-language reporting
- A self-hosted product could follow SupportStudioK12's model: AI off by default, the school supplies its own API key, and the school gets a clear statement that data is not used for training. UK GDPR and DPIA expectations would push the same way.

### Gaps
- I found no independent studies, surveys or district accounts of satisfaction or problems with K-12 helpdesk AI. The iiQ "5 days to 1 day" figure has no published method.

## Q9. Which ideas transfer to English schools, and what do the AU/NZ/Canada markets add?

### Takeaway
Most innovations are workflow-level and transfer well:
- roster-driven teacher views
- guardian billing for damage
- bulk start/end-of-year scanning
- loaner-on-repair
- funding-source tagging
- repair-vendor/RMA tracking
- QR/kiosk intake
- multi-department queues

US-specific items (E-Rate, FERPA wording, CIPA, Title funds, MySchoolBucks/Vanco) need UK equivalents rather than direct copies. I found no AU/NZ/Canadian K-12-native helpdesk products. Those markets appear to rely on MSPs plus MDM (Intune, Jamf, Google), with heavy BYOD in NZ.

### Cited Findings
- Australian school MSPs (NetComp in Brisbane, e-flo in Sydney) manage Intune, Jamf, Google Workspace for Education and Apple School Manager fleets for schools — [NetComp](https://netcomp.com.au/education/); [e-flo](https://www.e-flo.com.au/industries/it-support-for-schools)
- NZ schools increasingly use BYOD Chromebooks managed in Google Admin. Retailers such as PB Tech offer school-branded BYOD purchase portals — [MySchool NZ](https://www.myschool.co.nz/byod-bring-your-own-device); [PB Tech Education](https://education.pbtech.co.nz/services)
- The NZ Ministry of Education publishes guidance on centrally managing school devices — [education.govt.nz](https://www.education.govt.nz/education-professionals/schools-year-0-13/digital-technology/centrally-managing-devices) (title only; not fetched)
- US-specific elements seen in the tools:
  - ESEA Title fund tracking — [VIZOR](https://www.vizor.cloud/k12/)
  - FERPA compliance claims — [Incident IQ](https://www.incidentiq.com/newsroom/product-updates/ai-ticketing-assistant-k-12)
  - US school payment processors (MySchoolBucks, Vanco) — [iiQ](https://www.incidentiq.com/newsroom/product-updates/whats-new-in-incident-iq-may-22-enhancements-22)
  - "board reports" exports — [Manage1to1](https://manage1to1.com/)

### Inferences
Ranked by likely value to an English school and fit with what EduHelpdesk already has (per project memory):

1. **Pupil/guardian roster import (MIS via Wonde/Groupcall or CSV)**, enabling "My Classes"/tutor-group views and guardian contacts. This is the foundation for items 2 and 3.
2. **Damage incident → guardian charge workflow**:
   - photos
   - cause (accidental or wilful)
   - cost lines from parts used
   - PDF invoice/letter
   - payment status
   - an optional insurance/protection-plan flag

   The UK analogue of MySchoolBucks/Vanco is a parent-payment system such as ParentPay. A manual "paid" flag plus a PDF letter may be enough. DfE charging rules need checking before building.
3. **Bulk deployment/collection sessions** (scan pupil card, then scan device), built around the English calendar: September issue, July returns, Year 11/13 leavers, and mid-year starters and leavers, which EduHelpdesk's onboarding/offboarding work already touches.
4. **Loaner-on-repair and repair-vendor/RMA tracking**, with eight-state repair statuses (GoGuardian pattern) and per-device lifetime cost.
5. **QR codes on rooms and assets** that deep-link to the portal with location or asset pre-filled. A walk-up kiosk mode serves the IT office or library counter.
6. **Funding-source and refresh-year reporting** (England's analogue of E-Rate/Title reporting) inside the finance asset report.
7. **Department queues** (site/premises, AV, reprographics) and cross-department subtickets.
8. **Opt-in AI** (classification of free text, KB-sourced suggested replies) with a bring-your-own-key model.

The two most direct self-hosted comparators are Chalk (open source, SQLite, roster-first) and SupportStudioK12 (Community edition). Studying their docs and data models would be a quick way to benchmark EduHelpdesk.

### Gaps
- I found no Canadian or AU/NZ K-12 helpdesk vendors or district case studies. Those markets may use generic ITSM (TOPdesk, ServiceNow, Freshservice) or state-provided services, but this was not verified.
- I did not verify UK legal limits on charging parents for device damage (DfE charging policy guidance).
- No UK customer evidence was found for any of the US K-12 tools.
