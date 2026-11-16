# Final demonstration and screenshots

These are captures of the actual Angular application and ASP.NET Core API, using a separate disposable SQLite database. All employees and receipt references shown are fictional exercise data. No UI, accounting or approval rules were changed for the demonstration.

## Verified scenario

| Step | Actual result |
| --- | --- |
| Priya (employee 6) reports Bengaluru empty | One Open refill opportunity |
| Normal invitation dispatch | Private invitations to Anu (4) and Ravi (5) |
| Ravi accepts | Reserved; Anu's competing acceptance returns 409 |
| Ravi confirms ₹1,000 estimate | Purchasing; ₹1,000 outstanding, ₹14,000 available |
| Ravi records ₹900 actual expense | Completed; ₹900 spent, ₹0 outstanding, ₹14,100 available |
| Ravi creates and submits a fictional claim | One ₹900 INR claim, PendingHR |
| Expenses is refreshed and Ravi reselected | Same claim ID, receipt reference and pending state rediscovered without resubmission |
| HR report | Bengaluru ₹900 actual; default simulated conversion is 108 NOK; Ravi 5 points and Priya 1 |
| Oslo overview | Monthly limit/available budget 6 000,00 kr; released allowance 4 500,00 kr on capture date |

The receipt reference is `FICTIONAL-DEMO-BLR-SNACKS-001 (not a real receipt)`. It is an opaque synthetic reference, not an uploaded receipt. The claim description explicitly identifies the fictional demonstration.

The ₹1,000 estimate fits even the first released allocation period, so no weekly warning is expected. Screenshot 03 shows the genuine purchase commitment instead of inventing a warning. Approval and payment attempts made immediately after submission are rejected by the normal backend timing/state checks. The captured claim remains pending.

## Screenshots

Captured on 16 November 2026 at 1440 CSS pixels wide, using Chrome's actual PNG capture output. Full-page images retain the application navigation and real UI text; there is no DOM replacement, mock data rendering, image compositing or post-processing. All six images were visually inspected.

| File | Demonstrates |
| --- | --- |
| [01-bengaluru-overview-open.png](screenshots/01-bengaluru-overview-open.png) | Priya's overview with an active Open opportunity |
| [02-bengaluru-volunteer-reserved.png](screenshots/02-bengaluru-volunteer-reserved.png) | Ravi's accepted reservation and ₹1,000 estimate input |
| [03-bengaluru-purchase-commitment.png](screenshots/03-bengaluru-purchase-commitment.png) | Purchasing status, ₹1,000 commitment, ₹14,000 remaining |
| [04-bengaluru-expenses-pending-hr.png](screenshots/04-bengaluru-expenses-pending-hr.png) | Real persisted simulated claim rediscovered after full refresh |
| [05-hr-monthly-spending-contributions.png](screenshots/05-hr-monthly-spending-contributions.png) | Native/consolidated actual spending, rate metadata and contribution records |
| [06-oslo-overview-nok-budget.png](screenshots/06-oslo-overview-nok-budget.png) | Correct 6,000 NOK budget and 4,500 NOK released allowance |

## Repeatable isolated setup (Windows PowerShell)

Requirements: .NET 10, Node 24.15+ within Node 24, installed Chrome, and the repository's local npm dependencies. Existing servers on ports 5187/4200 can remain running; this setup uses **5189/4201**. Run commands from the repository root unless specified otherwise.

### Terminal 1: API with a NEW disposable database

```powershell
dotnet build SnackCounter.slnx
$demoDb = Join-Path ([IO.Path]::GetTempPath()) ('snack-screenshot-demo-' + [guid]::NewGuid().ToString() + '.db')
dotnet run --project backend/SnackCounter.Api --no-build --no-launch-profile --urls http://127.0.0.1:5189 -- "--ConnectionStrings:SnackCounter=Data Source=$demoDb;Pooling=False"
```

Keep `$demoDb` if you need to reopen this particular demonstration later. Do not point this command at a production database or the normal development database.

### Terminal 2: Angular with an isolated temporary proxy

```powershell
cd frontend/snack-counter-ui
# First setup only, with this frontend server stopped:
npm.cmd ci
$demoProxy = Join-Path ([IO.Path]::GetTempPath()) ('snack-demo-proxy-' + [guid]::NewGuid().ToString() + '.cjs')
[IO.File]::WriteAllText($demoProxy, "module.exports = { '/api/**': { target: 'http://127.0.0.1:5189', secure: false, changeOrigin: true, pathRewrite: { '^/api': '' } } };")
npm.cmd start -- --host 127.0.0.1 --port 4201 --proxy-config $demoProxy
```

