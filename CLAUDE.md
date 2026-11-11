# LENSYNC — Billing and Payment System for Photography Studios in Aparri

Capstone/thesis project (BS Accounting Information System, Cagayan State University – Aparri).
Desktop billing & payment system for a photography studio: bookings → billing → payment → invoice → reports.
It will be evaluated by IT experts using **ISO/IEC 25010** (functionality, performance, compatibility,
usability, reliability, **security**, maintainability, portability) and by owners/staff using **UTAUT2**.
Quality, security and ease of use matter as much as features.

---

## Tech stack (do not change without asking)

- **Language/UI:** VB.NET, Windows Forms, **.NET Framework 4.8**, Visual Studio 2022
- **Database:** MySQL/MariaDB via **XAMPP**, database name **`lensync_db`** (utf8mb4 / utf8mb4_unicode_ci)
- **NuGet packages:** `MySql.Data` (DB), `BCrypt.Net-Next` (password hashing). Ask before adding any other package.
- **Charts:** `System.Windows.Forms.DataVisualization` (built into .NET Framework 4.8)
- **Printing:** `System.Drawing.Printing.PrintDocument` (custom drawn), `PrintPreviewDialog`
- **Email:** `System.Net.Mail.SmtpClient` (Gmail SMTP + App Password, settings stored in `settings` table)
- Timezone/currency: Asia/Manila, Philippine Peso (₱), format `₱#,##0.00`

## Build

- Build from the command line after every change and fix all errors/warnings before handing back:
  - Find MSBuild: `"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find MSBuild\**\Bin\MSBuild.exe`
  - Build: `MSBuild.exe LenSync.sln /p:Configuration=Debug /v:minimal`
- Restore NuGet packages with `MSBuild.exe LenSync.sln /t:Restore` (or `nuget restore`) when packages change.
- Claude cannot see the screen. After UI work, tell the user exactly what to click/check in the running app.
- This PC: repo is `C:\Users\DELL\source\repos\LenSync` (own git repo, branch `main`; the home folder is a different repo).
  `git` is not on PATH — use VS's bundled one:
  `"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"`.
- Packages: `MySql.Data 8.4.0`, `BCrypt.Net-Next 4.2.0` (SDK-style `LenSync.vbproj`, `net48`).
- This PC also has a `MySQL80` Windows service (start type Automatic, root has a password). When it runs it holds port
  3306, XAMPP's MariaDB can't start, and LENSYNC can't log in to the DB. Ask the user; don't stop services yourself.
