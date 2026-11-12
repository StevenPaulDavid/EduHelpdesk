---
title: Phone app
summary: Pairing an Android phone with the EduHelpdesk app, what it can do, revoking a phone, and turning the app on for the school.
area: Your account
kind: page
pages: /Phones
keywords: phone app android mobile pair pairing qr code scan revoke lost phone today planned planner tickets on my way assign to me log reply close alerts notifications app lock update apk
order: 2
---
The EduHelpdesk app shows your tickets on an Android phone. With it you can see Today, Mine, Unassigned, Overdue and Open for the desk you're working on, open a ticket, press **Assign to me** or **On my way**, reply or add an internal note (with photos), change the status or priority, close it, and log a new ticket or a job already done. Swipe a ticket on Unassigned to the right to take it. The app follows this ticket page's rules and words, so it can't do anything the web page wouldn't let you do.

The phone can do only what your role allows, on the desks you see. It works on the school's network, or anywhere the school has published the helpdesk.

## Today

**Today** is first along the top of the app's ticket list: the jobs planned for you today on the [planner](ticket-planner), and any of yours planned for an earlier day that still aren't done - the same as your page of today's [daily sheet](daily-sheet#a-days-planned-jobs). The ones that slipped come first.

Every ticket with a planned day shows it, on the list and on the ticket: *Planned today*, *Planned Thu 13 Nov*, in red once the day has gone by. The app shows the plan but doesn't change it: plan tickets on the planner in the browser.

Today needs version 1.4 of the app; older versions just don't show it.

## Pairing a phone

1. Install the app and open it. To get it, open the address `/api/mobile/v1/app/android` on this helpdesk from the phone's browser, or ask whoever looks after EduHelpdesk.
2. Here, open **Phones** in the menu under your name, type your password and press **Add a phone**.
3. In the app, press **Scan pairing code** and point the camera at the code.

The code works once, for five minutes. Your password and two-step sign-in were checked here, so the phone needs nothing else. It stays signed in until you revoke it, your password changes, or it goes 90 days without being used.

**Can't scan?** In the app, choose **Enter the address instead**, type the address shown under *Can't scan a code?* and sign in with your email and password (and your two-step code, if you have one). If the school's certificate isn't one Android knows, the app shows its fingerprint: check it matches the one on the Phones page before trusting it.

## Revoking a phone

On **Phones**, press **Revoke** beside it. It's signed out at once and needs pairing again. Do this straight away for a lost or stolen phone. Changing your password signs out all your phones too. Anyone who can edit technician accounts sees everyone's phones there and can revoke any of them.

## Turning it on

The app is off until an administrator turns it on in [Sign-in security](sign-in-security), and only roles with **Use the phone app** ticked can use it (People → Roles). The roles a new helpdesk starts with have it ticked; a helpdesk upgraded from an earlier version doesn't tick it for anyone.

The app only connects over **HTTPS**. If the helpdesk is on plain http://, run **Enable-Https.cmd** from the folder it was installed from first. The pairing code carries the certificate's fingerprint, so phones trust a self-signed certificate without anyone installing it on them.

## Alerts

The app puts new tickets on your desks, replies, and tickets given to you in the phone's notifications. While the app is open they arrive at once; when it's closed the phone checks about every 15 minutes, and Android can stretch that to save battery. Each kind can be switched off in the app's **Account** screen, and by default alerts only arrive during your desk's working hours (its own hours, or the school day). For anything urgent, keep using pings on PCs.

Lock-screen alerts and the app's Alerts tab say only the kind and the number - "Assigned to you: IT-1001" - never what the ticket says.

## App lock

With **App lock** on (in the app's Account screen), the app asks for the phone's fingerprint, face or PIN after 5 minutes away. An administrator can make it compulsory for every phone in [Sign-in security](sign-in-security). Whenever the phone is connected to the school, the app also keeps its screens out of screenshots and the recent-apps view.

## Updating the app

Each EduHelpdesk release brings the matching app. When the helpdesk has a newer one than your phone, the app offers **Update** on its Tickets screen and in Account, and Android's installer takes it from there. If the phone's app is too old for this helpdesk, it asks you to update before carrying on.
