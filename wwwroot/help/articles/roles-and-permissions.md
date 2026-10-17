---
title: Roles and permissions
summary: What each role can see and do - the five ticks per area, the extra ticks, and good roles to make.
area: People and roles
kind: page
pages: /People/Role, /People/DeleteRole
needs: Roles:Edit
keywords: role roles permissions access view new edit delete ticks administrator technician least privilege
order: 1
---
Every technician account has one **role**, and the role decides what that person can see and do. Roles are on the **Roles** tab of the **People** page; open one to change it, or add a new one.

## The five ticks per area

![The role editor](role-editor.png)

Each area (Tickets, Projects, Onboarding, Assets, Loan kits, Loans, Parts, Suppliers, Contracts, Access control, Requesters, Technician accounts, Roles, Reports, Settings, Audit log) has up to five ticks:

| Tick | Lets them |
| --- | --- |
| Access | Open the list and see what's there |
| View | Open a record and read it |
| New | Add one |
| Edit | Change one |
| Delete | Remove one |

**Every box is on its own** - ticking Delete doesn't tick Access, so a role can be given exactly what it needs. An area with nothing ticked is hidden from the menu entirely. Blank cells are actions that area doesn't have.

## Extra ticks

Some permissions aren't about records, and are plain on/off ticks, such as:

- **See all desks** - every desk's tickets, for leads such as the site manager.
- **See and add job costs** - the Costs tab on tickets.
- **Tell staff through EduNotify** - outage notices from tickets.
- **Record cleaning rounds** - for cleaning supervisors.
- **Assign projects** - the helpdesk-side backup to the project lead.
- **One per report**, and **Export and print reports**.
- **View raw database** - every table as stored, for troubleshooting.

## Good roles to make

Make roles for jobs, not people: *Technician*, *Site staff*, *Cleaning supervisor*, *Site manager* (with See all desks), *Business manager* (reports and costs, nothing else), *DPO* (audit log and raw database). A new member of staff then just needs the right role, and changing a role changes it for everyone who has it.

## Administrator

**Administrator** can do everything and can't be changed or deleted, so there's always a way back in. Keep the number of administrators small. You can't change your own role or switch off your own account; another administrator can.

A role still in use by an account can't be deleted.
