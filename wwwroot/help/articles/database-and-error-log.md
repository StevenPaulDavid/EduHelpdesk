---
title: The database and error log
summary: The health check, every table as stored, everything held about one record, and the error log.
area: Settings
kind: page
pages: /Settings/Database, /Settings/Log
needs: System.RawDatabase, Settings:Edit
keywords: database raw data tables health disk space save time error log reference troubleshooting
order: 19
---
These pages are for looking into a problem. Nothing on them changes anything.

## Database

**Settings → Database** needs the *View raw database* tick.

- **Health** - the database and attachment sizes and how fast they're growing, free disk space, and how long saves take. When something needs attention, Administrators also see a warning on the Overview. The warning limits (slow saves, low disk) can be changed with Settings: Edit.
- **Everything stored about one thing** - pick what (a ticket, a person, an asset...) and which one, to see its rows from every table that mentions it. Records also have a **Raw data** link on their own page.
- **Tables** - every table exactly as stored, with what each holds.

Passwords, two-step secrets, recovery codes and session tokens show masked; only an Administrator sees them. **Each table or record opened is recorded in the audit log**, because it shows personal data.

## Error log

**Settings → Error log** lists warnings and errors, newest first. When someone sees an error page, it gives a **reference**; type it in to find that exact error. Tick **Errors only** to hide warnings.

The log files can hold names or ticket text from the moment something went wrong, so treat them like the rest of the helpdesk's data. *Nothing has been logged* is how it should be.
