# Installing EduHelpdesk

Version {{VERSION}}

EduHelpdesk is an IT helpdesk for schools: tickets, a staff portal for reporting problems, an asset register with loans and kits, parts, purchasing projects, reports and an audit log. It runs on one Windows computer in school, and staff reach it in their web browser.

This guide is for whoever looks after IT at the school. Installing takes about ten minutes.

## What you need

- **A Windows computer that stays on**, 64-bit: Windows 10 or 11, or Windows Server 2016 or newer. A server is best; a desktop that is never switched off will do.
- **An administrator account** on that computer.
- **About 500 MB of disk** for the program, plus room for the data - a school's database and ticket attachments usually stay well under a few gigabytes.
- **Nothing to download.** Everything it needs, the .NET runtime included, is in the zip.
- **Somewhere for the nightly backups** - ideally another drive or a network share.
- **Optional: a certificate** for HTTPS (see *HTTPS* below). Without one the helpdesk still works, but passwords cross the network unencrypted.

## Installing

1. **Unzip the whole zip** to a folder on the computer, for example `C:\Install\EduHelpdesk-{{VERSION}}`. Don't run it from inside the zip.
2. **Double-click `Install.cmd`** and say **Yes** when Windows asks for administrator rights.
   If Windows shows *"Windows protected your PC"*, click **More info**, then **Run anyway** - the installer isn't signed with a certificate.
3. **Answer the questions.** Press Enter to take the suggestion in brackets.
4. When it finishes, it shows the **address** of the helpdesk, such as `http://helpdesk-pc/`. Staff use that address.

### The questions

| Question | Suggested | What it means |
|---|---|---|
| Data folder | `C:\EduHelpdesk\Data` | Where the database, attachments and sign-in keys live. Use a plain local folder on a drive that is backed up. Not OneDrive, Dropbox or similar - sync clients damage live databases, and the installer refuses them. If the folder already holds an EduHelpdesk database, it offers to use it. |
| School name | EduHelpdesk | Shown in the header, the staff portal and on printouts. |
| Main colour | `#067A78` | Buttons and headings, as `#RRGGBB`. Use your school's colour. |
| Accent colour | `#E8F0EF` | A pale shade used behind highlights. |
| Logo | (none) | The full path to a PNG of the crest or logo, under 2 MB. Can be added later in Settings. |
| Backup folder | the data folder's `backups` | Where the nightly backup goes. Another drive or a network share is better: a backup on the same drive doesn't survive that drive failing. |
| Backup hour | 2 | The hour (0-23) the backup runs, when nobody is using the helpdesk. |
| Use HTTPS | yes, if there's a certificate | Only asked when the computer has a certificate. See *HTTPS*. |
| Port | 80 (443 with HTTPS) | Leave it unless something else on the computer uses that port; the installer suggests another if so. With 80 or 443, staff don't need to type a port. |
| Open the firewall | yes | Lets other computers on the school network reach the helpdesk. |
| Public networks too | yes | Only asked if Windows has the computer's network down as **Public** (common on a network it doesn't recognise), because there it blocks other computers. Better still, make the network Private or join the domain - see *Troubleshooting*. |

Everything the school name, colours, logo and backups questions set can be changed later in **Settings**.

### What the installer does

- Copies the program to `C:\Program Files\EduHelpdesk`.
- Registers a Windows service called **EduHelpdesk**. It starts with Windows, restarts itself if it ever stops, and runs under its own limited account, `NT SERVICE\EduHelpdesk`, which can only change the data folder.
- Opens the port in Windows Firewall for EduHelpdesk (domain and private networks, and public ones if you said so), and removes any rule Windows made earlier to block it.
- Starts the service and checks the site answers.
- Keeps a log of the install in `C:\ProgramData\EduHelpdesk`.

## Signing in the first time

