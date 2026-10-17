---
title: Asset, part and loan rules
summary: The review, check and support windows, the academic year, the reorder threshold, repeat borrowing, and asset types and models.
area: Settings
kind: page
pages: /Settings/InventoryRules, /Settings/AssetTypes, /Settings/AssetModels
needs: Settings:Edit
keywords: inventory rules review window check interval end of support academic year reorder threshold repeat borrowing asset type lifespan desk model make
order: 22
---
**Settings → Asset, part & loan rules** sets when things are flagged across the inventory.

| Setting | What it does |
| --- | --- |
| Review window (days) | Assets whose warranty ends, or that are due for replacement, within this many days come up for review on the Overview (with any already past) |
| Check due soon (days) | An asset's DfE check counts as *due soon* this many days before it's due |
| Support ending warning (days) | An asset is flagged this many days before its support or lease ends |
| Months between checks | Recording a check sets the next one this many months later |
| Academic year starts in | The finance and audit report groups purchases and disposals by academic year, so a July purchase belongs to the year that started the September before |
| Default reorder threshold | A part is low in stock at this quantity or below, unless the part has its own |
| Repeat borrowing | The loan report flags anyone who's taken this many kits within this many days |

See [Asset checks and end of support](asset-checks).

## Asset types

**Settings → Asset types** lists the kinds of asset (laptop, projector, boiler). Each has:

- an **expected lifespan** in years - an asset's replacement date is its purchase date plus the lifespan, unless a date is typed on the asset. Leave it blank to use only typed dates;
- a **desk** - plant such as boilers and extinguishers belongs to Site, so the asset list and report keep it apart from IT's kit. See [Desks: IT, Site and Cleaning](desks#plant).

## Asset models

**Settings → Asset models** lists models, each linked to its **make**. The Add and Edit asset pages then only offer that make's models. A model set to *Any make* is offered for every make.
