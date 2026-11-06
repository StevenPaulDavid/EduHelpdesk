---
title: Device agent
summary: The EduHelpdesk Agent on school PCs: installing it, how PCs add themselves to the asset register, linking a PC that matches an existing asset, and what the Device tab shows.
area: Assets and inventory
kind: page
pages: /Devices
needs: DeviceAgent
keywords: agent device agent pc pcs laptops desktops install intune enrol enrolment key specs inventory scan automatic auto discovery serial windows 11 tpm bitlocker last seen last signed in
order: 6
---
The **EduHelpdesk Agent** is a small Windows service for school PCs and laptops. Each PC reports its specs when it starts and once a day, and the asset register keeps itself up to date from them, so the only thing you ever need to type is the asset tag if you want a different one.

## Turning it on and installing it

1. Open **Assets → Device agent** and press **Turn on the device agent**. This makes the school's enrolment key.
2. Open **Show the install command** and copy it.
3. On a PC, open a command prompt as an administrator and run the command, with `EduHelpdesk.Agent.exe` in the same folder. To install on many PCs at once, push the same command with Intune.

The agent copies itself to Program Files and runs as the **EduHelpdesk Agent** service. It reports about 30 seconds after it starts, then daily, and soon after someone different signs in. A laptop away from school keeps trying every 15 minutes until it's back.

The agent only connects over **HTTPS**. The install command includes the helpdesk certificate's fingerprint, so PCs trust a self-signed certificate without anyone installing it on them. If the certificate is renewed, run the new install command on the PCs again: they keep their assets.

The key in the command enrols any PC, so treat it like a password. **New enrolment key** stops the old command from enrolling more PCs. PCs already enrolled carry on.

## What happens when a PC reports in

- **A PC the register hasn't seen** gets a new asset with the next asset number (Settings → Asset, part & loan rules). Its make, model, type, serial number and operating system come from the PC. A make, model or type that isn't on your lists yet is added to them.
- **A PC whose serial number is already on an asset** waits under **Waiting to be linked**. It might be that asset, or the serial on the asset might be wrong. Press **Link** if it's the same machine: blank make, model and type are filled in and the operating system is updated, and everything else stays as you typed it. Press **Add as a new asset** if it isn't the same machine.
- **A PC that's reinstalled** (Windows reset, or the agent removed and put back) is recognised by its serial number or its motherboard's ID and carries on into the same asset. "Agent reinstalled" goes in the asset's History.

After that, each report brings the asset up to date. The agent changes make, model, type, serial number or operating system only when the PC's own answer changes, so a model name you've tidied by hand stays tidied. A change to memory, disks, processor, BIOS, TPM, Secure Boot, BitLocker, antivirus, Windows 11 readiness, the computer name or what it's joined to goes in the asset's **History**, for example *Memory changed: 8 GB -> 16 GB*. Changes made by the agent are signed **EduHelpdesk Agent**.

## The Device tab

An asset with a PC reporting into it has a **Device** tab showing what the PC last said. It covers hardware, Windows and security, IP and MAC addresses, and who last signed in (linked to them in People when the name or user name matches). Who signed in is shown only: it never changes who the asset is assigned to.

## Removing a PC

**Remove** takes a PC off the Device agent page. Its asset stays. If the agent is still installed, the PC enrols again at its next report, so uninstall it first: run `"C:\Program Files\EduHelpdesk Agent\EduHelpdesk.Agent.exe" uninstall` as an administrator.

**Turn off** stops every PC reporting until it's turned back on. They stay enrolled.

## Permissions

The **Device agent** module in People → Roles gates this page. **Access** opens the list and **View** shows the Device tab on assets. **New** adds a waiting PC as a new asset (it also needs Assets: New). **Edit** links a waiting PC (it also needs Assets: Edit) and sets the agent up. **Delete** removes a PC. Only Administrators have it until you tick it on other roles.

## Looking into a PC that isn't reporting

On the PC, the agent keeps a log at `C:\ProgramData\EduHelpdesk Agent\agent.log`. To send a report now and see what happens, run `"C:\Program Files\EduHelpdesk Agent\EduHelpdesk.Agent.exe" report` as an administrator.
