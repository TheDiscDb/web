# Feature Spec: User Preferences (email opt-out + preferences framework)

## 1. Summary

Give users a way to **opt out of some or all emails**, and — because more communication is
coming — do it by introducing a small, reusable **user-preferences framework** rather than a
one-off email toggle. Email preferences are the framework's first consumer.

Concretely:
- A single typed `UserPreferences` row per user holds a **master email switch** plus
  **per-category** email flags.
- Both notification services consult a shared preference service before sending any
  **user-facing** email; admin-facing email is unaffected.
- Every email carries a **tokenized one-click unsubscribe** link (no login required), wired into
  a real `List-Unsubscribe` / `List-Unsubscribe-Post: One-Click` header — replacing today's
  `mailto:AdminEmail` placeholder.
- A logged-in **settings UI** on the existing `Account/Manage` page lets users manage every
  category directly.

The existing per-user **file-name templates** feature stays its own thing (separate table and
page); this spec does not fold it in, though a future Settings hub could co-locate the two.

## 2. Status

🔵 **Proposed** — no implementation started.

| Phase | Scope |
|-------|-------|
| **Phase 1** | `UserPreferences` table + migration; shared `IUserEmailPreferenceService` wired into **both** notification services (enforcement + real `List-Unsubscribe` header); tokenized one-click unsubscribe endpoint + landing page; email-preferences UI on `Account/Manage`. Categories: Contribution updates, Edit-suggestion updates, Direct messages, plus the master switch. |
| **Phase 2** | New categories (Announcements / marketing / digest); **auditing** of preference changes; optional Settings-hub unification and finer-grained (per-email-type) controls. |

## 3. Background: how email works today

All send decisions are **global configuration**, not per-user, and there is **no** general
preferences store.

### 3.1 Notification services
- **`ContributionNotificationService`** — has **no gate at all**. It always emails the user
  (whenever `IdentityUser.Email` is present) on contribution *submitted-confirmation*,
  *imported*, and admin *messages*, and emails the admin on new submissions / user replies.
- **`EditSuggestionNotificationService`** — gated by app-wide
  `EditSuggestionNotificationOptions { NotifyAdmins, NotifyUsers }` (config section
  `EditSuggestions:Notifications`; both default `false`, so the feature ships dormant). Still
  not per-user.

Both are Mailgun-backed (`MailgunClient` / `MailgunMessage`), render branded HTML from markdown,
and are best-effort (failures are logged and swallowed).

### 3.2 Unsubscribe today
Both services' `EnrichMessage` set `List-Unsubscribe` to `mailto:AdminEmail?subject=Unsubscribe`
— a manual placeholder with no real machinery behind it.

### 3.3 Identity & the settings precedent
- `TheDiscDbUser : IdentityUser` is **bare** — no profile or preference fields. Sign-in is GitHub
  OAuth, so `Email` may be `null` (every send already skips when it is).
- The only per-user settings precedent is **`UserFileNameTemplate`** (`UserId + ItemType +
  Template`), surfaced by the **WASM** `Pages/Settings/FileNameTemplates.razor` page via GraphQL.
  This stays separate.
- `Account/Manage` (`Components/Account/Index.razor`) is a **server-rendered** Identity-area page
  (currently just shows the username + logout).

## 4. User stories

- *As a contributor*, I can turn off contribution-status emails but keep getting admin messages.
- *As any user*, I can click **Unsubscribe** in an email and immediately stop that kind of email,
  without logging in.
- *As any user*, I can visit my account settings and toggle every email category, including a
  single "unsubscribe from everything" master switch.
- *As a user with a full inbox*, the browser/mail client's built-in one-click unsubscribe works,
  because we send a proper `List-Unsubscribe-Post` header.
- *As the site owner*, my admin notifications are never affected by a user's preferences.

## 5. Design

### 5.1 Storage — `UserPreferences`
One typed row per user (migration per new preference is acceptable):

| Column | Type | Notes |
|--------|------|-------|
| `UserId` | string (PK, FK → `AspNetUsers`) | 1:1 with the user |
| `EmailMasterEnabled` | bool, default `true` | Master switch; `false` ⇒ send the user nothing user-facing |
| `EmailContributionUpdates` | bool, default `true` | Contribution *submitted-confirmation* + *imported* |
| `EmailEditSuggestionUpdates` | bool, default `true` | Edit-suggestion *confirmation* + *resolved* |
| `EmailDirectMessages` | bool, default `true` | Admin→user messages (both services) |
| `UpdatedAt` | datetime | Last change |

*(Phase 2 adds `EmailAnnouncements`.)* A **missing** row means "all on" — rows are created lazily
on first change, so existing users need no backfill.

### 5.2 Shared preference service
`IUserEmailPreferenceService`, e.g.:
- `Task<bool> IsAllowedAsync(string userId, EmailCategory category)` — used by the notification
  services.
- get/update methods for the settings UI.

Injected into both notification services and the `Account/Manage` UI.

