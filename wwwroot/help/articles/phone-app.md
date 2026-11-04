---
title: Phone app
summary: Pairing an Android phone with the EduHelpdesk app, what it can do, revoking a phone, and turning the app on for the school.
area: Your account
kind: page
pages: /Phones
keywords: phone app android mobile pair pairing qr code scan revoke lost phone tickets on my way assign to me log
order: 2
---
The EduHelpdesk app shows your tickets on an Android phone. With it you can see Mine, Unassigned, Overdue and Open for the desk you're working on, open a ticket, press **Assign to me** or **On my way**, and log a new ticket or a job already done, with photos. Replying and closing still happen on the web: the app's **Reply or close** button opens the ticket here.

The phone can do only what your role allows, on the desks you see. It works on the school's network, or anywhere the school has published the helpdesk.

## Pairing a phone

1. Install the app and open it.
2. Here, open **Phones** in the menu under your name, type your password and press **Add a phone**.
3. In the app, press **Scan pairing code** and point the camera at the code.

The code works once, for five minutes. Your password and two-step sign-in were checked here, so the phone needs nothing else. It stays signed in until you revoke it, your password changes, or it goes 90 days without being used.

**Can't scan?** In the app, choose **Enter the address instead**, type the address shown under *Can't scan a code?* and sign in with your email and password (and your two-step code, if you have one). If the school's certificate isn't one Android knows, the app shows its fingerprint: check it matches the one on the Phones page before trusting it.

## Revoking a phone

On **Phones**, press **Revoke** beside it. It's signed out at once and needs pairing again. Do this straight away for a lost or stolen phone. Changing your password signs out all your phones too. Anyone who can edit technician accounts sees everyone's phones there and can revoke any of them.

## Turning it on

The app is off until an administrator turns it on in [Sign-in security](sign-in-security), and only roles with **Use the phone app** ticked can use it (People → Roles). The roles a new helpdesk starts with have it ticked; a helpdesk upgraded from an earlier version doesn't tick it for anyone.

The app only connects over **HTTPS**. If the helpdesk is on plain http://, run **Enable-Https.cmd** from the folder it was installed from first. The pairing code carries the certificate's fingerprint, so phones trust a self-signed certificate without anyone installing it on them.

Lock-screen alerts and the app's Alerts tab say only the kind and the number - "Assigned to you: IT-1001" - never what the ticket says.
