# Making a release of EduHelpdesk

For you, as the developer: how to turn the project into the zip other schools install from, and how to move your own school onto it.

## What a release is

One zip, `releases\EduHelpdesk-<version>.zip`, with a `.sha256.txt` checksum beside it. Inside:

| In the zip | What it is |
|---|---|
| `Install.cmd`, `Install.ps1` | The installer: new installs and upgrades (`installer\` in the project) |
| `Uninstall.cmd`, `Uninstall.ps1` | Removes the service and program, keeps the data |
| `install-answers.example.json` | For unattended installs |
| `app\` | The published program, for 64-bit Windows, with the .NET runtime inside |
| `INSTALL.pdf`, `INSTALL.md` | The school install guide (`docs\Install-Guide.md`), with the version filled in |
| `docs\` | The Technical Guide and the Staff Guide |
| `README.txt`, `VERSION.txt`, `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` | What they say |

Nothing from your own install goes in it. The release script refuses to zip if it finds a database, sign-in keys, attachments, backups, logs or an install's own settings.

## Version numbers

Versions look like **1.0, 1.1, 2.0**.

- Most releases add one to the second number: new features and fixes, installed over the top with no steps for the school.
- A new first number (2.0) is for a change a school needs to know about before upgrading. Say what it is when you share the zip.

The version is kept in `EduHelpdesk.csproj` (`<Version>`), shown in **Settings → About EduHelpdesk**, and written into the zip's name and `VERSION.txt`.

## Making one

1. **Commit your work.** The script warns about uncommitted changes, because they would go into the release.
2. **Update the guides** if anything a school would notice has changed - especially `docs\Install-Guide.md`.
3. From PowerShell in the project folder (no administrator rights needed):
   ```powershell
   .\deploy\New-Release.ps1
   ```
   It asks for the version, suggesting the next one, then:
   - runs the tests, and stops if any fail
   - publishes the app for 64-bit Windows with the .NET runtime included
   - adds the installer, the licence and the guides, and makes `INSTALL.pdf`
   - checks nothing private is in it, then zips it and writes the checksum
   - offers to commit the version number and tag the release (`v1.1`) in git

   The first run takes a few minutes longer while .NET downloads the Windows runtime it bundles. Your running copy of the helpdesk isn't touched.

Options: `-Version 1.1` skips the question; `-Tag yes` or `-Tag no` answers the git question; `-AllowUncommitted` skips the warning.

## Checking it before you share it

- **Read `INSTALL.pdf`.**
- **Best: try it on a spare PC or a virtual machine.**
  - Unzip it there, run `Install.cmd`, answer the questions, and sign in at the address it shows.
  - Then make the next release and run its `Install.cmd` on the same machine, to see the upgrade.
- **Uninstall there afterwards** with `Uninstall.cmd`.

## Sharing it

Send the zip and its `.sha256.txt`. A school can check the zip arrived intact with:

```powershell
Get-FileHash EduHelpdesk-1.1.zip -Algorithm SHA256
```

The installer isn't signed, so Windows may say *"Windows protected your PC"*. The install guide tells schools to choose **More info → Run anyway**. Signing it would need a code-signing certificate, which costs money every year.

## Moving your own school onto it

Your helpdesk currently runs with `dotnet run` from the project folder, with its data in `App_Data` inside OneDrive. To move it onto the installed service:

1. Make a release, and unzip it somewhere outside OneDrive, such as `C:\Install\EduHelpdesk-1.0`.
2. Stop `dotnet run`. The installer won't copy a database that is still open.
3. From an administrator command prompt in the unzipped folder:
   ```
   Install.cmd -ImportFrom "C:\Users\<you>\OneDrive\Documents\05 - Projects\EduHelpdesk\App_Data"
   ```
   Choose a data folder outside OneDrive, such as `C:\EduHelpdesk\Data`. The installer copies everything across and leaves `App_Data` as it was. It skips the branding questions, because your data already has its own.
4. Sign in at the address it shows. Everyone signs in again, and everything else carries over.
5. Once you're happy, archive `App_Data`. From then on, `dotnet run` is for development, against a copy of the data.

To update your own install later: make a release and run its `Install.cmd`, as any school would.

## What a new school starts with

- The worked example: tickets, assets, people and a project. **Settings → Go live & reset** removes it.
- The built-in administrator, **admin@eduhelpdesk.local** with the password **ChangeMe123!**. They must choose their own at first sign-in.
- The school name, colours, logo and backup settings they gave the installer, applied on the first start (`Services\InstallSettings.cs`).
