---
title: Importing assets
summary: Bringing assets in from a spreadsheet or the DfE register template, matching the columns, and checking before anything changes.
area: Assets and inventory
kind: page
pages: /Assets/Import
needs: Assets:New
keywords: import assets csv excel spreadsheet dfe template columns match preview update bulk no tag asset number serial contract lease
order: 3
---
Rows are matched to existing assets by **asset tag**: a tag that already exists updates that asset, and any other tag adds a new one. Rows with no tag can be matched by serial number or numbered for you (below). **Nothing changes until you've reviewed the result.**

## 1. Choose a file

A CSV from Excel, Google Sheets or another system, or an Excel workbook - including the **DfE digital technology asset register template**, filled in. One row per asset with a header row. Comma, semicolon and tab separated files all work; the **separator** is detected automatically. **Download a template file** gives the columns EduHelpdesk knows.

## 2. Match the columns

EduHelpdesk guesses what each column is from its heading. Check the guesses and change any that are wrong; columns set to *Don't import* are ignored. Then the options:

- **Dates in the file are written as** - day first (UK) or month first. Year-first dates are always understood.
- **Type** and **status** for new assets that don't have one.
- **Give assets with no asset tag a built-in number** - a row with no tag first looks for the asset with the same **serial number** and updates it; otherwise it's added with the next number, such as AST-00001. Set the prefix and how many digits in **Settings → Asset, part & loan rules**. Numbers carry on from the highest already used and are never given out twice. A row with neither a tag nor a serial number gets a new number every time the file is imported, so the preview warns about it. Ticked to start with when no column looks like an asset tag.
- **Add makes, models, types, locations, statuses, suppliers and contracts that aren't in the lists yet** - off, a row with an unknown value is skipped instead.
- **A blank cell clears the value on an existing asset** - leave this off (recommended), so blank cells leave existing values alone.

## Linking assets to a contract

Map a column to **Contract** to link each asset to a contract on the [contracts register](contracts-register) by its name (capitals and spaces at the ends don't matter). A name that isn't on the register yet is added as a new contract with just that name, and the row's supplier if it has one. It shows **Needs details** on the register, the compliance summary and the overview until someone opens it and saves it from **Edit contract**.

## 3. Review before importing

The preview shows how many assets would be **added**, **updated**, left **unchanged**, or **skipped** (with why), and any warnings. Warnings don't stop a row. Press **Import** to apply it, or **Download the skipped rows** to fix them in the spreadsheet and import again.
