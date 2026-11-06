---
title: Device agent
summary: The EduHelpdesk Agent on school PCs: installing it by hand or with Intune, how PCs add themselves to the asset register, linking a PC that matches an existing asset, the tray icon, updates, and what the Device tab shows.
area: Assets and inventory
kind: page
pages: /Devices, /Devices/Review
needs: DeviceAgent
keywords: agent device agent pc pcs laptops desktops install intune win32 intunewin enrol enrolment key specs inventory scan automatic auto discovery serial windows 11 tpm bitlocker last seen last signed in tray icon asset tag update download not reporting missing lost low disk space free space warning bell
order: 6
---
The **EduHelpdesk Agent** is a small Windows service for school PCs and laptops. Each PC reports its specs when it starts and once a day, and the asset register keeps itself up to date from them, so the only thing you ever need to type is the asset tag if you want a different one.

## Turning it on and installing it

1. Open **Assets → Device agent** and press **Turn on the device agent**. This makes the school's enrolment key.
2. Press **Download the agent** to get `EduHelpdesk.Agent.exe`.
3. Open **Show the install command** and copy it.
4. On a PC, open a command prompt as an administrator, go to the folder with `EduHelpdesk.Agent.exe` in it, and run the command. To install on many PCs at once, use Intune (below).

The agent copies itself to Program Files and runs as the **EduHelpdesk Agent** service. It reports about 30 seconds after it starts, then daily, and soon after someone different signs in. A laptop away from school keeps trying every 15 minutes until it's back.

## Installing with Intune

Add the agent as a **Windows app (Win32)**. It needs no other files.

1. Put `EduHelpdesk.Agent.exe` in a folder on its own, and package that folder with Microsoft's **Win32 Content Prep Tool** (`IntuneWinAppUtil.exe`), with `EduHelpdesk.Agent.exe` as the setup file.
2. In Intune, go to **Apps → Windows → Add → Windows app (Win32)** and upload the `.intunewin` file.
3. For the **Install command**, paste the install command from this page as it is. For the **Uninstall command**, use `"C:\Program Files\EduHelpdesk Agent\EduHelpdesk.Agent.exe" uninstall`.
4. Set **Install behavior** to **System**.
5. Add a **Detection rule**: *File*, path `C:\Program Files\EduHelpdesk Agent`, file `EduHelpdesk.Agent.exe`, *File or folder exists*.
6. Assign it to the device groups you want.

The install command holds the enrolment key, and Intune shows it to anyone who can read the app's settings. If you make a new key, update the install command in Intune too.

## The tray icon

Everyone who signs in to a PC with the agent sees a small teal **EH** icon by the clock. Clicking it shows the PC's **asset tag** and **computer name**, so staff can read them out when they contact IT. Right-clicking it gives options to copy either one. Until the PC has reported, the icon says the tag isn't known yet. If the PC is waiting to be linked, it says IT needs to confirm it. The icon appears within a minute of signing in, and comes back if someone closes it.

## Updates

Each EduHelpdesk release brings the matching agent, and the Device agent page shows which version this helpdesk hands out. A PC running an older version updates itself after its next report. It waits a random time of up to two hours first, so a school's PCs don't all download at once. It checks the download is exactly the file the helpdesk described before installing it, and it keeps its enrolment and asset. While a PC is waiting to update, the PC list shows "updating" beside its version. You don't need to change anything in Intune for updates.

The agent only connects over **HTTPS**. The install command includes the helpdesk certificate's fingerprint, so PCs trust a self-signed certificate without anyone installing it on them. If the certificate is renewed, run the new install command on the PCs again: they keep their assets.

The key in the command enrols any PC, so treat it like a password. **New enrolment key** stops the old command from enrolling more PCs. PCs already enrolled carry on.

## What happens when a PC reports in

