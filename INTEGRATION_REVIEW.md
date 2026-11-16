# Final integration review

## Fixes

- Added read-only employee claim discovery at `GET /demo/employees/{employeeId}/reimbursements`. Both claim ownership and completed, positive, non-cancelled purchase ownership are checked. Missing claims return `[]`; no drafts or HR submissions are created by a read.
- Expenses now automatically loads claim state and receipt reference after refresh and navigation. Removed the employee-facing API limitation and manual discovery forms.
- Dashboard and volunteer views refresh their shared refill status on re-entry instead of retaining an old reservation indefinitely.
- Field-specific HTTP 400 errors are displayed when the backend supplies validation details. Existing 409 explanations, retry states and submission guards remain intact.
- Configured console logging for the interactive demo. On the restricted Windows account used for verification, the default Event Log provider threw while EF logged an expected duplicate-report uniqueness exception. This aborted requests and appeared as a proxy 502. Duplicate reports now return the existing opportunity through the normal backend path.

## Money investigation

The reported NOK conversion defect was **not reproducible in the inspected current source**. Before the fixes, `money()` already divided by `100n` for both currencies. No backend accounting or second conversion was added.

The live HTTP responses during this review were:

| Office | Monthly limit, minor units | Released, minor units | Correct UI limit | Correct released allowance |
| --- | ---: | ---: | --- | --- |
| Oslo | 600000 | 450000 | 6 000,00 kr | 4 500,00 kr |
| Bengaluru | 1500000 | 1125000 | ₹15,000.00 | ₹11,250.00 |

The comma in Norwegian output is a decimal separator; the two final digits are øre. A raw value of 600000 displayed as 600000 kr would indicate a missing conversion, but that behavior was not found in this checkout. The exact cause of an earlier screenshot cannot be established from the current code alone.

All display amounts pass through the shared money pipe. The formatter now explicitly fixes two fractional digits and rejects unsafe numeric integers instead of silently displaying rounded money. JSON parsing preserves large integers as strings, and BigInt preserves the minor-unit calculation. Added regression cases cover both offices' exact limit and allowance amounts, fractional values, large values and validation errors.

## Navigation and visual review

The sidebar belongs to the shared App shell, outside RouterOutlet. It remains present on every route. At desktop widths it is a sticky left column; at widths of 800px or less it becomes a top navigation area. At matching desktop viewports, the missing-sidebar issue was not reproduced. Original screenshots with different crops/viewports cannot establish a route-specific defect.

Browser checks verified desktop navigation and unchanged document time origin (no full-page reload), non-default employee selection across routes, the separate HR area, and all four routes at 390px without document overflow. The existing design, palette, type and layout were retained. Tables scroll within their own wrapper.

## Reproducible live verification

`scripts/integration-review.mjs` uses Node 24's built-in fetch/WebSocket and an installed Chrome debugger. No browser automation package is required. **Only run against a fresh disposable SQLite database**, since it creates synthetic purchases and claims. It refuses to run if refill requests already exist.

Example API startup in PowerShell, from the repository root:

```powershell
$reviewDb = Join-Path ([IO.Path]::GetTempPath()) ('snack-review-' + [guid]::NewGuid() + '.db')
dotnet run --project backend/SnackCounter.Api --no-launch-profile --urls http://127.0.0.1:5187 -- "--ConnectionStrings:SnackCounter=Data Source=$reviewDb;Pooling=False"
```

Start Angular in another terminal with `cd frontend/snack-counter-ui` and `npm.cmd start`.
Start the installed Chrome headlessly with a temporary profile and `--remote-debugging-port=9227`, then run:

```powershell
node scripts/integration-review.mjs
```

The browser scenario reports as Liv, dispatches private invitations through the existing invite endpoint, accepts as Ada, rejects Erik's stale acceptance, checks public assignee privacy, authorizes purchasing, completes actual spending, creates/submits a claim, refreshes Expenses to rediscover it and checks HR spending/points. A weekly advisory is verified before the fourth allocation period; after full release, no advisory is expected and warning behavior remains covered by clock-controlled tests.

Additional live HTTP checks cover duplicate reports, voluntary offers, budget rejection, missing-field 400 responses, explicit cancellation, unnecessary-refill completion, decline/reassignment, INR spending, definite HR submission failure, uncertain submission/reconciliation and rejection of premature approval/payment. Approval/payment are never forced for the demonstration.

Reservation expiry and later HR approval/payment are verified with the existing SQLite-backed injected-clock tests, rather than waiting 15 minutes or a day in the live browser. There is no production clock-override endpoint.

## Validation and CI

Final backend suite: 66 passed (63 existing plus 3 discovery tests). Frontend suite: 29 passed (14 existing plus 15 regression cases). Backend build has zero warnings/errors. Frontend production build and clean npm installation pass. The live browser/HTTP scenario passes.

CI has separate backend and frontend jobs: .NET 10 restore/Release build/xUnit tests, and Node 24 npm ci/production build/Vitest tests. Paths and commands match the repository. GitHub-hosted execution is not verified without a push. No commits, pushes or history rewriting were performed.

Tracked-file checks found no SQLite databases, receipt files, secrets.json, .env files, node_modules or bin/obj/dist artifacts. Existing ignore rules cover these files. Previously committed files, if any, remain in historical commits.

## Remaining demo limitations

Employee IDs are simulated identity inputs, not authentication. Production must authorize private purchase/claim/receipt access and HR routes. Selection persists between Angular routes; a full browser refresh resets the selector to its default. Claim discovery then follows the selected employee.

HR still has no global claim list and accepts a known claim ID for simulation. Decisions require the normal backend timing/state checks. Receipts remain references only; no image upload, real HR integration, rewards, deployment or cloud infrastructure was added.
