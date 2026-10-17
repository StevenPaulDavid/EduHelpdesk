---
title: Importing from spreadsheets
summary: Bringing requesters, staff accounts and lists in from CSV files, and where assets and Spiceworks tickets are imported.
area: Settings
kind: page
pages: /Settings/Imports, /Settings/SpiceworksImport
needs: Settings:Edit
keywords: import csv spreadsheet users requesters technicians staff accounts lists bulk spiceworks quick start guide temporary password
order: 20
---
Rather than typing people and lists in one at a time, import them from **CSV** files (save a spreadsheet *as CSV* in Excel). Each import has a template to download, with example rows to replace.

## Requesters and staff accounts

| File | Columns, in order, with a header row |
| --- | --- |
| Users (requesters) | Name, Email, Department, Location |
| Technicians (staff accounts) | Name, Email, Team, Role |

For technicians, **Role** is optional and defaults to *Technician*; it must match the name of an existing role.

Tick **Give each a temporary password and print their quick start guides** to give everyone imported a temporary password (changed the first time they sign in) and go straight to their quick start guides, one per page, to print and hand out. Untick it to import them without passwords; you can give temporary passwords later from **People**.

## Option lists

List files have one column, **Name**, with a header row and one value per row - departments, locations, makes and so on. Asset models can have a second column, **Make**, to link each model to its make. Blank rows and values that already exist are skipped.

## Elsewhere

- **Assets** are imported on their own page, with column matching and a preview (from the asset list).
- **Spiceworks** - moving from Spiceworks Cloud Help Desk, its data export brings tickets, comments, history and the people on them across in one go. It can be run again while both systems are in use; it updates rather than duplicates, and the last import can be undone.