### 5.3 Effective-send rule
```
shouldSendToUser = existingGlobalGate(if any) && EmailMasterEnabled && <categoryFlag>
```
The per-user preference can only **further restrict** sending — it never re-enables a global gate
that is off (e.g. edit-suggestion `NotifyUsers = false` still wins). The contribution service,
which has no global gate today, becomes gated solely by the user preference.

### 5.4 Email → category map (user-facing sends only)

| Service | Email (notification-type tag) | Category |
|---------|-------------------------------|----------|
| Contribution | submitted-confirmation, imported | Contribution updates |
| Contribution | message-from-admin | Direct messages |
| Edit-suggestion | confirmation, resolved | Edit-suggestion updates |
| Edit-suggestion | message-from-admin | Direct messages |

Admin-facing tags (contribution *submitted*, *message-from-user*) are out of scope for user prefs.

### 5.5 Tokenized one-click unsubscribe
- Each email's `List-Unsubscribe` header (and an in-body link) points at a per-recipient,
  per-category URL, e.g. `/Account/Unsubscribe?token=…`, plus
  `List-Unsubscribe-Post: List-Unsubscribe=One-Click` (RFC 8058).
- The **token** is a signed, non-guessable value encoding `{ userId, category }` (Data Protection
  or a persisted token — see open questions). No login required.
- Server-side endpoints under the Account area:
  - `POST` — the RFC 8058 one-click action: flip that **category** off, return `200`.
  - `GET` — a **landing page** confirming the category unsubscribe, with links to
    **manage all preferences** and **unsubscribe from everything** (master off).

### 5.6 Settings UI
Add an **Email preferences** section to `Account/Manage` (`Components/Account/Index.razor`):
master switch + one checkbox per category, persisted through the preference service.
Because this page is **server-rendered** Identity UI in the `TheDiscDb` project (not the WASM
client), it uses direct server-side DbContext/service access — it does **not** reuse the WASM
GraphQL path that the naming-templates page uses.

## 6. Behavior & rules

- **Defaults**: opted-in for new and existing users; missing row ⇒ all categories on.
- **Nothing is mandatory**: with the master off, the user receives no user-facing email. (No
  account/security mail exists today, since auth is GitHub OAuth.)
- **Admin email is never gated by user prefs** — it stays governed by the existing global config.
- **Null-email users**: preferences are still storable but moot; existing null-email skips remain.
- **One-click scope**: a link only unsubscribes from **that email's category**, never everything,
  unless the user explicitly chooses "unsubscribe from everything" on the landing page.
- **Best-effort sends unchanged**: preference checks run before the Mailgun call; a failed check
  should fail *closed* only for the user send, never throw out of the notification path.

## 7. Risks / considerations

- **Project / render-mode split**: Account pages are server-rendered (`TheDiscDb`); naming
  templates are WASM (`TheDiscDb.Client`) via GraphQL. Keep email-prefs data access server-side;
  don't try to reuse the client GraphQL pattern here.
- **Token security**: unsubscribe tokens are typically long-lived (emails linger in inboxes). Use
  a signed, non-guessable token scoped to `{ userId, category }`. Misuse impact is limited to
  toggling a recipient's own email preferences.
- **Deliverability / compliance**: a real `List-Unsubscribe` + `List-Unsubscribe-Post` improves
  inbox placement and satisfies one-click / CAN-SPAM expectations far better than the current
  `mailto`.
- **Interaction with global gates**: document clearly that per-user prefs only restrict; a
  disabled global gate cannot be re-enabled by a user preference.

## 8. Open questions

- Which DbContext owns `AspNetUsers` and should host `UserPreferences` (the Identity /
  `ApplicationDbContext`)? Confirm at implementation.
- Token strategy: a Data-Protection signed token vs a persisted token column on `UserPreferences`.
- Also surface a "manage email preferences" link in the email **footer** (in addition to the
  header/one-click link)?
- Keep a single `EmailDirectMessages` category, or split admin-message vs future user-message?

## 9. Rollout / phases

- **Phase 1** — `UserPreferences` table + EF migration; `IUserEmailPreferenceService`; wire both
  notification services (enforcement + real `List-Unsubscribe` header); tokenized one-click
  unsubscribe endpoint (`GET` landing + `POST` one-click) + landing page; email-preferences UI on
  `Account/Manage`. Categories: Contribution updates, Edit-suggestion updates, Direct messages,
  plus the master switch.
- **Phase 2** — Announcements / marketing / digest category; **auditing** of preference changes;
  optional Settings-hub unification and per-email-type granularity if demand appears.

## 10. Future enhancements

- **Auditing** of preference changes: who, which preference, old→new value, timestamp, and source
  (settings page vs one-click token).
- **Announcements / newsletter** and **digest** (batched) emails as new categories.
- **In-app (non-email) notification** preferences; web push.
- **Per-individual-email-type** granularity beyond the category level.
- A unified **Settings hub** co-locating email preferences and file-name templates (navigation
  only; the two stores stay separate).