- Test harness (C# net48 console referencing LenSync.exe, App.config -> throwaway `lensync_test`): run UI scripts inside
  `Application.Run` and wait with `await Task.Delay`. `Application.DoEvents` loops lose the WinForms sync context, so code
  after `Await` resumes on the thread pool and animation timers started there never tick (a false "stuck animation").
- Rebuild DB: `C:\xampp\mysql\bin\mysql.exe -u root --default-character-set=utf8mb4 -e "source database/schema.sql"` then the same with `seed.sql`.
- DPI: app is system-DPI aware (`app.manifest`); forms are built in code with `AutoScaleMode.None`, so wrap every
  code-set pixel size in `Theme.Dpi()`. Fonts are in points and scale on their own. Client PCs may run at 150%.
- UI API in place: `LsDialog`, `Toast`, `Validator` (`UI\Dialogs`), `LsButton` (`UI\Controls`, `Kind` = Primary/Secondary/Ghost),
  `Gfx` (rounded rects, viewfinder brackets, lens icons), `Tween`/`Easing` (`UI\Motion.vb`). Errors: `AppLog.Write(ex, context)`,
  user text from `Db.FriendlyMessage(ex)`. Validator: leave ~22px (96-dpi) under each field for the message line.
- Popups with fields: subclass `UI\Dialogs\LsFormDialog` (put controls in `Body`, implement `OnSubmitAsync`, show with
  `ShowCard(owner)`); all popups derive from `CardWindow`. Inputs: `LsTextBox` (set `FieldName` = label text for
  accessibility), `InlineNotice` for form-level messages, `PasswordChecklist` for new passwords.
- Run DB work off the UI thread with `Await Task.Run(...)` inside Async handlers; the forms run under ShowDialog /
  Application.Run, so continuations return to the UI thread.
- Gotcha: when an `Application.Run` loop ends, WinForms drops the thread's ThreadException handler and its
  WindowsFormsSynchronizationContext — `Program.PrepareUiThread()` re-attaches both before every screen. Keep it.
- Gotcha: VB runs a subclass's field initializers AFTER the base constructor, so a base class must not call
  overridable layout/paint code from its constructor (ModulePage.OnLayout waits for IsHandleCreated).
- Tabbed modules: subclass `Modules\TabbedView` (tab names + `CreatePage`); each tab is a `Modules\ModulePage`
  (title/description header, `AddAction`, `LayoutToolbar`, `RunAsync`; override `RefreshOnShow` to reload on every visit).
- Pickers: `LsSelect` (drop-down, optional right-aligned detail like a price), `LsDatePicker` (month calendar); both derive
  from `FieldShell` and open a `PopupHost` (ToolStripDropDown). Never use ComboBox/DateTimePicker. `LsGrid.SolidStatuses`
  picks which status pill is solid per list.
- In VB, `Dir`, `When` and `Now` are reserved/built-in names: don't use them as variable names.
- Receipts: `Printing\InvoicePrinter.Render` draws in points (`PageUnit = Point`); the same drawing feeds the on-screen
  preview (`RenderPreview`, draw it with an explicit pixel size, never DrawImageUnscaled) and the printer.
- Data changes that the Dashboard must see raise `AppEvents.RaiseSalesChanged()` (Phase 5 subscribes).
- Module views with unsaved edits implement `Modules.IGuardedView` (MainForm asks before switching/logging out).
- Session ends in Program after the shell is disposed; UI code that may outlive it uses `Session.CurrentUserId` (0 = none).

---

## Folder structure

```
C:\Projects\LenSync\
  CLAUDE.md
  LenSync.sln
  database\
    schema.sql          -- full CREATE TABLE script (idempotent: DROP IF EXISTS / CREATE)
    seed.sql            -- default admin, roles, permissions, sample packages, sample bookings
  docs\                 -- test cases, user manual, notes
  src\LenSync\
    App.config          -- connection string ONLY here (portability)
    Core\               -- Db.vb, Session.vb, Security.vb, Permissions.vb, AuditLog.vb, AppSettings.vb
    Data\               -- one repository per module: UserRepository.vb, BookingRepository.vb, ...
    Models\             -- plain classes: User.vb, Booking.vb, Transaction.vb, ...
    UI\
      Theme.vb          -- ALL colors, fonts, sizes live here
      Dialogs\          -- LsDialog.vb, Toast.vb, Validator.vb
      Controls\         -- reusable themed controls (LsButton, LsTextBox, StatTile, SidebarButton...)
    Forms\              -- LoginForm, ForgotPasswordForm, MainForm (shell)
    Modules\            -- UserControls loaded inside MainForm: DashboardView,
                           AddServiceView (incl. bookings), ReportsView, SettingsView, VerifyReportView
                           (ModulePage/TabbedView bases; Settings\*Page and AddService\*Page are their tabs)
    Printing\           -- InvoicePrinter.vb, ReportPrinter.vb
```

**Shell pattern:** `MainForm` = left sidebar + content panel. Each module is a `UserControl` swapped into
the content panel (with a short fade/slide). No separate windows for modules; dialogs only for popups.

---

## Business decisions (agreed defaults)

1. **Bookings (inside Add Service — no separate Bookings nav):** the Add Service module handles bookings
   (customer name, contact no., email, package, schedule date/time, downpayment, notes, status) as well as
   billing. Status: `Pending` → `Unpaid` (shoot done, awaiting billing) → `Paid` / `Cancelled`.
   Billing works on bookings with status `Unpaid`.
   Keep the `bookings` table generic so an external booking system could write to it later.
2. **Standalone system, integration-ready:** own users/roles; access is controlled per **module permission**.
3. **One studio per installation.** Business name, address, contact, logo, owner signature live in `settings`.
   No `studio_id` columns.
4. **Pricing:** admin maintains **packages** (name, inclusions, price) and **add-ons** (name, price) in Settings.
   Staff may add an "Other" line with a manual description and price.
   **Discount:** fixed amount or percent, reason required, written to audit log.
5. **Downpayment** is recorded on the booking. Add Service bills the **full remaining balance** (no installments).
   `Total = Σ items − discount`; `Balance due = Total − downpayment`; `Change = Tendered − Balance due`.
6. **Payment methods:** `Cash`, `GCash`, `Maya`. E-wallets require a **reference number** (no API integration —
   the thesis delimitation excludes online banking/credit-card integration). For e-wallets, tendered = balance, change = 0.
7. **Printing:** regular printer, short bond. Invoice = half letter (5.5" × 8.5"); reports = letter (8.5" × 11").
   User picks the printer via PrintDialog; the printer name is recorded.
   Invoice title: **"ACKNOWLEDGMENT RECEIPT"** with footer "This document is not valid as a BIR invoice."
   (the system is not BIR-registered).
8. **Roles** (permissions editable by Admin via checkboxes):
   - `Admin` — all modules
   - `Cashier` — Dashboard, Add Service
   - `Accounting` — Dashboard, Reports
   Modules (permission keys): `DASHBOARD`, `ADD_SERVICE` (bookings + billing), `REPORTS`, `SETTINGS`, `VOID_SALE`.
   (`BOOKINGS` was removed on 2026-11-10 when bookings moved into Add Service.)
9. **Forgot password:** username + registered email → 6-digit code emailed → verify → set new password.
   If offline, Admin resets the password in Settings.

---

## Database conventions

- Table/column names: **lowercase_snake_case**. Primary key `id INT AUTO_INCREMENT`. Engine InnoDB.
- Every table: `created_at DATETIME DEFAULT CURRENT_TIMESTAMP`; mutable tables also `updated_at`.
- Money: `DECIMAL(12,2)` in MySQL, **`Decimal`** in VB. Never `Double`/`Single` for money.
- Soft delete / deactivate (`is_active`), never hard-delete users, packages, bookings or transactions.
- Document numbers (generated in a DB transaction, unique index):
  `BK-2026-0001` (booking), `AR-2026-000001` (receipt/transaction), `RPT-2026-0001` (report).

### Tables (initial design)

| Table | Key columns |
|---|---|
| `roles` | id, name, description |
| `permissions` | id, perm_key, label |
| `role_permissions` | role_id, permission_id |
| `users` | id, username (unique), password_hash, full_name, email, role_id, is_active, failed_attempts, locked_until, last_login |
| `password_resets` | id, user_id, code_hash, expires_at, attempts, used_at |
| `packages` | id, name, inclusions, price, is_active |
| `addons` | id, name, price, is_active |
| `bookings` | id, booking_no, customer_name, contact_no, customer_email, package_id, schedule_at, downpayment, downpayment_method, downpayment_ref, status, notes, created_by |
| `transactions` | id, receipt_no, booking_id, subtotal, discount_type, discount_value, discount_amount, discount_reason, total, downpayment, balance_due, payment_method, reference_no, amount_tendered, change_amount, status (`Completed`/`Voided`), void_reason, voided_by, voided_at, cashier_id, printed_count |
| `transaction_items` | id, transaction_id, item_type (`Package`/`Addon`/`Other`), ref_id, description, qty, unit_price, line_total |
| `report_logs` | id, report_no, report_type, date_from, date_to, filters_json, row_count, total_amount, content_hash, verification_code, generated_by, printer_name, generated_at |
| `audit_logs` | id, user_id, action, entity, entity_id, details, created_at |
| `settings` | setting_key (PK), setting_value — business_name, address, contact, logo_path, signature_path, smtp_*, report_secret |
| `doc_sequences` | doc_type, seq_year (PK together), last_no — counters for BK/AR/RPT; use `Db.NextDocumentNo(conn, tx, prefix, digits)` inside the save transaction |

Added in Phase 1 beyond the initial design: `users.must_change_password` (seed admin = 1), `bookings.is_active` (soft delete),
CHECK constraints on statuses/methods/amounts. Status/method columns are `VARCHAR` + `CHECK` (not ENUM) to stay integration-friendly.

---

## Security rules (ISO 25010 security is graded)

- **Always parameterized queries** (`MySqlCommand.Parameters.AddWithValue`). Never concatenate user input into SQL.
- Passwords: **BCrypt** hash only. Never store, log, or display plain passwords.
- Login: generic error "Invalid username or password." Lock account 5 minutes after **5 failed attempts**.
- Reset codes: 6 digits, stored **hashed**, expire in **10 minutes**, max **5** attempts, single use.
- Every module checks `Session.HasPermission(key)`; sidebar hides modules the user can't access.
  Check permission again inside the action (not only in the UI).
- Audit log: login/logout, failed login, account create/edit/deactivate, password reset, discount, void, report print.
- Saving a sale = **one DB transaction** (transaction + items + booking status + audit). Roll back on any error.
- Report proof: `content_hash` = SHA-256 of the report data; `verification_code` = first 10 chars (Base32)
  of HMAC-SHA256(content_hash + report_no, `report_secret`). `report_secret` is generated once at first run.
- Catch exceptions at UI boundaries, show a friendly LsDialog, write details to a log file in `%AppData%\LenSync\logs`.

---

## UI / Theme rules — minimalist monochrome

- **No color accents.** Only black, white and grays defined in `UI\Theme.vb`:
  `Ink #111111` (sidebar, primary buttons) · `Charcoal #2B2B2B` · `Slate #6B6B6B` (secondary text) ·
  `Silver #A3A3A3` (borders/disabled) · `Mist #E5E5E5` (dividers) · `Snow #F5F5F5` (page bg) · `White #FFFFFF` (cards)
- Font: **Segoe UI** (body 10pt, labels 9pt, titles Segoe UI Semibold 16–20pt). One typeface only.
- Flat controls, generous padding, rounded corners (6–8px) drawn with GDI+, subtle shadows on cards/dialogs.
- Meaning must never depend on color: use icons, weight, text and motion.
- Never hard-code a color or font outside `Theme.vb`.
- Keyboard friendly: logical TabIndex, Enter = primary action (with confirm for money actions), Esc = cancel/close.

### Dialogs, toasts, validation — dynamic, never static

- **Never use `MessageBox.Show`.** Use the custom API:
  - `LsDialog.Success(msg[, title])` — check icon, auto-closes after ~2.5s (still closable)
  - `LsDialog.Error(msg[, title])` — X icon + short horizontal **shake**
  - `LsDialog.Warning(msg[, title])`, `LsDialog.Info(msg[, title])`
  - `LsDialog.Confirm(msg[, title], yesText, noText) As Boolean` — safe option focused by default
  - `Toast.Show(msg[, kind])` — non-blocking card that slides in bottom-right and fades out
  - `Validator.SetError(control, msg)` / `Validator.Clear(control)` / `Validator.ClearAll(container)` —
    thick black underline + small message under the field + small shake; clears as soon as the user edits.
- Animations: fade + slide-in on open, fade-out on close, dimmed overlay behind modal dialogs,
  hover/press states on buttons. Use a WinForms `Timer` (~15ms) with easing; keep animations < 250ms.
  Enable `DoubleBuffered` to prevent flicker.
- Prefer inline validation over popups. Use dialogs for confirmations, errors from the system, and results.

---

## Coding conventions

- `Option Strict On`, `Option Explicit On`, `Option Infer On` in every file / project settings.
- PascalCase for types/methods, camelCase for locals, `_camelCase` for private fields.
- Forms/UserControls contain UI logic only; SQL lives in `Data\*Repository.vb`.
- Use `Using` blocks for connections, commands and readers.
- **Avoid large hand edits to `*.Designer.vb`.** Keep designer files minimal; build complex/dynamic layouts in code
  (e.g., a `BuildLayout()` method called from the constructor). Never leave a designer file uncompilable.
- Small, focused files. Comment the *why*, not the *what*.
- Commit after each completed step: `git add -A && git commit -m "<phase>: <what>"`.

---

## Phases (update status as work progresses)

- [x] **Phase 1 – Foundation:** solution/project, App.config, Db, Theme, LsDialog/Toast/Validator, `schema.sql`, `seed.sql`
  (temporary `Forms\DemoForm.vb` test bench is the startup form until Phase 2 replaces it with LoginForm — delete it then)
- [x] **Phase 2 – Login & security:** LoginForm, BCrypt, lockout, Session, permissions, MainForm shell + sidebar, audit log, ForgotPasswordForm (SMTP)
  (Core\Auth.vb holds all sign-in/lockout/reset rules; Forms: LoginForm, MainForm, ChangePasswordDialog, ForgotPasswordDialog;
  modules show Modules\PlaceholderView until their phase — swap them in MainForm.CreateView. SMTP password is stored with
  AppSettings.SetSecret (DPAPI, LocalMachine) — Phase 3's email settings must use SetSecret, not SetValue.)
- [x] **Phase 3 – Settings:** user accounts (create/edit/deactivate/reset password, role), role permissions, packages, add-ons, business info, logo, signature, SMTP config
  (Modules\SettingsView + Modules\Settings\*Page; rules in Core\UserAdmin, PricingAdmin, StudioAdmin — each calls
  Session.Demand(SETTINGS), returns AdminResult with per-field errors, writes audit in the same transaction. Logo/signature
  files live in %ProgramData%\LenSync\branding (path in settings). Temporary passwords are shown once via TempPasswordDialog.)
- [x] **Phase 4 – Add Service (bookings + billing in one module):** bookings CRUD, unpaid list, billing computation, discount, payment method, save (DB transaction), invoice preview/print, void sale (admin)
  (Modules\AddServiceView = tabs Bookings / Billing / Sales in Modules\AddService\*Page; rules in Core\BookingAdmin and
  Core\Billing (pure math `Compute`/`ValidateBill`/`PaymentProblems` + `CompleteSale`, `VoidSale`, `RecordPrint`).
  CompleteSale re-prices package/add-on lines from the DB and refuses if the balance differs from what the cashier saw.
  Max 12 lines per bill (one half-letter receipt). Void returns the booking to Unpaid. "Shoot done now (walk-in)" saves a
  booking straight to Unpaid. Receipt prints are counted in transactions.printed_count; the printer name goes to the audit
  log (RECEIPT_PRINT); copies after the first print "REPRINT · COPY n". Dialogs: BookingDialog, BillingDialog,
  DiscountDialog, OtherItemDialog, ReasonDialog (cancel/void), ReceiptDialog.)
- [x] **Phase 5 – Dashboard:** today's services, today's sales, 7-day chart, recent transactions, auto-refresh after sale
  (Modules\DashboardView; data from Core\DashboardService.Load (DASHBOARD) via Data\DashboardRepository. "Sales" = totals
  of Completed receipts by issue date; voided never counted. Refreshes on AppEvents.SalesChanged and every 60 s; refresh
  errors after the first load show as a quiet status line, not a dialog. Tiles (UI\Controls\StatTile) and recent receipts
  click through to Add Service only for users with ADD_SERVICE (MainForm implements Modules.IModuleHost.OpenModule(key, tab)).
  Charts: UI\ChartStyle (monochrome DataVisualization column chart; reuse for Phase 6).)
- [x] **Phase 6 – Reports:** report type selector (daily sales, transactions, by payment method, by staff, by package), date range, preview/print with proof footer (generated by, printer, date/time, report no., verification code, owner signature), VerifyReportView
  Done and checked in the running app by the user. Agreed with the user:
  Verify lives only inside Reports (REPORTS permission); every print gets a NEW report no. + code (no reprints of an old
  number); "sales" = Completed receipts by issue date.
  Core: Models\Reports.vb, Data\ReportRepository.vb, Core\ReportService.vb (Build, ComputeHash, ComputeCode, Issue,
  History, Verify; audit REPORT_PRINT / REPORT_VERIFY), Printing\PrintKit.vb (shared print helpers; InvoicePrinter uses
  it; receipts verified pixel-identical to Phase 5), Printing\ReportPrinter.vb (letter pages, repeated table headers,
  signature, proof footer).
  UI: Modules\ReportsView (tabs) + Modules\Reports\CreateReportPage (type/period/From-To on the left, UI\Controls\
  PagePreview on the right; Print rebuilds the report first and refuses if the figures changed since the preview, then
  ChoosePrinter -> Issue -> Print), HistoryPage ("Open again" -> ReportsView.OpenAgain -> CreateReportPage.Apply),
  VerifyPage + UI\Controls\VerifyResultCard. LsDatePicker gained DateFormat/ShowIcon.
  PagePreview zoom (user request): "−  Fit width  +" pill top-right (middle toggles Fit width / Whole page), 25–200% of
  real paper size, Ctrl+wheel (anchored at the mouse), Ctrl+Plus/Minus/0; zoomed in = drag or Shift+wheel to move.
- [ ] **Phase 7 – Testing & handover:** test cases mapped to ISO 25010, fixes, installer, user manual
  Full regression (2026-11-11): 245 checks pass on a throwaway lensync_test (FS 107, SEC 94, REL 12, PERF 3, USA 29),
  incl. lockout, reset codes, role access, re-pricing, rollback mid-save (rename transaction_items), void, dashboard,
  reports, every tab and dialog rendered at 1032x594. Fixes from it: column widths at 1080p/150% (Sales receipt no. /
  cashier, Users name "(you)", Dashboard schedule name). The harness lives outside the repo (scratchpad) and writes
  results.csv with IDs per ISO area; open decisions: add it to the repo as tests\, installer tool, document format.

## Default seed login (development only)

- Username `admin` / password `Admin@123` — force change on first login.
