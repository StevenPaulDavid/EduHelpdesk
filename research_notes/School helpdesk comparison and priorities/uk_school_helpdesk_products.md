# Helpdesk / ITSM products used by or marketed to UK schools and MATs: feature comparison (researched 2026-10-03)

Scope notes: Pricing is list price from vendor or G-Cloud pages unless marked "(aggregator)". Aggregator figures come from third-party pricing blogs and should be checked before quoting. USD prices are left in USD, not converted. "Not found" means I searched and found nothing. It does not mean the feature is absent. Review/opinion content was deliberately excluded (another researcher covers it).

## Q1. Which products have explicit UK education positioning, and what features do they lead with for schools?

### Takeaway
Very few helpdesk products are built for UK schools. The UK-education-specific products found are AssetTap, Civica Education Operations and Every Compliance (IRIS). They lead with asset registers, loans, audits, trust-wide dashboards, UK hosting and compliance or DfE alignment, more than with deep ticketing. The general ITSM vendors with education pages (Halo, TOPdesk, Freshservice, Zendesk, ManageEngine, SysAid) mostly show university or US K-12 evidence, not UK schools or MATs. Many UK schools never buy a helpdesk at all: they use the ticket portal of their managed-service provider or broadband consortium (LGfL, Classroom365, EduThing, Joskos, Computeam). Two incumbents are in decline. Spiceworks Cloud is now capped at 5 free seats (since 2025), and NetSupport ServiceDesk, a UK schools-oriented helpdesk, reached end of life on 31 March 2025.

### Cited Findings