Open http://127.0.0.1:4201 for a live demonstration. The regular application proxy configuration is unchanged.

### Terminal 3: real browser screenshot capture

```powershell
$demoProfile = Join-Path ([IO.Path]::GetTempPath()) ('snack-screenshot-browser-' + [guid]::NewGuid().ToString())
& "$env:ProgramFiles/Google/Chrome/Application/chrome.exe" --headless --disable-gpu --no-first-run --remote-debugging-port=9228 "--user-data-dir=$demoProfile" about:blank
node scripts/capture-demo.mjs --disposable-demo
```

Adjust only Chrome's executable location if installed elsewhere. The script uses Node's built-in fetch/WebSocket and Chrome DevTools; no browser packages or global Angular CLI are needed. It operates only against the fixed local demo port 4201, checks for the expected seed employees and refuses to run if any refill already exists. A successful run overwrites the six screenshots with fresh captures of the same scenario.

Do not run the older `integration-review.mjs` against this demo database: that script has its own broader scenario and expects different ports and a fresh database.

## Manual presentation / capture steps

Use the same isolated setup above, starting with a fresh database. Browser width: 1440px for consistent desktop screenshots.

1. Select **Priya · Bengaluru** on Overview. Click **Report low or empty snacks**. Capture screenshot 01.
2. Wait up to 30 seconds for the existing worker to dispatch invitations. Alternatively invoke the existing normal endpoint `POST /refills/{id}/invite` on the demo API. Neither approach assigns responsibility.
3. Select **Ravi · Bengaluru**, navigate to Volunteer, refresh private status if necessary, and click **Accept invitation**. Enter `1000.00` as the estimate. Capture screenshot 02 before confirming.
4. Click **Confirm intention to purchase**. Return to Overview to show Purchasing, ₹1,000 outstanding and ₹14,000 available. Capture screenshot 03.
5. Return to Volunteer, enter `900.00` as actual expense, and click **Complete purchase**.
6. Open Expenses. Enter the fictional description/reference above, click **Create claim**, then **Submit to simulated HR**. Refresh the browser, reselect Ravi (the selector defaults to Ada on full reload), and confirm the same pending claim returns. Capture screenshot 04.
7. Open HR workspace and select the current reporting month. Show ₹900 native expenditure, its configured consolidated NOK equivalent, Ravi's 5 points and Priya's 1 point. Capture the full page as screenshot 05.
8. Select **Ada · Oslo**, open Overview, and capture the correctly formatted 6,000 NOK limit as screenshot 06.

Do not approve/pay the claim merely to advance the presentation. Normal HR timing and payment confirmation remain separate.

## Safe reset and duplicate prevention

Stop only the demo API and Angular terminals with Ctrl+C. Close the dedicated headless Chrome instance/profile. Run Terminal 1 again with a **new GUID database filename**, then restart the demo frontend. Each capture run starts from a new database and therefore creates one refill/purchase/claim, not another copy in an old environment.

The capture script refuses a second run against the populated database before mutating data or replacing images. This was explicitly verified: one refill, one purchase and one claim remained after the refused repeat. No direct database editing, deletion or history rewriting is required to reset. Old temporary demo files may be left for operating-system cleanup; never delete the application's normal SQLite files as a reset shortcut.

SQLite databases, WAL/SHM files, local receipts, node_modules and build output remain ignored by Git. Only synthetic screenshots and reproduction source/documentation belong in the submission. Previously committed data, if any, is not removed from history by this procedure.

## Validation performed

- `dotnet build SnackCounter.slnx`: passed, zero warnings/errors.
- `dotnet test SnackCounter.slnx`: 66 passed, zero failed.
- `npm.cmd run build`: production build passed.
- `npm.cmd test -- --watch=false`: 29 passed, zero failed.
- Browser capture scenario: all six screenshots saved; no uncaught browser exceptions.
- HTTP smoke checks: duplicate report deduplication, competing acceptance rejection, exact estimate/actual accounting, read-only owner-filtered claim discovery, pending status, premature approval/payment rejection, native/consolidated reporting, contribution totals and Oslo minor-unit values all passed.
- GitHub Actions inspected: separate .NET 10 and Node 24 jobs actually execute backend/frontend tests after building. GitHub-hosted execution is not claimed without a push.

## Remaining limitations

Employee selection is demo identity simulation, not authorization. HR operations require real authorization in production. A full refresh resets the selector to Ada; reselect Ravi to view his claim. HR simulation still requires a known claim ID and at least one backend-clock day before approval/rejection. Receipt images are not uploaded. Month labels, timestamps and released allowances follow the real clock and will vary on future runs; no clock override was used for these captures.
