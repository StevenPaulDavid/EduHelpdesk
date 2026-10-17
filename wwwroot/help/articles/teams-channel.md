---
title: Posting to a Teams channel
summary: A short message in a Microsoft Teams channel for new portal tickets and overdue tickets, one channel per desk.
area: Settings
kind: page
pages: /Settings/TeamsChannel
needs: Settings:Edit
keywords: teams microsoft channel webhook workflow post message overdue new ticket
order: 15
---
EduHelpdesk can post a short message to a **Microsoft Teams channel** when a requester submits a ticket in the staff portal, and when a ticket becomes overdue.

## Channels

Each desk posts its own tickets to its own channel, so the site team's channel never hears about IT's tickets. A desk without a channel doesn't post.

1. In Teams, create a **workflow** that receives a webhook request and posts to the channel (the steps are at the bottom of the page - Microsoft has retired the older Incoming Webhook connector).
2. Paste its **webhook address** under the desk and save.
3. Press **Send a test message** to check it arrives. This works even while posting is switched off.

For safety, a saved address is never shown again, only where it posts. To change it, paste a new one.

## What posts

- **Post to Teams** - the master switch. Off pauses every desk and keeps the addresses.
- **A requester submits a new ticket** in the staff portal. Tickets a technician logs, imports and onboardings never post.
- **A ticket becomes overdue** - checked every few minutes. If several turn overdue at once (the morning after a holiday), it posts one message with a link to the overdue list.

## What is sent

Only that something happened, the ticket number, and a button that opens the ticket for anyone signed in. Never the title, description, comments or the requester's name - a Teams channel is held by Microsoft and may be seen by people outside the team.

If Teams can't be reached, EduHelpdesk tries a few times and then gives up on that message. It never holds up a ticket; failures show beside **Send a test message** and in the error log.
