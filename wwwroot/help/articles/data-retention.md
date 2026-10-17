---
title: Data retention
summary: Removing personal data the helpdesk no longer needs - old tickets, leavers' details, old audit entries.
area: Settings
kind: page
pages: /Settings/Retention
needs: Settings:Edit
keywords: retention gdpr personal data delete anonymise leavers old tickets audit log schedule months
order: 17
---
Your school's retention schedule sets how long personal data is kept. This page applies it every night.

## Rules

Each rule is in **months**; **0** switches it off.

| Rule | What happens |
| --- | --- |
| Delete closed tickets | Tickets closed more than this many months ago are deleted, with their updates, notes, history, files and custom fields, and their audit lines. Open tickets are never touched, and numbers are never reused |
| Anonymise people who have left | Requesters marked inactive more than this many months ago lose their name, email, department and location everywhere. Their tickets and loans stay, so counts still add up. Anyone still holding an asset or kit, or with an open ticket or active project, is left until that's sorted |
| Delete old audit log entries | Entries older than this are deleted. There's a minimum, because the log is how the school shows who changed what |

## When it runs

Rules run each night **after the backup**, and only once a backup has worked in the last day, so nothing is removed that no backup holds. Backups keep their own copies until they age out (**Settings → Backups & data**), so removed data is gone for good only after that.

**Last run** says what was removed. **Back up, then apply now** runs it straight away.

> **Important:** Reports covering months whose tickets have been deleted will no longer count them. Set the ticket rule long enough for the reports you need.
