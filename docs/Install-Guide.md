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
- **Optional: HTTPS.** The installer can make a certificate for you (see *HTTPS* below), or use one you already have. Without HTTPS the helpdesk still works, but passwords cross the network unencrypted.

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
| Use HTTPS | yes if the computer has a certificate, otherwise no | Either pick a certificate already on the computer, or have the installer make a self-signed one. See *HTTPS*. |
| Name staff will type | this computer's name | Only asked when the installer makes the certificate. It must be exactly the name in the address bar, such as `helpdesk.school.org.uk`; several can be given, separated by commas. |
| Port | 80 (443 with HTTPS) | Leave it unless something else on the computer uses that port; the installer suggests another if so. With 80 or 443, staff don't need to type a port. |
| Open the firewall | yes | Lets other computers on the school network reach the helpdesk. |
| Public networks too | yes | Only asked if Windows has the computer's network down as **Public** (common on a network it doesn't recognise), because there it blocks other computers. Better still, make the network Private or join the domain - see *Troubleshooting*. |

Everything the school name, colours, logo and backups questions set can be changed later in **Settings**.

### What the installer does

- Copies the program to `C:\Program Files\EduHelpdesk`.
- Registers a Windows service called **EduHelpdesk**. It starts with Windows, restarts itself if it ever stops, and runs under its own limited account, `NT SERVICE\EduHelpdesk`, which can only change the data folder.
- If you asked for a self-signed certificate: makes it, tells the helpdesk computer to trust it, and writes the files other computers need into a `certificate` folder inside the data folder (see *HTTPS*).
- Opens the port in Windows Firewall for EduHelpdesk (domain and private networks, and public ones if you said so), and removes any rule Windows made earlier to block it.
- Starts the service and checks the site answers.
- Keeps a log of the install in `C:\ProgramData\EduHelpdesk`.

## Signing in the first time

1. Open the address the installer showed.
2. Sign in straight away as **admin@eduhelpdesk.local** with the password **ChangeMe123!**. You'll be asked to choose your own password, and then given a **recovery key**: download the recovery file and keep it safe, because it's how you reset the password if you forget it.
   > Until you do, anyone on the network who knows this default could sign in first - so do it as soon as the install finishes.
3. The helpdesk starts with a **worked example** - a few tickets, assets and people - so you can see how things fit together. When you're ready, **Settings → Go live & reset → Remove demo data** clears it.

### Then, in Settings

- **Branding & logo**: check the name, colours and logo.
- **Sign-in security**: turn on **two-step sign-in** for your own account (from your account menu), then require it for every staff account. The DfE's cyber security standard expects this.
- **Your recovery key**: keep the file you were given somewhere safe (or make a new one from **Recovery key** in your account menu). If you forget your password, it lets you reset it from the sign-in page. For the built-in administrator, it's the way back in without another administrator.
- **People**: add your technicians (Staff accounts) and give them roles, and import staff who will use the portal (Requesters, from a CSV). Each account you add gets a temporary password and a one-page **quick start guide** to print and hand over; an import prints a guide for everyone in it.
- **Sign-in security → Helpdesk address**: check it's the address staff will type. It's printed on every quick start guide.
- **Backups & data**: after the first night, check the backup ran.
- The **Technical Guide** (`docs\Technical-Guide.md` in the zip) explains every setting, and the **Staff Guide** (`docs\Staff-Guide.md`) is written for the staff who use the portal.

## HTTPS

HTTPS encrypts everything between the browser and the helpdesk, passwords included, and the DfE's cyber security standard expects it. It needs a **certificate**, and there are two ways to get one.

**A certificate from your school's own authority, or one you've bought.** Install it in **Local Computer → Personal** on the helpdesk computer, with its private key, for exactly the name staff will type. Computers that already trust your authority need nothing more. Your IT provider, your trust or your local authority can usually issue one.

**A self-signed certificate the installer makes.** It encrypts exactly as well. What it can't do is vouch for itself: until a computer has been told to trust it, its browser shows a full-page warning ("your connection isn't private") every time, and desktop notifications from the helpdesk may be blocked. So making one is only half the job; the other half is telling the school's computers to trust it, once.

### Making and trusting a self-signed certificate

1. **During a new install**, answer **yes** to HTTPS and choose **Make a new self-signed certificate** (or say yes when the computer has none). Give the name staff will type. The installer makes the certificate, trusts it on the helpdesk computer, and writes a `certificate` folder inside the data folder.
2. **On an install that is already running on plain HTTP**, double-click **`Enable-Https.cmd`** from the unzipped installer folder instead. It asks the same questions, switches the helpdesk over, restarts the service and checks it answers. If the check fails it puts the old settings back. Your data isn't touched; the old `http://` address stops working, so staff need the new one.
3. **Open the `certificate` folder.** It holds:
   - `EduHelpdesk-<name>.cer`: the public certificate. It's safe to copy anywhere; it can't be used to read anyone's traffic.
   - `Trust-This-Certificate.cmd`: double-click it on one computer and say yes to administrator rights, and that computer trusts the certificate.
   - `READ-ME-FIRST.txt`: the same steps in plain words, with the address, names, expiry date and fingerprint.