- **A PC the register hasn't seen** gets a new asset with the next asset number (Settings → Asset, part & loan rules). Its make, model, type, serial number and operating system come from the PC. A make, model or type that isn't on your lists yet is added to them.
- **A PC whose serial number is already on an asset** waits under **Waiting to be linked**. It might be that asset, or the serial on the asset might be wrong. See [Linking a waiting PC](device-agent#linking-a-waiting-pc) below.
- **A PC that's reinstalled** (Windows reset, or the agent removed and put back) is recognised by its serial number or its motherboard's ID and carries on into the same asset. "Agent reinstalled" goes in the asset's History.

After that, each report brings the asset up to date. The agent changes make, model, type, serial number or operating system only when the PC's own answer changes, so a model name you've tidied by hand stays tidied. A change to memory, disks, processor, BIOS, TPM, Secure Boot, BitLocker, antivirus, Windows 11 readiness, the computer name or what it's joined to goes in the asset's **History**, for example *Memory changed: 8 GB -> 16 GB*. Changes made by the agent are signed **EduHelpdesk Agent**.

## Linking a waiting PC

Press **Review and link** beside a waiting PC. The asset and the PC appear side by side: where the asset is and who holds it, and the PC's specs and who last signed in. Below them is each field the PC can fill in, with what the asset says and what the PC says.

- **Serial number** and **Operating system** start on the PC's value, because the machine knows best.
- **Make**, **Model** and **Type** start on the asset's value, because they're often typed the school's way. If the asset has nothing for one, it starts on the PC's.
- A field that's the same on both, or that the PC says nothing about, has no choice to make.

Flip any choice, then press **Link and keep these**. The PC reports into that asset from now on. Each change is written to the asset's History, along with "Linked to the device agent". Everything else on the asset stays as it is: its tag, status, location, purchase and warranty details, and who holds it.

If more than one asset has the same serial number, choose which one at the top first. If it isn't the same machine, press **Add as a new asset** instead. If the matched asset is disposed, the page warns you before you link.

## Needs attention

PCs with a problem are listed under **Needs attention** at the top of the Device agent page:

- **Not reporting.** It hasn't reported for 14 days. It might be lost, broken, put away in a cupboard, or the agent might have been removed. The PC list marks it **Not reporting** too.
- **Low disk space.** Its C: drive has less than 10% free.

To change either limit, open **Warnings** under Set-up. 0 switches that warning off. **Download CSV** gives you the list as a spreadsheet, for example to chase PCs round the school. A PC whose asset is disposed is never listed.

Once a day, from 7am, the bell rings for everyone with Device agent access if a PC is newly on the list. It rings once per PC per problem, so the end of a holiday is one ring, not one every morning. If a PC comes back, or frees some space, and later has the same problem again, the bell rings for it again.

## The Device tab

An asset with a PC reporting into it has a **Device** tab showing what the PC last said. It covers hardware, Windows and security, IP and MAC addresses, and who last signed in (linked to them in People when the name or user name matches). Who signed in is shown only: it never changes who the asset is assigned to.

## Removing a PC

**Remove** takes a PC off the Device agent page. Its asset stays. If the agent is still installed, the PC enrols again at its next report, so uninstall it first: run `"C:\Program Files\EduHelpdesk Agent\EduHelpdesk.Agent.exe" uninstall` as an administrator.

**Turn off** stops every PC reporting until it's turned back on. They stay enrolled.

## Permissions

The **Device agent** module in People → Roles gates this page. **Access** opens the list and **View** shows the Device tab on assets. **New** adds a waiting PC as a new asset (it also needs Assets: New). **Edit** opens the review and sets the agent up. Linking a PC from the review also needs Assets: Edit. **Delete** removes a PC. Only Administrators have it until you tick it on other roles.

## Looking into a PC that isn't reporting

On the PC, the agent keeps a log at `C:\ProgramData\EduHelpdesk Agent\agent.log`. To send a report now and see what happens, run `"C:\Program Files\EduHelpdesk Agent\EduHelpdesk.Agent.exe" report` as an administrator.
