---
title: Sign-in security
summary: Two-step sign-in, recovery keys, the helpdesk address, passwords, lockouts and https.
area: Settings
kind: page
pages: /Settings/SignIn
needs: Settings:Edit
keywords: sign in security two-step 2fa mfa authenticator recovery key password lockout https certificate address
order: 13
---
How helpdesk staff accounts are protected. The staff portal has its own passwords and isn't covered by two-step sign-in.

## Two-step sign-in

After their password, people enter a code from an app on their phone, so a guessed or reused password isn't enough on its own. The page shows how many accounts have it set up, by role. The DfE's cyber security standard for schools expects it on accounts like these, which can change anything in the helpdesk.

- **Require it for everyone** - anyone without it sets it up the next time they use the helpdesk.
- **Make it optional** - each person chooses (from *Two-step sign-in* in their account menu).

Someone who's lost their phone and their recovery codes: open their staff account in **People** and press **Reset two-step sign-in**; they then set it up again.

## Recovery keys

With a **recovery key**, a technician who forgets their password can set a new one from the sign-in page, once, without waiting for an administrator. Each person makes their own from *Recovery key* in their account menu. **Turn recovery keys off** if you'd rather every reset came through an administrator.

## Helpdesk address

The address staff type to reach the helpdesk, printed on the quick start guide given to each new account. Leave it empty to use the address the guide is printed from.

## Passwords and lockouts

- **Encryption** - the page says whether it was reached over **https**. Over plain http, passwords cross the network unencrypted; run `Enable-Https.cmd` from the install folder to fix it (see the install guide).
- **Passwords** - a minimum length, and not one of the most-guessed passwords or the person's own name.
- **New accounts** - get a temporary password they must change the first time they sign in, unless whoever adds the account types one for them to keep.
- **Lockout** - several wrong passwords or codes for one account lock it for a while; too many from one computer block that computer.
- **Signing out** - ends the session on the server, so a copied sign-in cookie stops working too.
