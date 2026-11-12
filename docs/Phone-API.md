# Phone API (`/api/mobile/v1`)

What the EduHelpdesk app for Android calls. Code: `Services/PhoneEndpoints.cs`, `Services/PhoneAuthentication.cs`, `Services/HelpdeskStore.Phones.cs`. Help for schools: the *Phone app* article.

## Rules

- **HTTPS only.** Over plain http every call answers `403 { message }`.
- **Off until turned on** in Settings → Sign-in security, and only for roles with **Use the phone app** (`Tickets.PhoneApp`).
- **Tokens** (`ehp_…`) go in `Authorization: Bearer`. Only a SHA-256 of each is stored, in the `Phones` table. A token ends on sign-out or revoke, a password change, deactivation, *sign out everywhere*, or 90 days unused.
- **A token signs in as the technician**, with the same claims as the cookie. Every call asks the role's ticks and the desks the person sees, as the web pages do.
- **Errors** are `{ "message": "..." }` in words for the technician.
  - `401`: the phone must pair again.
  - `403` on a valid token: the app is off or the role lacks the tick. The phone stays paired.
  - `409`: an action refused (Assign to me, On my way, a reply, status, priority or close), with the ticket page's message.
- Times are UTC ISO 8601.

## Getting a token

| Call | Body | Answer |
|---|---|---|
| `GET /info` | | `{ schoolName, serverVersion, apiVersion, minAppVersion, latestAppVersion, phoneAppEnabled, features }` |
| `GET /app/android` | | the APK this server hands out (`application/vnd.android.package-archive`), or `404` |
| `POST /pair` | `{ code, deviceName }` | `{ token }` |
| `POST /sign-in` | `{ email, password, deviceName }` | `{ token }`, or `{ pendingId }` when two-step sign-in is on |
| `POST /sign-in/code` | `{ pendingId, code, deviceName }` | `{ token }` |

The pairing code comes from **Phones** in the account menu, which asks for the person's password first. The QR holds `eduhelpdesk://pair?u=<address>&c=<code>&fp=<sha256>`:

- `c` is a 32-character code that works once, for 5 minutes.
- `fp` is the SHA-256 of the server's certificate as lower-case hex, found from the Kestrel settings. The phone trusts that certificate even when it's self-signed. `fp` is missing behind a proxy that handles HTTPS itself.

Password sign-in shares the web's lockout counts and its wording. It refuses a temporary password, and it refuses an account without two-step sign-in when the school requires it. Those are sorted out on the web first.

## With a token

| Call | Needs | Answer |
|---|---|---|
| `POST /sign-out` | | `204`; this phone is revoked |
| `GET /me` | | `{ id, name, role, team, schoolName, serverAddress, serverVersion, defaultDeskId, priorities, appLockRequired, desks: [{ id, name, prefix, color, hours, alertDays, alertWindows }] }` |
| `GET /options?desk=` | Tickets: New | `{ categories: [{ name, deskId, icon }], priorities, locations, technicians: [person] }` |
| `GET /people?q=` | Tickets: New | `[person]`, up to 30 |
| `GET /tickets?view=&desk=&q=` | Tickets: Access | `{ tickets: [ticket], counts: { open, mine, unassigned, overdue, onboarding, all, today } }`, up to 200 |
| `GET /tickets/{number}` | Tickets: View | full ticket; also marks its alerts read, as opening it in a browser does |
| `GET /tickets/{number}/attachments/{id}` | Tickets: View | the file |
| `POST /tickets/{number}/assign-to-me` | Tickets: Edit | `{ ticket, message }` |
| `POST /tickets/{number}/on-my-way` | Tickets: Edit | `{ ticket, message }` |
| `POST /tickets/{number}/comments` (form) | Tickets: Edit | `{ ticket, message }` |
| `POST /tickets/{number}/status` | Tickets: Edit | `{ status }` → `{ ticket, message }` |
| `POST /tickets/{number}/priority` | Tickets: Edit | `{ priority }` → `{ ticket, message }` |
| `POST /tickets/{number}/close` | Tickets: Edit | `{ message }` (the closing message) → `{ ticket, message }` |
| `POST /tickets` (form) | Tickets: New | `201` full ticket |
| `GET /alerts` | | `{ unread, items: [alert] }` |
| `GET /alerts/poll?after=&wait=` | | `{ cursor, items: [alert] }`; waits up to 25 seconds. With staff notifications switched off (Settings → Notifications) this and `/alerts` answer `{ disabled: true }` at once, which the app shows rather than polling again straight away |
| `POST /alerts/read` | | `{ id }` or `{ all: true }` → `204` |

