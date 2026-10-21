---
title: Sign-in security
summary: Two-step sign-in, recovery keys, the helpdesk address, showing it inside EduDashboard, passwords, lockouts and https.
area: Settings
kind: page
pages: /Settings/SignIn
needs: Settings:Edit
keywords: sign in security two-step 2fa mfa authenticator recovery key password lockout https certificate address dashboard edudashboard embed frame tile
order: 13
---
How helpdesk technician accounts are protected. The staff portal has its own passwords and isn't covered by two-step sign-in.

## Two-step sign-in

After their password, people enter a code from an app on their phone, so a guessed or reused password isn't enough on its own. The page shows how many accounts have it set up, by role. The DfE's cyber security standard for schools expects it on accounts like these, which can change anything in the helpdesk.

- **Require it for everyone** - anyone without it sets it up the next time they use the helpdesk.
- **Make it optional** - each person chooses (from *Two-step sign-in* in their account menu).

Someone who's lost their phone and their recovery codes: open their technician account in **People** and press **Reset two-step sign-in**; they then set it up again.

## Recovery keys

With a **recovery key**, a technician who forgets their password can set a new one from the sign-in page, once, without waiting for an administrator. Each person makes their own from *Recovery key* in their account menu. **Turn recovery keys off** if you'd rather every reset came through an administrator.

## Helpdesk address

The address staff type to reach the helpdesk, printed on the quick start guide given to each new account. Leave it empty to use the address the guide is printed from.

## Shown inside EduDashboard

Off until you give an address: the helpdesk refuses to be shown inside any other site, so a page elsewhere can't pass itself off as the helpdesk to trick someone into clicking.

To make the helpdesk a tile shown inside the school's **EduDashboard**, type the dashboard's address as it appears in the address bar, such as `https://dashboard.school.org.uk`, and press **Save dashboard address**. Only that site may then show it. Clear the box to turn it off again.

For it to work:

- **Same domain** - keep the two on one domain (helpdesk.school.org.uk and dashboard.school.org.uk, or one server on two ports). Otherwise browsers may not keep technicians signed in inside the dashboard.
- **HTTPS on both, or neither** - a dashboard on https:// can't show a helpdesk on plain http://.
- On the dashboard's tile, choose **Show inside the dashboard** and press **Test** to check.

## Passwords and lockouts

- **Encryption** - the page says whether it was reached over **https**. Over plain http, passwords cross the network unencrypted; run `Enable-Https.cmd` from the install folder to fix it (see the install guide).
- **Passwords** - a minimum length, and not one of the most-guessed passwords or the person's own name.
- **New accounts** - get a temporary password they must change the first time they sign in, unless whoever adds the account types one for them to keep.
- **Lockout** - several wrong passwords or codes for one account lock it for a while; too many from one computer block that computer.
- **Signing out** - ends the session on the server, so a copied sign-in cookie stops working too.