#### Product status changes (verify-still-exists check)
- **Spiceworks Help Desk (on-prem/Desktop):** deprecated 31 Dec 2021. No longer downloadable, no security patches. — [Comparitech](https://www.comparitech.com/net-admin/spiceworks-review/). One source says Desktop "ended development in 2018" (development stopped earlier than the EOL date; both can be true). — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- **Spiceworks Cloud Help Desk:** "actively sold and maintained as of August 2026", with no deprecation announcement found. — [siit.io review 2026](https://www.siit.io/tools/trending/spiceworks-review). Releases shipped through 27 July 2026. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- Spiceworks pricing change: the free Core plan has been capped at 5 technician/admin seats since May 2025 (end of the "free forever" model). The Premium tier launched in 2025 at $5/seat/month annual or $6 monthly. Once you pass 5 seats, *all* seats become billable (6 techs = $360/yr). — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system); [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
- Spiceworks over-cap behaviour: adding a 6th seat without upgrading locks techs out, stops the support email accepting tickets and closes the end-user portal. Staff confirmed this in a July 2026 community thread (reported via search summary). — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- Spiceworks is owned by Ziff Davis (acquired 2019). — [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
- **NetSupport ServiceDesk** (browser-based helpdesk that NetSupport marketed to schools' IT teams): end of sale and final support both 31 March 2025, "due to market demand, shifts in technology, and a change in focus". netsupportservicedesk.com now 301-redirects to an end-of-life page. NetSupport DNA (asset management) and NetSupport School continue. — [NetSupport EOL page (redirect target)](https://www.netsupportsoftware.com/end-of-life-netsupport-servicedesk); [NetSupport education solutions](http://www.netsupportsoftware.com/education-solutions/?Lang=IT). *Uncertain:* the EOL page body did not render in my fetch, so the dates come from the search-engine extract of that page.
- **Every** (UK schools compliance and HR) joined IRIS at the end of 2021. It is now "Every Compliance by IRIS" and is *not* a Juniper Education product. — [G-Cloud listing](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369). Juniper Education (Horizon Capital-backed) sells pupil tracking, curriculum planning, MIS, HR and compliance to "over 14,000 schools". — [Horizon Capital](https://horizoncapital.co.uk/portfolio/juniper-education/). Its compliance feature is a website compliance dashboard, not an IT helpdesk. — [Juniper help](https://help.junipereducation.org/hc/en-gb/articles/23622407670813-Juniper-Websites-How-do-I-get-to-my-school-s-Compliance-Dashboard)
- **GLPI** 11.0.0 released 1 Oct 2025. 11.0.8 shipped 24 June 2026 with critical security fixes. — [siit.io GLPI review](https://www.siit.io/tools/trending/glpi-review)
- **osTicket** is still maintained: v1.18.3 has security fixes and PHP 8.3/8.4 support. The release notes are maintenance-focused, with no major new features. — [osTicket releases](https://osticket.com/category/releases/)

#### UK-education-specific products (closest competitors to EduHelpdesk)
- **AssetTap (UK, 2026, v3.7.0):** "School IT operations platform" for UK schools, academies and MATs. — [assettap.co.uk](https://assettap.co.uk/)
  - Helpdesk: tickets, priorities, assignment, response and resolution SLAs; repairs and maintenance with technician assignment and service history.
  - Assets: tags, serials, rooms, users, purchase data, warranties, photos, lifecycle history; QR audits and stocktakes that surface exceptions; QR/barcode "SmartCapture" with duplicate detection and batch onboarding; disposal tracking.
  - Planning and contracts: loans and staff booking; documents and contracts with expiry reminders; "assets due now, 12-month replacement pressure and budget baselines".
  - Integration: Microsoft 365 and Intune (user import, device discovery, matching). No Google, Jamf or MIS integration listed.
  - Trust, hosting and compliance: MAT-ready school-scoped permissions, read-only role, full audit trail, UK-hosted, automated backups, "Built with DfE digital technology guidance in mind".
  - Price: £39/month (£390/yr) small primary; £49/month (£490/yr) medium/large primary; £69/month (£690/yr) secondary/college; MAT £149/month for 3 schools + £15/month per extra school.
  - Named user: Netherhall Learning Campus. Partner: MGL World.
- **Civica Education Operations (MATs):** compliance and operations SaaS. — [Civica](https://www.civica.com/en-us/sector-pages/education/software-multi-academy-trusts/school-operations-and-compliance-software/)
  - IT helpdesk: "Staff and students can report issues with a simple email, which will automatically create and assign a ticket".
  - Assets and maintenance: tracks, loans and audits IT equipment across all sites; logs maintenance checks.
  - Trust-wide dashboards and reporting.
  - Named trusts: Co-op Academies Trust (testimonial), Reach2, The de Ferrers Trust, Cognita, Westcountry, Oasis. No MIS integration stated.
- **Every Compliance by IRIS:** modular compliance SaaS for schools and trusts. — [G-Cloud 14](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369); [weareevery.com](https://www.weareevery.com/)
  - Logs "IT and Premises issues" across multiple sites; asset module with depreciation and a mobile app.
  - "Trust Wide Activity management and reporting".
  - Links to schools' Active Directory to import users; identity federation (e.g. Google).
  - UK data centres. £1,526/yr per licence (unit not defined on listing), education pricing available.
  - Homepage lists integrations with payroll, MIS, budgets and IRIS Central Reporting.
- **Microsoft-native (SharePoint/Lists/Teams):**
  - The free SharePoint "IT help desk" site template ships a **Tickets** list, a **Devices** list, FAQ and documents pages. As a "Microsoft 365 connected template" its lists and pages are pinned as tabs in the team's General channel. Microsoft suggests you "Integrate your existing ticketing system or use a Microsoft List". — [Microsoft Support](https://support.microsoft.com/en-us/office/use-the-it-help-desk-sharepoint-site-template-808d0c01-1ea6-4962-8efc-f458d2102c77)
  - **Helpdesk 365** (third-party, SharePoint/Teams): tickets are raised with existing M365 sign-in; "works within your Microsoft 365 subscription and data stays with you". 14-day trial. From $19.99/user/month (Standard) to $59.99 (Enterprise); free for 1 user. — [Microsoft Marketplace](https://marketplace.microsoft.com/en-us/product/office/wa200004972); [G2 pricing](https://www.g2.com/products/helpdesk-365/pricing) (aggregator)
  - Commercial Power Apps helpdesk templates exist (canvas apps: tickets, FAQs, Copilot/PVA chatbot). — [Logisam](https://logisam.com/it-helpdesk-power-apps-template/)
  - No UK school case studies of these were found.

#### UK managed-service / consortium portals (a service, not a product schools buy separately)
- **LGfL:** schools raise "support cases" on the LGfL Support Site (USO login, "Service Desk" tab, "Raise an Issue"). Cases are recorded with a case number emailed to the school, and the site keeps "a full history and audit trail". Phone line 8:00–18:00 working days. Schools can also configure and report on their own LGfL services. — [LGfL support guide](https://lgfl.net/supportguide); [emPSN KB](https://www.empsn.org.uk/knowledge-base/lgfl-service-desk/)
- **Classroom365:** ICT helpdesk for schools and MATs with online ticketing plus email and phone support. — [Classroom365](https://www.classroom365.co.uk/ict-support/ict-helpdesk/)
- **EduThing:** 24-hour service desk with emergency response, remote monitoring and patching, audit and strategy reports. — [eduthing](https://www.eduthing.co.uk/what-we-do/)
- **Joskos:** 160+ schools, colleges and MATs; National Operations Centre; 100k+ users supported daily. — [Joskos](https://www.joskos.com/)
- **Computeam:** MAT IT support. Its "Compass" tool records broadband, filtering and firewall setup against DfE expectations. — [Computeam MAT](https://www.computeam.co.uk/mat-it-support); [Computeam DfE article](https://www.computeam.co.uk/videos-and-blog/article/dfes-digital-and-technology-standards-a-comprehensive-overview)
- **Class Technology Solutions (CTS, UK MSP):** runs ~70 schools on **Desk365** (107 agents). Features used: per-school queues, email-to-ticket routing, SLA management, asset management (Premium plan), browser access for on-site engineers. CTS migrated from osTicket because "We outgrew it". The case study is dated 2026. — [Desk365 case study](https://www.desk365.io/customers/cts-uk/)
- **Bromcom (MIS vendor):** its own service desk has response targets from 1 hour to four working days by priority. MATs, federations and LAs "may wish to take on the 1st line and 2nd line service desk" with Bromcom accreditation. This is MIS support, not an IT helpdesk module. — [Bromcom MAT G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/777067293419029)
- **Wonde** (MIS middleware): 30,000+ UK schools and 700+ integrated apps. — [Wonde](https://www.wonde.com/). Connects 24 MIS including SIMS, Arbor, Bromcom, ScholarPack, iSAMS, Progresso, RM Integris, Pupil Asset. — [SchoolsBuddy help](https://help.schoolsbuddy.com/hc/en-gb/articles/4405334397965-Wonde-Arbor-Bromcom-Civica-Maze-Engage-Facility-CMIS-Furlong-SchoolBase-Progresso-Pupil-Asset-RM-Integris-ScholarPack-REST-SchoolsBase-SchoolsPod-SIMS-Synergetic-Teacher-Centre-Veracross-VSware-WCBS); [Clever (Wonde owner) blog 2025](https://www.clever.com/blog/2025/10/what-is-wonde)

#### General ITSM vendors with education pages (UK relevance noted)
- **HaloITSM / HaloPSA (UK vendor):**
  - Company: Stowmarket, Suffolk; founded 1994 as NetHelpDesk; rebranded Halo around 2019. — [Flamingo HaloPSA review](https://www.flamingo.run/blog/halopsa-review)
  - Positioning: "single-site secondary schools to large multi-campus universities", explicitly naming schools, colleges and MATs. — [Halo education sector page](https://usehalo.com/haloitsm/sectors/itsm-software-for-education/)
  - Leads with: multi-site deployments "with location-based configurations"; a single asset register with lifecycle tracking; SLAs; KB; branded portal; workflow automation; "AI is included in every Halo licence at no additional cost". — [Halo education sector page](https://usehalo.com/haloitsm/sectors/itsm-software-for-education/)
  - Also on the education page: room and space booking; "Built for the Academic Calendar" demand forecasting; SOC 2 Type II, ISO 27001, Cyber Essentials Plus; GDPR. — [Halo /use/education](https://www.usehalo.com/use/education)
  - Named education customers: Canterbury Christ Church University (98% incident / 99% request SLA compliance). — [Halo /use/education](https://www.usehalo.com/use/education). Sidney Sussex College, Cambridge. — [Allied ESM case studies (Halo partner)](https://alliedesm.com/case-study)
  - No UK schools or MATs named, and no MIS integration or device-loan feature found on the education pages.
  - G-Cloud 14: £34/licence/month, education pricing available, 30-day trial, UK data location (AWS), 2FA, native mobile apps included, M365/Teams/Google Workspace/Power BI integrations, ISO 27001, Cyber Essentials. — [G-Cloud listing](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/595769909588872)
  - Integrations: 250+ native, including Intune device sync, Jamf, Google Workspace, Lansweeper and Entra ID (Azure AD) SAML SSO/provisioning, included in the licence (Halo partner claim). — [Allied ESM integrations](https://alliedesm.com/halo/integrations); [Lansweeper](https://www.lansweeper.com/product/integrations/itsm/haloitsm/); [SMC Consulting SSO guide](https://www.smcconsulting.be/news/itsm-sso-azure-ad-guide); [Halo Teams guide](https://usehalo.com/haloitsm/guides/1080/)
  - HaloPSA is the MSP edition (cloud or on-prem; service desk, contracts, billing, time tracking, assets). — [Flamingo](https://www.flamingo.run/blog/halopsa-review)
- **TOPdesk (Dutch, Delft, founded 1997):** — [Wikipedia](https://en.wikipedia.org/wiki/TOPdesk)
  - Education page leads with a self-service portal ("read FAQs, log tickets, and order supplies 24/7"), automated routing, asset management, onboarding workflows and room booking.
  - Integrations: Teams, Power BI, Azure DevOps, WhatsApp, Zapier, Lansweeper.
  - Named customers: University of Sheffield, LSHTM and others. — [TOPdesk education](https://www.topdesk.com/en/industries/help-desk-software-for-education/)
  - Supports ~10% of Canadian educational entities and >25% of Ontario school boards. — [TOPdesk US education](https://www.topdesk.com/us/education/)
  - No UK school or MAT customers found.
- **Freshservice (Freshworks):** the education page leads with AI (claims "45% average resolutions by autonomous AI agents"), Teams/email/text omnichannel, and ITAM via Device42. Only US universities are named (UPenn, Texas A&M). — [Freshservice education](https://www.freshworks.com/freshservice/solutions/education/)
- **Zendesk:** the education page leads with channels (email, phone, web, Facebook, Twitter, chat), a KB with FAQs and community forums, and "set up in less than 30 minutes". No UK institutions named. — [Zendesk UK education](https://www.zendesk.co.uk/service/ticketing-system/education/)
- **ManageEngine ServiceDesk Plus:** K-12 page leads with barcode/QR asset scanning, an "asset loan registry", SIS integration, SLAs, automated approvals, the Zia AI self-service bot, CMDB, and on-prem or cloud deployment. US K-12 framing. — [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html)
- **SysAid:** used in K-12; self-service portal, password reset, ITAM/CMDB, routing by campus, GenAI chatbot; quote-only pricing. — [StatusGator K-12 roundup](https://statusgator.com/blog/best-help-desk-software-for-schools-and-k12/) (secondary source; weak)
- **InvGate Service Management:** ITIL-certified; ticketing, KB, portal, SLAs/OLAs, multi-department; "discounts are available to non-profits and educational organizations". — [Capterra listing](https://www.capterra.com/p/133392/Service-Desk/) (aggregator)
- **Hornbill (UK):** G-Cloud 14 lists UK data storage (Equinix), SSO via identity federation, 2FA, "1000+ codeless integrations", ESM/ITSM/ITOM/ITAM, browser-based mobile access (no native app), £34.16–£58.33 per user (period not stated in my extract), no education discount listed. — [Hornbill G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/221286282512590)
- **SolarWinds Web Help Desk:** on-prem and self-hosted ("no data leaving your network"). Includes asset discovery, hardware-to-ticket linking, asset lifecycle, licence management, SLA and reporting. Per-technician annual subscription. No education pricing on the page. — [SolarWinds WHD pricing](https://www.solarwinds.com/web-help-desk/pricing)
- **US K-12 benchmarks with no UK presence found** (useful as the "school-specific feature ceiling"):
  - **VIZOR:**
    - Ticketing: school-specific auto assignment, categorisation and prioritisation; email-to-ticket; repair workflows; portal and per-audience KB.
    - Asset sync: Google Admin Console, Intune and Jamf, with serials and warranties shown inside tickets.
    - Devices: 1:1 programmes, barcode check-out, repair cost and charge tracking, automations (Powerwash loaners, disable lost devices), SIS integration (PowerSchool). The page shows US (Georgia) HQ details. — [VIZOR helpdesk](https://www.vizor.cloud/school-help-desk-software/)
    - Chromebooks: barcode-reader-driven 1:1 distribution, loaners, swap-outs and returns; "track student device insurance"; auto-populated warranties. — [VIZOR Chromebooks](https://www.vizor.cloud/chromebooks/)
  - **Incident IQ:** "designed exclusively for school districts". Routing by issue type, location, device category, keywords or department; asset lifecycle, facilities, compliance docs, district-wide reporting; technician mobile app. — [Incident IQ helpdesk](https://www.incidentiq.com/school-help-desk-software); [Incident IQ](https://www.incidentiq.com/)
  - **EduDesk** (UniDesk Inc.): K-12 AI ticketing, classroom-level hotspot analytics, Jamf and Google Admin integrations (several integrations marked "In Development"). — [edudesk.org](https://edudesk.org/)

#### MIS vendors
- I found no IT helpdesk or asset-ticketing module from Arbor or Bromcom. Arbor's partner page lists Wonde and ParentPay-style integrations rather than an IT helpdesk. — [Arbor partners](https://arbor-education.com/our-partners-integrations/)

#### DfE Digital and Technology Standards (the only explicit UK "spec" for helpdesks)
- **Publication.** DfE added an IT support standard (the 12th standard) for schools and colleges, published 15 Dec 2025. — [Smoothwall](https://smoothwall.com/resources/new-dfe-it-support-standards). The gov.uk page was "updated 16 September 2026" and says "You should already be meeting this standard or working towards it". — [GOV.UK IT support standards](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Per-request record.** For each request, record "a description of the issue, the date and time the request was made, the requester's name and the approver if required, the issue's priority level, actions taken to investigate and resolve the issue". — [GOV.UK](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Status visibility.** Staff must "be able to check the status of their requests and see whether they need to take any further actions, such as confirming that the issue has been resolved". — [GOV.UK](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Registers.** Maintain "registers of assets, software and IT support's own activities", plus a record of all IT support services used, who provides them and what they cover. — [GOV.UK](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Response and resolution.** Set "clear expectations for how quickly issues will be responded to and resolved". Agree prioritisation (e.g. priority for issues affecting teaching and learning or core systems) and escalation. — [GOV.UK](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Annual review.** A formal review at least once a year, "summarised in writing" for leaders and governors. — [GOV.UK](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Core standards deadline.** Six core standards (broadband, cyber security, digital leadership and governance, filtering and monitoring, network switching, wireless) should be met by 2030. — [Smoothwall search extract](https://smoothwall.com/resources/new-dfe-it-support-standards)
- **Vendor mapping.** Only AssetTap ("built with DfE digital technology guidance in mind") and Computeam Compass claim alignment with the standards. No general ITSM vendor maps features to them. — [AssetTap](https://assettap.co.uk/); [Computeam](https://www.computeam.co.uk/videos-and-blog/article/dfes-digital-and-technology-standards-a-comprehensive-overview)

#### UK 1:1 device schemes, parental payments and insurance (adjacent market)
- **Parental contribution schemes** run through third parties, not helpdesks:
  - Freedom Tech runs a school's 1:1 parental-contribution scheme. — [Trafalgar School](https://www.trafalgarschool.org.uk/parents/chromebooks/)
  - Albion offers leasing and parental-contribution subscriptions for iPad, Mac and Chromebook. — [Albion](https://www.albion.co.uk/education/financing-mobile-learning/)
  - edde provides parent portals to register and manage contributions, plus staff portals for orders, serials, custodians, payments, insurance/warranty claims and disposal. — [edde](https://edde.education/)

### Inferences
- The UK-school-specific market is thin and asset-led. AssetTap and Civica both bundle helpdesk + assets + loans + audits + trust dashboards, which is the same shape as EduHelpdesk. AssetTap is the closest like-for-like (UK-hosted, MAT pricing, M365/Intune, DfE-aligned), and it is cheap (£690/yr per secondary).
- Big ITSM vendors sell to UK HE and some MATs, but their marketing evidence is university-level. For a 2–6 technician school they mostly sell generic ITSM with a discount.
- The DfE IT support standard is, in effect, a requirements list for a school helpdesk: request record fields including approver, requester status visibility and confirm-resolved, asset/software/contract registers, SLA expectations, prioritisation weighted to teaching and learning, and an annual written review. Almost no vendor maps to it explicitly, so this is an open positioning opportunity.
- NetSupport ServiceDesk's EOL and Spiceworks' seat cap and ads leave a population of small UK school IT teams looking for a cheap or free replacement. That is the same audience EduHelpdesk's Spiceworks importer targets.

### Gaps
- No UK school or MAT case studies were found for TOPdesk, Freshservice, Zendesk, ManageEngine, SysAid, InvGate, Hornbill, Jitbit or SolarWinds WHD. That may reflect the search approach, not real absence.
- RM Education and Grey Matter: I found no school-facing helpdesk product or portal details (Grey Matter appears to be a software reseller). The EduThing and Joskos portals' underlying platforms (e.g. HaloPSA, Autotask) are not disclosed.
- The Wonde sync frequency ("every four hours") appeared only in a search summary without a clear primary source. Treat as unverified.
- Civica Education Operations' former product name, pricing and MIS integration were not found.

## Q2. What do they offer that a small self-hosted school helpdesk typically lacks?

### Takeaway
The recurring things commercial products have and small self-hosted tools lack are:
- device and identity sync (Intune, Jamf, Google Admin, Entra ID SSO/SCIM);
- discovery agents;
- a knowledge base with AI article suggestions;
- satisfaction surveys;
- native mobile apps with barcode/QR scanning;
- email/Teams channels;
- room booking and facilities;
- change/problem/approval workflows;
- certified UK hosting (ISO 27001 / Cyber Essentials Plus / SOC 2).

US K-12 tools add 1:1 device-programme tooling (repair charges, insurance tracking, loaner automation). No product found offers Wonde/MIS sync for UK staff, pupils or rooms. That is a gap across the whole market, not just for small tools.

### Cited Findings
- **MDM/asset sync:**
  - Freshservice syncs Intune devices as assets. — [Freshservice support](https://support.freshservice.com/support/solutions/articles/50000001055-ms-intune-integration-with-freshservice). It also syncs Jamf Pro macOS, iOS and tvOS devices. — [Climb CS](https://www.climbcs.com/news/manage-all-assets-on-all-devices-with-freshworks-and-jamf/)
  - Halo has Intune, Jamf, Google Workspace and Lansweeper integrations. — [Allied ESM](https://alliedesm.com/halo/integrations); [Lansweeper](https://www.lansweeper.com/product/integrations/itsm/haloitsm/)
  - VIZOR syncs Google Admin, Intune and Jamf. — [VIZOR](https://www.vizor.cloud/school-help-desk-software/)
  - Snipe-IT has "native sync adapters for 20+ MDM/RMM platforms (Jamf, Intune, Kandji, etc.)". — [Snipe-IT features](https://snipeitapp.com/features)
  - AssetTap offers M365/Intune user import and device discovery. — [AssetTap](https://assettap.co.uk/)
- **Discovery/inventory:**
  - SolarWinds WHD has automated asset discovery. — [SolarWinds](https://www.solarwinds.com/web-help-desk/pricing)
  - Spiceworks includes asset inventory scanning. — [siit.io](https://www.siit.io/tools/trending/spiceworks-review). Inventory Online is a separate free subnet scanner. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
  - Freshservice uses discovery probes and a CMDB. — [eesel Freshservice for schools](https://www.eesel.ai/blog/freshservice-for-schools)
  - GLPI does inventory of computers, network kit, printers and phones. — [GLPI features](https://www.glpi-project.org/en/features/)
- **SSO/identity/provisioning:**
  - Halo supports Entra ID SAML SSO and attribute provisioning. — [SMC Consulting](https://www.smcconsulting.be/news/itsm-sso-azure-ad-guide)
  - Snipe-IT supports SAML, AD/LDAP and SCIM. — [Snipe-IT features](https://snipeitapp.com/features)
  - Jitbit includes SAML on the Company and Enterprise self-hosted tiers and in SaaS. — [Jitbit purchase](https://www.jitbit.com/helpdesk/purchase/)
  - Hornbill and Every Compliance support identity federation. — [Hornbill G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/221286282512590); [Every G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369)
  - Every imports users from schools' Active Directory. — [Every G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369)
- **MFA:**
  - Spiceworks offers TOTP MFA. — [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
  - Halo and Hornbill offer 2FA. — [Halo G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/595769909588872); [Hornbill G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/221286282512590)
- **Mobile apps:**
  - Halo has native apps included in the subscription. — [Halo G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/595769909588872)
  - Spiceworks has iOS and Android apps. — [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
  - Jitbit has iOS and Android apps. — [Jitbit](https://www.jitbit.com/helpdesk/purchase/)
  - Incident IQ has a technician app. — [Incident IQ](https://www.incidentiq.com/school-help-desk-software)
  - Every's asset module has a mobile app. — [Every G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369)
  - Hornbill has no native app (browser only). — [Hornbill G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/221286282512590)
- **Barcode/QR scanning and audits:**
  - ManageEngine K-12 does bar code/QR scanning. — [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html)
  - Snipe-IT generates QR and barcode labels. — [Snipe-IT features](https://snipeitapp.com/features)
  - AssetTap has QR audits and stocktake exceptions. — [AssetTap](https://assettap.co.uk/)
  - VIZOR offers barcode check-out. — [VIZOR](https://www.vizor.cloud/school-help-desk-software/)
- **Loans/checkout:**
  - ManageEngine has an "asset loan registry". — [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html)
  - Civica tracks loans and audits. — [Civica](https://www.civica.com/en-us/sector-pages/education/software-multi-academy-trusts/school-operations-and-compliance-software/)
  - Snipe-IT does check-in/out with optional digital signature acceptance. — [Snipe-IT features](https://snipeitapp.com/features)
  - Freshservice handles K-12 device check-in/check-out. — [eesel](https://www.eesel.ai/blog/freshservice-for-schools)
- **1:1 programme tooling** (repair charges, insurance tracking, automated warranty population, loaner Powerwash, disabling lost devices): VIZOR only among products found. — [VIZOR](https://www.vizor.cloud/school-help-desk-software/); [VIZOR Chromebooks](https://www.vizor.cloud/chromebooks/)
- **Knowledge and AI:**
  - Halo includes AI in every licence, with AI KB suggestions for self-resolve. — [Halo education](https://www.usehalo.com/use/education)
  - Freshservice's Freddy AI Copilot costs $29/agent/month, Pro/Enterprise only. — [eesel Freshservice pricing](https://www.eesel.ai/blog/freshservice-pricing) (aggregator)
  - Jitbit AI (semantic search, reply generation, article suggestions) is $999 one-off on self-hosted or included in SaaS. — [Jitbit](https://www.jitbit.com/helpdesk/purchase/)
  - ManageEngine has the Zia conversational AI. — [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html)
  - Spiceworks has no AI. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- **Satisfaction surveys:** GLPI has satisfaction surveys and statistics. — [GLPI features](https://www.glpi-project.org/en/features/). Spiceworks lacks CSAT. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- **Recurring/scheduled work:** GLPI 11 adds recurrent changes, e.g. every Wednesday evening for Windows update deployments. — [Omnicom GLPI 11](https://www.omnicom.digital/en/2025/11/17/discover-the-new-glpi-11/)
- **Approvals:** ManageEngine "automated approvals". — [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html). The DfE standard expects an approver to be recorded "if required". — [GOV.UK](https://www.gov.uk/guidance/meeting-digital-and-technology-standards-in-schools-and-colleges/it-support-standards-for-schools-and-colleges)
- **Facilities and room booking:**
  - TOPdesk includes facility management on all plans, with reservations as an add-on. — [TOPdesk pricing](https://www.topdesk.com/en/pricing/)
  - Halo offers room and space booking. — [Halo education](https://www.usehalo.com/use/education)
  - Civica and Every combine IT with premises and maintenance. — [Civica](https://www.civica.com/en-us/sector-pages/education/software-multi-academy-trusts/school-operations-and-compliance-software/); [Every G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369)
- **Channels:**
  - Email-to-ticket: Civica, VIZOR and Desk365. — [Civica](https://www.civica.com/en-us/sector-pages/education/software-multi-academy-trusts/school-operations-and-compliance-software/); [VIZOR](https://www.vizor.cloud/school-help-desk-software/); [Desk365 CTS](https://www.desk365.io/customers/cts-uk/)
  - Teams: Freshservice, TOPdesk and Halo. — [Freshservice education](https://www.freshworks.com/freshservice/solutions/education/); [TOPdesk education](https://www.topdesk.com/en/industries/help-desk-software-for-education/); [Halo Teams guide](https://usehalo.com/haloitsm/guides/1080/)
  - Spiceworks has no native Teams or Slack integration. — [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
- **Assurance and hosting:**
  - Halo: UK data location; ISO 27001, Cyber Essentials Plus, SOC 2 Type II. — [Halo G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/595769909588872); [Halo education](https://www.usehalo.com/use/education)
  - Hornbill hosts in the UK. — [Hornbill G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/221286282512590)
  - Every hosts in the UK. — [Every G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369)
  - AssetTap is UK-hosted. — [AssetTap](https://assettap.co.uk/)
  - Freshworks regions are US, EEA, UAE, India and Australia, with no UK region listed. — [eesel Freshdesk residency](https://www.eesel.ai/blog/freshdesk-data-residency)
  - Spiceworks data is not encrypted at rest. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- **Contracts register with expiry reminders and replacement budget planning:** AssetTap. — [AssetTap](https://assettap.co.uk/). Also GLPI financial management (budgets, contracts, licences). — [GLPI features](https://www.glpi-project.org/en/features/)
- **Multi-tenant / trust separation:**
  - Halo: multi-site "location-based configurations". — [Halo](https://usehalo.com/haloitsm/sectors/itsm-software-for-education/)
  - GLPI: "entities". — [GLPI](https://www.glpi-project.org/en/features/)
  - Snipe-IT: "multi-company". — [Snipe-IT](https://snipeitapp.com/features)
  - AssetTap: school-scoped permissions. — [AssetTap](https://assettap.co.uk/)
  - Desk365: per-school queues. — [Desk365 CTS](https://www.desk365.io/customers/cts-uk/)
  - Every and Civica: trust-wide reporting. — [Every G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369); [Civica](https://www.civica.com/en-us/sector-pages/education/software-multi-academy-trusts/school-operations-and-compliance-software/)
- **Small-tool ceiling:** a UK MSP serving ~70 schools "outgrew" osTicket (needed multi-client support and modern functionality). — [Desk365 CTS](https://www.desk365.io/customers/cts-uk/)

### Inferences
- **What EduHelpdesk appears to have.** The project memory index (not re-verified in code here) lists: ticket queues and bulk actions; templates; SLA work-day periods; assets with warranty and loans; loan kits; parts inventory; requester portal with service-catalogue tiles; custom roles; TOTP 2FA; Teams channel webhook; desktop notifications; onboarding checklists; projects and quotes; finance/disposal reports; Spiceworks import. Email-to-ticket was deliberately ruled out. Against the products above, EduHelpdesk already covers more of the "school operations" scope than Spiceworks Cloud (which has no SLA, CSAT or Teams) and is comparable in breadth to AssetTap.
- **Not detected in EduHelpdesk's code.** A quick grep of Models/Pages/Services for intune/jamf/google admin/wonde/groupcall/QR (other than the 2FA QR)/recurring/satisfaction/CSAT/OpenID/knowledge base/FAQ found no matches. It is not exhaustive, but suggests these market features are probably absent:
  - Intune/Jamf/Google Admin device sync;
  - MIS/Wonde sync;
  - asset QR labels and scan-to-audit;
  - recurring/scheduled tickets;
  - satisfaction surveys;
  - Entra/Google SSO;
  - a knowledge base/FAQ.
- **Most school-relevant gaps.** The commercial features that matter most for a school (rather than generic ITSM) are, in rough order:
  1. MDM device sync, because Intune or Google Admin is the device record of truth in most schools;
  2. Entra ID / Google SSO;
  3. QR/barcode scanning for audits and loans on a phone;
  4. KB/FAQ and known-issue announcements in the portal;
  5. a DfE IT-support-standard evidence report (request log fields, SLA performance, registers, annual review pack);
  6. MIS/Wonde sync for rooms and staff.
- **Features EduHelpdesk can skip.** Change/problem management, CMDB and AI agents are differentiators for large ITSM vendors but are rarely needed by a 2–6 technician school team.

### Gaps
- Could not verify whether Halo or TOPdesk support Wonde/Groupcall/MIS sync via their generic API or connector tools. No public evidence found either way.
- Did not find recurring-ticket, canned-response, ticket-merge or time-tracking specifics for most products in the time available. These are standard in Halo, Freshservice, Zendesk, ManageEngine and Jitbit per general product knowledge, but I did not cite them, so treat them as unverified.
- No vendor was found offering parental-payment or insurance-claim workflows inside a helpdesk in the UK. edde and Freedom Tech handle these as separate services.

## Q3. Which features are common to almost all (table stakes) vs differentiators?

### Takeaway
Table stakes, present in essentially every product reviewed (including free and open-source ones):
- ticket logging via web portal;
- assignment and categories;
- status tracking visible to the requester;
- a basic KB/FAQ;
- some asset register;
- roles;
- reporting;
- MFA or SSO.

SLAs and asset management are near-universal but are often moved to higher paid tiers (TOPdesk Engaged+, Freshservice Growth+). Spiceworks lacks SLAs entirely.

Differentiators:
- MDM/MIS sync;
- 1:1 device programmes;
- trust-wide multi-tenant reporting;
- UK hosting and certifications;
- facilities and room booking;
- included AI;
- DfE standards alignment;
- per-school pricing instead of per-agent.

### Cited Findings
- **Portal + KB + ticketing + assets on entry tiers:**
  - TOPdesk Essential (£51) includes incident management, asset management, self-service portal, KB and facility management. — [TOPdesk pricing](https://www.topdesk.com/en/pricing/)
  - Freshservice Starter has ticketing, incident, KB and portal. — [rezolve.ai (aggregator)](https://www.rezolve.ai/blog/freshservice-pricing-and-plans)
  - Spiceworks free has portal, KB, inventory and mobile apps. — [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
  - GLPI (free) has KB, self-service portal and inventory. — [GLPI](https://www.glpi-project.org/en/features/)
- **SLAs tiered or missing:**
  - TOPdesk: SLA management only on Engaged (£72) and above. — [TOPdesk pricing](https://www.topdesk.com/en/pricing/)
  - Freshservice: Growth ($49) adds asset tracking and SLA management. — [rezolve.ai (aggregator)](https://www.rezolve.ai/blog/freshservice-pricing-and-plans)
  - Spiceworks: no SLA tracking. — [siit.io](https://www.siit.io/tools/trending/spiceworks-review)
  - SolarWinds WHD: SLA included. — [SolarWinds](https://www.solarwinds.com/web-help-desk/pricing)
  - AssetTap: response and resolution SLAs. — [AssetTap](https://assettap.co.uk/)
  - InvGate: SLAs and OLAs. — [Capterra](https://www.capterra.com/p/133392/Service-Desk/)
- **Change/problem management is a mid-tier ITSM differentiator:** TOPdesk Engaged+. — [TOPdesk pricing](https://www.topdesk.com/en/pricing/). Freshservice Pro. — [rezolve.ai](https://www.rezolve.ai/blog/freshservice-pricing-and-plans)
- **Roles granularity:** Spiceworks has only admin and tech. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system). GLPI has 8 profile types. — [GLPI](https://www.glpi-project.org/en/features/). Snipe-IT has granular role permissions. — [Snipe-IT](https://snipeitapp.com/features)
- **Deployment:**
  - Self-hosted options: SolarWinds WHD. — [SolarWinds](https://www.solarwinds.com/web-help-desk/pricing). Jitbit (Windows server, perpetual). — [Jitbit](https://www.jitbit.com/helpdesk/purchase/). ManageEngine (on-prem and cloud). — [ManageEngine K-12](https://www.manageengine.com/products/service-desk/industry/help-desk-software-k12-schools.html). GLPI, osTicket and Snipe-IT (open source). — [GLPI](https://www.glpi-project.org/en/features/); [osTicket](https://osticket.com/category/releases/); [Snipe-IT pricing](https://snipeitapp.com/pricing). HaloPSA (cloud or on-prem). — [Flamingo](https://www.flamingo.run/blog/halopsa-review)
  - SaaS only: TOPdesk. — [TOPdesk pricing](https://www.topdesk.com/en/pricing/). Spiceworks (cloud only since 2021). — [Comparitech](https://www.comparitech.com/net-admin/spiceworks-review/)

#### Compact matrix (Y = cited above; ? = not found; N = cited as absent)

| Product | UK edu positioning | Hosting | SLA | KB/portal | Assets | MDM sync | Loans | Multi-site/trust | Mobile app | SSO | UK data |
|---|---|---|---|---|---|---|---|---|---|---|---|
| AssetTap | Y (UK schools/MATs) | UK SaaS | Y | ? | Y | Intune/M365 | Y | Y | ? | ? (M365 user import) | Y |
| Civica Edu Ops | Y (MATs) | SaaS | ? | ? | Y | ? | Y | Y | ? | ? | ? |
| Every (IRIS) | Y | UK SaaS | ? | ? | Y | ? | ? | Y | Y (assets) | Y (federation) | Y |
| HaloITSM | Y (names MATs) | UK SaaS (AWS); PSA on-prem | Y | Y | Y | Intune/Jamf/Google | ? | Y | Y | Y (Entra SAML) | Y |
| TOPdesk | partial (HE/Canada) | SaaS | Engaged+ | Y | Y | ? (Lansweeper) | ? | ? | ? | ? | ? |
| Freshservice | US HE | SaaS | Growth+ | Y | Growth+ | Intune/Jamf | Y (K-12) | ? | ? | ? | N (no UK region) |
| Zendesk | generic | SaaS | ? | Y | ? | ? | ? | ? | ? | ? | ? |
| ManageEngine SDP | US K-12 | Cloud + on-prem | Y | Y | Y | ? | Y | ? | ? | ? | ? |
| SolarWinds WHD | none | On-prem | Y | ? | Y (+discovery) | ? | ? | ? | ? | ? | self-host |
| Spiceworks Cloud | none | SaaS | N | Y | Y (scan) | ? | ? | ? | Y | MFA only | ? |
| Jitbit | none | SaaS + self-host | ? | ? | ? | ? | ? | ? | Y | Y (SAML) | ? |
| GLPI | none | Self-host / GLPI Network | ? | Y | Y | ? | ? | Y (entities) | ? | Y (LDAP/SSO) | self-host |
| Snipe-IT (assets only) | none | Self-host / hosted | n/a | n/a | Y | Jamf/Intune+ | Y | Y (multi-company) | ? | Y (SAML/SCIM) | self-host |
| VIZOR | US K-12 | SaaS | ? | Y | Y | Google/Intune/Jamf | Y (1:1) | ? | ? | ? | ? |
| Hornbill | none (UK vendor) | UK SaaS | ? | ? | Y | ? | ? | ? | N (browser) | Y | Y |

### Inferences
- **For a UK school buyer, table stakes are now:**
  - portal + ticket status visibility (the DfE standard requires it);
  - priorities and SLAs;
  - an asset register;
  - MFA;
  - exports and reports;
  - multi-site separation for MATs.
- **Where EduHelpdesk could differentiate.** These intersect DfE requirements and UK MAT structure and are not well served by any one product found:
  - UK-specific integration (Wonde/MIS);
  - DfE IT-support-standard reporting;
  - per-school (not per-agent) or free self-hosted pricing;
  - trust-wide roll-up reporting combined with school-scoped permissions.
- **Generic ITSM differentiators are not school priorities.** AI, change/problem/CMDB and omnichannel social are the main differentiators among generic ITSM vendors, but they are low-priority for school IT.

### Gaps
- The matrix has many "?" cells. Those were not verified within the time budget and should not be read as "No".

## Q4. Pricing ranges for a typical single secondary school (2–6 technicians) and a 5–20 school MAT

### Takeaway
- **Single secondary (2–6 techs):** list prices range from £0 (Spiceworks ≤5 seats, ManageEngine free ≤5 techs, open source) through about £700/yr (AssetTap secondary, flat) and roughly £800–£2,500/yr (Halo at £34/agent/month), up to £1,700–£5,200/yr (TOPdesk Engaged, the tier with SLAs).
- **MAT, 5–20 schools (assumed 10–25 technicians):** roughly £2,100–£4,800/yr (AssetTap MAT per-school pricing) to £4,000–£10,000/yr (Halo) and £8,600–£21,600/yr (TOPdesk Engaged). Freshservice Growth runs about $5.9k–$14.7k/yr.
- Education discounts exist (Halo, InvGate, possibly ManageEngine) but amounts are rarely published.

### Cited Findings (list prices; VAT and discounts excluded)
- **AssetTap:** £390/yr (small primary), £490/yr (medium/large primary), £690/yr (secondary/college). MAT: £149/month for 3 schools + £15/month per extra school. — [AssetTap](https://assettap.co.uk/)
- **HaloITSM:** £34/licence/month on G-Cloud 14, with education pricing available. — [G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/595769909588872). Minimum agent count not found.
- **TOPdesk:** £51 (Essential) / £72 (Engaged) / £101 (Excellent) per agent/month. — [TOPdesk pricing](https://www.topdesk.com/en/pricing/). Aggregators cite volume-discount bands at 50, 150, 500 and 1,000 operators. — [search extract; vendorbenchmark/checkthat (aggregator)](https://vendorbenchmark.com/vendors/topdesk-pricing)
- **Freshservice:** $19 / $49 / $99 per agent/month annual (Starter/Growth/Pro); $29 / $59 / $119 monthly; no free plan. — [eesel (aggregator)](https://www.eesel.ai/blog/freshservice-pricing); [rezolve.ai (aggregator)](https://www.rezolve.ai/blog/freshservice-pricing-and-plans)
- **Zendesk (UK):** Support Team £15, Suite Team £45, Suite Professional £89 per agent/month annual. — [Featurebase (aggregator)](https://www.featurebase.app/blog/zendesk-pricing); vendor page: [zendesk.co.uk/pricing](https://www.zendesk.co.uk/pricing/) (not fetched)
- **ManageEngine ServiceDesk Plus Cloud:** $13 (Standard) to $67 (Enterprise) per technician/month (English); free plan up to 5 technicians; priced per technician, not per end user. — [SuperOps (aggregator)](https://superops.com/blog/manageengine-pricing); [Costbench (aggregator)](https://costbench.com/software/itsm/manageengine-servicedesk-plus/). A 10% education discount was mentioned in a search extract, probably from the [G-Cloud 14 pricing PDF](https://assets.applytosupply.digitalmarketplace.service.gov.uk/g-cloud-14/documents/721684/720849265376472-pricing-document-2024-05-03-1249.pdf). *Unverified:* the PDF did not parse.
- **SolarWinds Web Help Desk** (on-prem, annual per tech): $533 (1–5), $524 (6–10), $517 (11–20), $507 (21–30), down to $320 (501+). — [SolarWinds](https://www.solarwinds.com/web-help-desk/pricing). SolarWinds Service Desk (cloud) is $39 / $79 / $99 per tech/month. — [SolarWinds Service Desk pricing](https://www.solarwinds.com/service-desk/pricing)
- **Spiceworks Cloud:** free ≤5 tech seats (with ads); Premium $5/seat/month annual, applied to all seats once over 5. — [eesel](https://www.eesel.ai/blog/spiceworks-ticketing-system)
- **InvGate:** Starter $1,499/yr for 5 agents; Pro $500/agent/yr (5–50); Enterprise from $12,000/yr; education discounts available. — [Capterra (aggregator)](https://www.capterra.com/p/133392/Service-Desk/); [InvGate pricing](https://invgate.com/pricing?p=serviceManagement) (not fetched)
- **Hornbill:** £34.16–£58.33 per user (billing period not shown in my extract). — [G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/221286282512590)
- **Jitbit:**
  - Self-hosted perpetual: $2,199 (10 agents), $3,799 (20 agents), $6,499 (unlimited), each with 1 year of upgrades. — [Jitbit purchase](https://www.jitbit.com/helpdesk/purchase/)
  - SaaS: the vendor page shows "$24/month" (unit unclear). An aggregator lists $29/month (1 agent) to $249/month (9 agents + $29 per extra). — [eesel (aggregator)](https://www.eesel.ai/blog/jitbit-helpdesk-pricing). These conflict.
- **Every Compliance (IRIS):** £1,526/yr per licence, education pricing available. — [G-Cloud](https://www.applytosupply.digitalmarketplace.service.gov.uk/g-cloud/services/631589798045369)
- **Helpdesk 365 (M365/SharePoint):** $19.99 / $29.99 / $44.99 / $59.99 per user/month (Standard to Enterprise); free for 1 user. — [G2 (aggregator)](https://www.g2.com/products/helpdesk-365/pricing)
- **Snipe-IT:** self-hosted free and open source. Hosted: $399.99/yr (Basic), $999.99/yr (Small Business), $2,499.99/yr (Dedicated small), $5,000–$7,500/yr (larger dedicated). No education discount listed. — [Snipe-IT pricing](https://snipeitapp.com/pricing)
- **GLPI and osTicket:** free open source; GLPI Network offers paid hosting (price not captured). — [GLPI](https://www.glpi-project.org/en/features/); [osTicket](https://osticket.com/category/releases/)
- **SharePoint IT help desk template:** no extra licence beyond M365. — [Microsoft Support](https://support.microsoft.com/en-us/office/use-the-it-help-desk-sharepoint-site-template-808d0c01-1ea6-4962-8efc-f458d2102c77)

### Inferences (my arithmetic on the list prices above; annual, before VAT and discounts)

| Product | Single secondary, 2 techs | 6 techs | MAT, 10 techs | MAT, 25 techs |
|---|---|---|---|---|
| Spiceworks Cloud | $0 | $360 | $600 | $1,500 |
| ManageEngine SDP Cloud Std / Ent | $312 / $1,608 (or free ≤5) | $936 / $4,824 | $1,560 / $8,040 | $3,900 / $20,100 |
| Helpdesk 365 Standard | ~$480 | ~$1,439 | ~$2,399 | ~$5,997 |
| HaloITSM (£34) | £816 | £2,448 | £4,080 | £10,200 |
| Zendesk Suite Team (£45) | £1,080 | £3,240 | £5,400 | £13,500 |
| Freshservice Starter / Growth | $456 / $1,176 | $1,368 / $3,528 | — / $5,880 | — / $14,700 |
| SolarWinds WHD | $1,066 | $3,144 | $5,240 | $12,675 |
| InvGate | $1,499 (Starter, 5 agents) | $3,000 (Pro) | $5,000 | $12,500 |
| TOPdesk Essential / Engaged | £1,224 / £1,728 | £3,672 / £5,184 | £6,120 / £8,640 | £15,300 / £21,600 |
| Jitbit self-hosted | $2,199 one-off (≤10) | $2,199 one-off | $2,199–$3,799 | $6,499 one-off (unlimited) |
| AssetTap (per school, not per tech) | £690 | £690 | 5 schools £2,148; 10 schools £3,048 | 20 schools £4,848 |
| Open source (GLPI/osTicket/Snipe-IT) | £0 licence + hosting/staff | same | same | same |

- The 10–25 technician range for a 5–20 school MAT is my assumption, not a sourced figure. Adjust if the user has real MAT staffing data.
- Per-agent pricing penalises MATs with many part-time or site-based technicians. Per-school pricing (AssetTap) or free self-hosted is a clear price advantage for small schools.

### Gaps
- No published education-discount percentages were found for Halo, TOPdesk, Freshservice, Zendesk, SysAid or InvGate.
- SysAid, Incident IQ, VIZOR and Civica Education Operations publish no prices that I found.
- Jitbit SaaS pricing conflicts between sources. The Hornbill per-user billing period is unclear.
- There is no UK benchmark for actual MAT spend on helpdesk software. Public procurement data (e.g. Contracts Finder) was not searched.