4. **Trust it on every computer.** On a Windows domain, use Group Policy: *Computer Configuration → Policies → Windows Settings → Security Settings → Public Key Policies → Trusted Root Certification Authorities → Import* the `.cer`, then `gpupdate /force`. With Intune, use a *Trusted certificate* profile. Edge and Chrome follow Windows' list. **Firefox** has its own: set the policy `security.enterprise_roots.enabled` to true. Phones and tablets need the `.cer` installed too.
5. **Check it:** on another computer, open the address. A padlock and no warning means it works.

Things to know:

- **The name has to match.** The certificate is made for the names you gave, plus the computer's own name and IP addresses. Typing any other name, or an address that has since changed, shows the warning again.
- **It lasts five years.** Before it runs out, run `Enable-Https.cmd` again, choose to make a new certificate, and trust the new `.cer` the same way.
- **It doesn't touch your data or accounts.** Only the helpdesk's address and certificate change.
- Then set **Settings → Sign-in security → Helpdesk address** to the new address, so the quick start guides print it.

### Using a certificate you already have

- **Before you install:** answer **yes** to HTTPS and pick it from the list. The installer does the rest, including letting the service read the certificate's private key.
- **On a running install:** run `Enable-Https.cmd` and pick it from the list.
- A certificate that no authority vouches for (a self-signed one you made yourself, say) is loaded too, and the same advice applies: computers must be told to trust it.

### By hand

If you'd rather edit the settings yourself, change `C:\Program Files\EduHelpdesk\appsettings.Production.json` to the address, the certificate and `RequireHttps`:

```
{
  "EduHelpdesk": { "DataPath": "C:\\EduHelpdesk\\Data", "RequireHttps": true },
  "Kestrel": { "Endpoints": { "Main": {
    "Url": "https://*:443",
    "Certificate": { "Subject": "helpdesk.school.org.uk", "Store": "My", "Location": "LocalMachine" }
  } } }
}
```

Add `"AllowInvalid": true` to the certificate if it is self-signed or the authority behind it isn't trusted on the helpdesk computer itself; without it the service refuses to start. Then, in `certlm.msc`, right-click the certificate → **All Tasks → Manage Private Keys**, add `NT SERVICE\EduHelpdesk` with **Read**, open the port in the firewall, and restart the **EduHelpdesk** service (Services, or `Restart-Service EduHelpdesk`).
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
| `UseHttps` | `true` for HTTPS. |
| `CertificateThumbprint` | With `UseHttps`: the thumbprint of a certificate in Local Computer → Personal to use. |
| `CreateCertificate`, `CertificateName` | With `UseHttps` and no thumbprint: `true` to have the installer make a self-signed certificate, for this name (several can be separated by commas; defaults to the computer's name). The files to trust it are written to the `certificate` folder in the data folder. |
| `Port`, `OpenFirewall` | As the questions above. |
| `AllowOnPublicNetwork` | `true` to open the port on Public networks too (only used if the computer is on one). |

Anything left out takes the suggested value. A wrong answer stops the install with a message rather than guessing.

## Moving to another computer

- **With the old one still running:** make a backup in **Settings → Backups & data**. Stop the EduHelpdesk service on the old computer. Copy its data folder to the new computer. Install there, and choose that folder when asked - the installer offers to use the database it finds.
- **Or:** unzip on the new computer and run `Install.cmd -ImportFrom "\\oldpc\c$\EduHelpdesk\Data"` from an administrator command prompt; it copies the data across into the new data folder and leaves the original alone.

Users sign in again afterwards, and everything else carries over.

## Uninstalling

Double-click `Uninstall.cmd` (from any copy of the zip). It removes the service, the firewall rule and the program folder, and offers to remove the self-signed certificate if the installer made one. **The data folder is kept** - reinstall and choose it to carry on, or delete it yourself once you are sure. It holds tickets and personal data, so dispose of it as your data protection policy says.

## Where things are

| What | Where |
|---|---|
| Program | `C:\Program Files\EduHelpdesk` (the previous version, after an upgrade, in `EduHelpdesk.previous`) |
| This install's settings | `C:\Program Files\EduHelpdesk\appsettings.Production.json` |
| Data | The folder you chose - `helpdesk.db`, `attachments`, `keys`, `logs` and more |
| The self-signed certificate's files (if the installer made one) | `certificate` in the data folder |
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

**The browser says the certificate isn't trusted, or doesn't match.** The certificate must be issued for exactly the name people type, by an authority that computer trusts. For a self-signed certificate, that computer hasn't been told to trust it yet: see *HTTPS → Making and trusting a self-signed certificate*. If the name in the address bar isn't one the certificate was made for, run `Enable-Https.cmd` again and give the right one.

**The service won't start after HTTPS, and the log says the certificate "could not be found".** The certificate has no authority behind it and the settings don't allow that. Run `Enable-Https.cmd` again, or add `"AllowInvalid": true` to the certificate in `appsettings.Production.json`.

**The default admin password doesn't work.** Someone has already changed it - which is what should happen. If they made a recovery key, **Forgotten your password?** on the sign-in page resets it. Otherwise another administrator can set a new password under **People → Staff accounts**.

## Licence

EduHelpdesk is made by Steven Davidson. It is free to use, copy, change and share under the MIT licence (`LICENSE.txt`), and is provided as it is, with no warranty and no promise of support.
