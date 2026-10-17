---
title: Importing assets
summary: Bringing assets in from a spreadsheet or the DfE register template, matching the columns, and checking before anything changes.
area: Assets and inventory
kind: page
pages: /Assets/Import
needs: Assets:New
keywords: import assets csv excel spreadsheet dfe template columns match preview update bulk
order: 3
---
Rows are matched to existing assets by **asset tag**: a tag that already exists updates that asset, and any other tag adds a new one. **Nothing changes until you've reviewed the result.**

## 1. Choose a file

A CSV from Excel, Google Sheets or another system, or an Excel workbook - including the **DfE digital technology asset register template**, filled in. One row per asset with a header row. Comma, semicolon and tab separated files all work; the **separator** is detected automatically. **Download a template file** gives the columns EduHelpdesk knows.

## 2. Match the columns

EduHelpdesk guesses what each column is from its heading. Check the guesses and change any that are wrong; columns set to *Don't import* are ignored. Then the options:

- **Dates in the file are written as** - day first (UK) or month first. Year-first dates are always understood.
- **Type** and **status** for new assets that don't have one.
- **Add makes, models, types, locations, statuses and suppliers that aren't in the lists yet** - off, a row with an unknown value is skipped instead.
- **A blank cell clears the value on an existing asset** - leave this off (recommended), so blank cells leave existing values alone.

## 3. Review before importing

The preview shows how many assets would be **added**, **updated**, left **unchanged**, or **skipped** (with why), and any warnings. Warnings don't stop a row. Press **Import** to apply it, or **Download the skipped rows** to fix them in the spreadsheet and import again.