1. Open the address the installer showed.
2. Sign in straight away as **admin@eduhelpdesk.local** with the password **ChangeMe123!**. You'll be asked to choose your own password.
   > Until you do, anyone on the network who knows this default could sign in first - so do it as soon as the install finishes.
3. The helpdesk starts with a **worked example** - a few tickets, assets and people - so you can see how things fit together. When you're ready, **Settings → Go live & reset → Remove demo data** clears it.

### Then, in Settings

- **Branding & logo**: check the name, colours and logo.
- **Sign-in security**: turn on **two-step sign-in** for your own account (from your account menu), then require it for every staff account. The DfE's cyber security standard expects this.
- **People**: add your technicians (Staff accounts) and give them roles, and import staff who will use the portal (Requesters, from a CSV). Each account you add gets a temporary password and a one-page **quick start guide** to print and hand over.
- **Sign-in security → Helpdesk address**: check it's the address staff will type. It's printed on every quick start guide.
- **Backups & data**: after the first night, check the backup ran.
- The **Technical Guide** (`docs\Technical-Guide.md` in the zip) explains every setting, and the **Staff Guide** (`docs\Staff-Guide.md`) is written for the staff who use the portal.

## HTTPS

HTTPS encrypts everything between the browser and the helpdesk, passwords included. It needs a **certificate** for the name staff will type, installed in **Local Computer → Personal** on the helpdesk computer, with its private key. Your IT provider, your trust or your local authority can usually issue one from the school's own certificate authority.

- **If the certificate is there before you install:** answer **yes** to HTTPS and pick it from the list. The installer does the rest, including letting the service read the certificate's private key.
- **To add HTTPS later:** install the certificate, then edit `C:\Program Files\EduHelpdesk\appsettings.Production.json`. Change the address to `https://*:443`, add the certificate, and turn on `RequireHttps`:

```
{
  "EduHelpdesk": { "DataPath": "C:\\EduHelpdesk\\Data", "RequireHttps": true },
  "Kestrel": { "Endpoints": { "Main": {
    "Url": "https://*:443",
    "Certificate": { "Subject": "helpdesk.school.org.uk", "Store": "My", "Location": "LocalMachine" }
  } } }
}
```

Then, in `certlm.msc`, right-click the certificate → **All Tasks → Manage Private Keys**, add `NT SERVICE\EduHelpdesk` with **Read**, open port 443 in the firewall, and restart the **EduHelpdesk** service (Services, or `Restart-Service EduHelpdesk`).

## Backups and restoring

A backup is made every night at the hour you chose, and kept for 14 days (change both in **Settings → Backups & data**, which also has **Back up now**). Each backup is a zip holding the database, attachments, logo and sign-in keys, plus a `RESTORE.txt` with step-by-step instructions for putting it back.

A network share needs to let the **computer's own account** write to it - `SCHOOL\HELPDESK-PC$`, for a computer called HELPDESK-PC - because that's who the service is on the network.

## Updating to a new version

1. Unzip the new version's zip to its own folder.
2. Double-click its `Install.cmd`.

The installer sees EduHelpdesk is already installed and **upgrades it without asking anything**:

- It stops the service.
- It zips the data into the backups folder, named `before-upgrade-<old>-to-<new>`.
- It swaps in the new program, keeping your settings and data.
- It starts the service again.

If the new version doesn't start, it puts the previous one back and tells you. The version you're on is in **Settings → About EduHelpdesk**.

## Installing without questions

For a trust installing at several schools: copy `install-answers.example.json` to `install-answers.json`, fill it in, and run from an administrator command prompt in the unzipped folder:

```
Install.cmd -AnswersFile install-answers.json
```

| Answer | Meaning |
|---|---|
| `ProgramFolder` | Where the program goes (`C:\Program Files\EduHelpdesk` if left out). |
| `DataFolder` | The data folder. |
| `UseExistingData` | `true` to use a database already in that folder. |
| `SchoolName`, `PrimaryColor`, `AccentColor`, `LogoPath` | As the questions above. |
| `BackupFolder`, `BackupHour` | As the questions above. |
| `UseHttps`, `CertificateThumbprint` | For HTTPS: the thumbprint of the certificate in Local Computer → Personal. |
| `Port`, `OpenFirewall` | As the questions above. |
| `AllowOnPublicNetwork` | `true` to open the port on Public networks too (only used if the computer is on one). |

