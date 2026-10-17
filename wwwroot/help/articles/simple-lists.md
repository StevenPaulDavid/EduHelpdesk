---
title: The simple lists
summary: Categories, priorities, teams, departments, rooms and the other pick-lists - adding, renaming and removing values.
area: Settings
kind: page
pages: /Settings/Categories, /Settings/Priorities, /Settings/Teams, /Settings/Departments, /Settings/Locations, /Settings/Buildings, /Settings/BudgetCodes, /Settings/Cleaners, /Settings/AssetMakes, /Settings/AssetStatuses, /Settings/AssetConditions, /Settings/PartCategories, /Settings/PartLocations, /Settings/LoanReasons, /Settings/ContractTypes, /Settings/SpendCategories, /Settings/ContractDurations, /Settings/ContractStatuses, /Settings/PersonTypes, /Settings/AccessCategories, /Settings/RevokeReasons, /Settings/PurchasingRequirements
needs: Settings:Edit
keywords: list option category priority team department location room building budget code cleaner make condition rename delete add
order: 2
---
Many Settings pages are a simple list of values that appear in drop-downs elsewhere: categories, priorities, teams, departments, rooms, budget codes, cleaners, asset makes and so on. They all work the same way.

## Adding, renaming and removing

- **Add** - type the new value in the box at the top and press **Add**.
- **Rename** - press the pencil beside a value, change it and **Save**. Everything already using it follows the new name: tickets, assets, people, rounds and history.
- **Remove** - press the bin. A value that's **in use** can't be removed (a room on a cleaning round, a category with tickets, a budget code on a cost line); the message says why. Rename it instead, or move whatever uses it first.

## Lists that sit on a desk

**Categories** and **Teams** each belong to a desk when there's more than one. Pick the desk when you add one; to move it, choose another desk beside it.

- Moving a **category** moves its open tickets with it. They take the new desk's ticket numbers, and lose a team, technician or status that belongs only to the old desk.
- Moving a **team** moves the people in it onto that desk.

See [Desks: IT, Site and Cleaning](desks).

## Some lists with a little more

- **Statuses** have descriptions and can stop the SLA clock. See [Statuses](statuses).
- **Asset types** and **asset models** have extra details (a lifespan, a make). They're covered with the asset register.
- **Locations** are the rooms used on tickets, assets and cleaning rounds. A long list can be imported from a spreadsheet in **Settings → Imports**.
