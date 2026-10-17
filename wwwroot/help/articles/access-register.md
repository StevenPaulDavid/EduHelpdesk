---
title: The access control register
summary: Who can get into which systems and areas, who approved it, MFA, and taking access away when someone leaves.
area: Compliance
kind: page
pages: /Access/Index, /Access/Grant
needs: Access
keywords: access control register permissions systems keys fobs privileged administrator approval mfa leaver remove revoke dfe cyber
order: 3
---
The **access control register** records who can get into which **systems** (the MIS, Microsoft 365, the firewall) and **physical areas and keys** (the server room, a master key), who approved it, and - for anyone who has left - when it was taken away. Open it from **Compliance → Access control**.

## The list

**Who has access** lists live access by default. **Show** picks out problems - **anything to fix**, **leavers still with access**, **privileged access not approved**, **MFA not enabled**, **review overdue** - or **privileged** access, **removed** access, or everything. Filter by system or area, kind, and person type. **Export CSV** downloads it.

## Recording access

Press **Record access** for one person's access to one system or area:

- **Person** - from People. Someone new? Add them there first.
- **System or area** - from the list on *Systems and areas*.
- **Access level** - such as *Read only*, *Teacher*, *Global admin*.
- **Account, key or fob number**.
- **MFA** - enabled, not enabled, not available (a complex password instead), or not applicable. MFA is needed for cloud or remote access and every administrator account; it doesn't apply to keys.
- **Privileged (administrator) access** - the cyber security standard expects a member of **SLT or a trustee** to approve it first. Record who in **Approved by** and when.
- **Granted on** and **Granted by**, and any notes.

## Taking access away

When someone no longer needs it, or has left, open the record and use **Remove access** with the date and a **reason**. The record stays on the register, so it still shows they had it. For a leaver, the requester's page in People can **remove all access** in one go.

**Delete** is only for access recorded by mistake.

## Keeping it reviewed

Access should be reviewed every term. See [The termly access review](access-review).