Anything left out takes the suggested value. A wrong answer stops the install with a message rather than guessing.

## Moving to another computer

- **With the old one still running:** make a backup in **Settings → Backups & data**. Stop the EduHelpdesk service on the old computer. Copy its data folder to the new computer. Install there, and choose that folder when asked - the installer offers to use the database it finds.
- **Or:** unzip on the new computer and run `Install.cmd -ImportFrom "\\oldpc\c$\EduHelpdesk\Data"` from an administrator command prompt; it copies the data across into the new data folder and leaves the original alone.

Users sign in again afterwards, and everything else carries over.

## Uninstalling

Double-click `Uninstall.cmd` (from any copy of the zip). It removes the service, the firewall rule and the program folder. **The data folder is kept** - reinstall and choose it to carry on, or delete it yourself once you are sure. It holds tickets and personal data, so dispose of it as your data protection policy says.

## Where things are

| What | Where |
|---|---|
| Program | `C:\Program Files\EduHelpdesk` (the previous version, after an upgrade, in `EduHelpdesk.previous`) |
| This install's settings | `C:\Program Files\EduHelpdesk\appsettings.Production.json` |
| Data | The folder you chose - `helpdesk.db`, `attachments`, `keys`, `logs` and more |
| Error log | `logs` in the data folder, and **Settings → Error log** |
| Install logs | `C:\ProgramData\EduHelpdesk` |
| The service | **EduHelpdesk** in Services (`services.msc`) |

## Troubleshooting

**The installer says to run it as administrator.** Right-click `Install.cmd` → **Run as administrator**, or sign in to Windows with an administrator account.

**"Something else on this computer is already using port 80."** Another web server (often IIS) has it. Choose another port, such as 5277; staff then type it in the address, for example `http://helpdesk-pc:5277/`.

**It works on the helpdesk computer but not from others.**
- **The network is Public.** Windows files a network it doesn't recognise as *Public* and blocks incoming connections there. Check with `Get-NetConnectionProfile` in PowerShell. Either make it Private (**Settings → Network & internet →** the network **→ Private**) or join the computer to the domain, or run `Install.cmd` again: run over an existing install it repairs the firewall and includes Public networks when the computer is on one.
- **The firewall wasn't opened.** Run `Install.cmd` again, or open the port yourself from an administrator PowerShell (use your port):
  ```
  New-NetFirewallRule -DisplayName "EduHelpdesk (80)" -Direction Inbound -Protocol TCP -LocalPort 80 -Program "C:\Program Files\EduHelpdesk\EduHelpdesk.exe" -Action Allow -Profile Domain,Private,Public
  ```
- **The name doesn't resolve.** The installer lists the computer's IP addresses at the end - try `http://<address>/` instead.
- Running `EduHelpdesk.exe` by hand while the service is stopped makes Windows ask to allow it through the firewall. There's no need: the service is what people use, and the installer's rule already covers the program.

**The service won't stay running.** Look in the data folder's `logs`, and in **Event Viewer → Windows Logs → Application** (source EduHelpdesk). A common cause after adding HTTPS is the service not being allowed to read the certificate's private key.

**The browser says the certificate isn't trusted, or doesn't match.** The certificate must be issued for exactly the name people type, by an authority school computers trust.

**The default admin password doesn't work.** Someone has already changed it - which is what should happen. Another administrator can set a new password under **People → Staff accounts**.

## Licence

EduHelpdesk is made by Steven Davidson. It is free to use, copy, change and share under the MIT licence (`LICENSE.txt`), and is provided as it is, with no warranty and no promise of support.