`view` is one of the web Tickets page's views: `open`, `mine`, `unassigned`, `overdue`, `onboarding`, `all` - or `today` (EduHelpdesk with the planner): the open tickets planned for this technician today, and theirs whose planned day has gone by without them being done, oldest day first, as on today's daily sheet. `desk` left out means every desk the person sees. The counts honour `q`; `counts.today` is only sent by a server that has the Today list, which is how the app knows to offer it.

Each ticket carries `plannedFor`: the day the team booked it in for on the planner (`yyyy-MM-dd`), or `null`. It is the helpdesk's own plan and never reaches the requester.

**`POST /tickets` form fields:**

- Always: `category`, `title`, `location`, `description`, up to 5 files as `photos`.
- A new ticket also takes `priority` and `requesterId`.
- `alreadyDone=true` logs a job already done instead, with `doneById` and `doneAt`. It's saved closed and nobody is notified, as *Log a job already done* on the web.

**`POST /tickets/{number}/comments` form fields:** `text`, `internal=true` for an internal note, `notifyRequester=true` to send the requester a portal pop-up (replies only, and only when they can see the ticket), `status` to move it in the same step (which can reopen a closed ticket), and up to 5 files as `photos`. Photos follow the comment: shared with the requester on a reply, internal on a note.

Reply, status, priority and close are the ticket page's own rules (`Services/HelpdeskStore.WorkTicket.cs`): the desk's statuses only, the checklist done before closing, and a closing message where Settings asks for one.

`features` in `/info` lists what this server adds to API version 1, so a newer app can hide what an older server lacks. `work-tickets` (3.2): reply, status, priority and close.

**Shapes:**

- **person**: `{ id, name, email, department, location }`.
- **ticket**: `{ number, ref, deskId, title, description, type, category, priority, status, statusTone, requester: person, createdAt, updatedAt, technicianId, technicianName, team, location, assetTag, dueAt, sla, closedAt, commentCount, attachmentCount, canAssignToMe, canSayOnMyWay, canEdit }`.
  - `statusTone` is `new`, `active`, `waiting` or `closed`.
  - The full ticket adds `comments: [{ author, text, at, internal, byRequester }]`, `history: [{ action, details, at }]` `attachments: [{ id, fileName, contentType }]`, `requesterSeesIt` (a reply can notify them), `closeNeedsMessage` and `statuses: [{ name, tone }]` (the ticket's desk's, for the status picker).
  - `canAssignToMe` and `canSayOnMyWay` use the same checks as the actions themselves (`HelpdeskStore.TakeRefusal`), so the app shows only buttons that will work.
- **alert**: `{ id, kind, title, at, unread, ticketNumber, deskId }`.
  - `kind` is `new-ticket`, `reply` or `assigned`.
  - `title` is the bell's own wording: the kind and the number, never the ticket's words.

Desk `color` and category `icon` are the staff portal's tile keys (`Services/PortalLook.cs`).

**Desk alert hours:** `alertDays` are ISO days (1 Monday to 7 Sunday) and `alertWindows` are `[{ start, end }]` as `HH:mm`: the desk's own working hours, or the school days from the first period's start to the last one's end. No windows means any time. The app uses them for *Only during my desk's hours*.

**The app's own download:** `deploy/New-Release.ps1` copies the newest `EduHelpdesk-Android-<version>.apk` from `EduHelpdeskApp\releases` into the program's `phone-app` folder. `latestAppVersion` is that file's version, or null when there is none. `appLockRequired` is *Require app lock on phones* in Settings → Sign-in security.
