---
title: Importing from spreadsheets
summary: Bringing technician accounts and lists in from CSV files, and where requesters, assets and Spiceworks tickets are imported.
area: Settings
kind: page
pages: /Settings/Imports, /Settings/SpiceworksImport
needs: Settings:Edit
keywords: import csv spreadsheet users requesters technicians technician accounts lists bulk spiceworks quick start guide temporary password
order: 20
---
Rather than typing people and lists in one at a time, import them from **CSV** files (save a spreadsheet *as CSV* in Excel). Each import has a template to download, with example rows to replace.

## Technician accounts

The technicians file has these columns, in order, with a header row: **Name, Email, Team, Role**. **Role** is optional and defaults to *Technician*; it must match the name of an existing role.

## Temporary passwords

Tick **Give each a temporary password and print their quick start guides** to give everyone imported a temporary password (changed the first time they sign in) and go straight to their quick start guides, one per page, to print and hand out. Without it they're imported with no password; you can give them one later from **People**. The staff list import below has the same box.

## Option lists

List files have one column, **Name**, with a header row and one value per row - departments, locations, makes and so on. Asset models can have a second column, **Make**, to link each model to its make. Blank rows and values that already exist are skipped.

## Elsewhere

- **Requesters** - the staff who use the portal - come from the MIS or HR system's staff list: **People → Import a staff list** (.xlsx or .csv, with a preview). It adds new people and updates existing ones, and records start and leave dates for the access register.
- **Assets** are imported on their own page, with column matching and a preview (from the asset list).
- **Spiceworks** - moving from Spiceworks Cloud Help Desk, its data export brings tickets, comments, history and the people on them across in one go. It can be run again while both systems are in use; it updates rather than duplicates, and the last import can be undone.
