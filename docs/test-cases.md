# Test strategy and verified results

Latest verified on 16 November 2026: **66 backend tests passed, 29 frontend tests passed, 95 total tests passed; zero failures.** Both backend and frontend production builds passed. Backend build reported zero warnings and errors. Counts include parameterized cases, not just test methods.

## Run the checks

From the repository root:

```sh
dotnet build SnackCounter.slnx
dotnet test SnackCounter.slnx
```

From the frontend directory:

```sh
cd frontend/snack-counter-ui
npm ci
npm run build
npm test -- --watch=false
```

Use `npm.cmd` in restricted Windows PowerShell. Stop the corresponding dev server first if the compiler/database binaries are locked; do not delete local data to fix a build lock. Angular uses its local CLI and Vitest, not a global CLI or browser test runner.

## Backend: real SQLite and an injected clock

Each fixture creates a temporary SQLite file. Operations use EF contexts/connections; the concurrent tests synchronize competing tasks against the same database. There are no mock repositories substituting for locking or uniqueness behavior. Clock-controlled tests advance time rather than waiting 15 minutes or a day.

| Test source | Covered behavior |
| --- | --- |
| [LifecycleTests.cs](../backend/SnackCounter.Tests/LifecycleTests.cs) | Concurrent accept/report winners; reservation release/reassignment; strict before/exact/after expiry including a boundary crossed after sweeping; concurrent purchase/sweep; Purchasing and NeedsReview protection; decline/silence eligibility; returning volunteers and preserved history; cooldown, notification cap and concurrent invitation retries; known-schema index upgrades |
| [BudgetTests.cs](../backend/SnackCounter.Tests/BudgetTests.cs) | Office/currency isolation; positive/valid estimates and owner checks; monthly exhaustion and rollback; cumulative four-period allowance and carry-forward; soft warnings; concurrent authorizations; estimate replacement and full overruns; zero-spending cancellation; original-month commitments; office-local month boundaries; policy snapshots and schema upgrades |
| [Phase2BTests.cs](../backend/SnackCounter.Tests/Phase2BTests.cs) | Claim eligibility/ownership and read-only discovery; duplicate/concurrent claims and submission; definite failure; timeout before/after provider creation; key-preserving retries, persistent recovery and reconciliation; separate approval/payment; rejection preserving expenditure; required receipt reference; native/consolidated reporting and historical rate consistency; amount-independent contribution awards/deduplication and schema upgrades |

Within the current one-active-counter-per-office domain, the competing-budget test uses simultaneous authorizations of the same active refill after earlier spending has reduced availability. It does not claim to simulate multiple independent counters in one office.

## Frontend: contracts, precision and UI state

Six spec files under [src/app](../frontend/snack-counter-ui/src/app) use Vitest, Angular TestBed and HTTP testing:

- `format.spec.ts`: exact decimal input parsing, both offices' monthly/released amounts, NOK/INR fractional and large values, unsafe-number rejection, lossless JSON integers, ticks, and specific 400/409/offline feedback.
- `api.service.spec.ts`: exact request methods/bodies/routes, no-content responses, ownership query, claim discovery and consolidated reporting.
- `app.spec.ts`: demo selector, office name and separate HR navigation.
- `expenses.spec.ts`: automatic claim state/receipt discovery, absent claims, failed-request recovery and discarding responses after identity changes.
- `session.service.spec.ts`: duplicate action suppression and clearing busy/error state after failure.
- `refill-data.service.spec.ts`: refreshed status when re-entering a route.

These are unit/component tests. Mocked HttpClient requests verify frontend contracts; they are not counted as live browser integration tests.

## Live HTTP and browser verification

The previous live integration review exercised all four routes, SPA navigation, a stale competing acceptance, budget advisory/reconciliation, claim discovery after reload, HR totals, contributions and 390px overflow checks. The screenshot demonstration independently ran the Bengaluru flow with Priya/Ravi: ₹1,000 estimate, ₹900 actual, PendingHR claim, ₹14,100 remaining and correct contribution points. It also checked duplicate reports, owner filtering and premature approval/payment rejection via HTTP.

The [demo guide](DEMO.md) provides the isolated setup, safe reset, real screenshots and capture command. [integration-review.mjs](../scripts/integration-review.mjs) and [capture-demo.mjs](../scripts/capture-demo.mjs) are opt-in scripts using an installed Chrome debugger and Node's fetch/WebSocket. They require their documented ports and **fresh disposable databases**; they are not automatically executed by CI. Repeating capture against populated data was verified to refuse changes rather than duplicate purchases/claims.

Live verification does not replace clock-controlled expiry or later-payment tests. The demo never changes the production clock or skips HR timing to manufacture a successful screenshot.

## CI status and limits

[ci.yml](../.github/workflows/ci.yml) defines independent .NET 10 and Node 24 jobs for pushes to `main` and PRs targeting `main`. Backend restores, builds Release and runs xUnit; frontend uses npm ci, builds and runs Vitest. Dependency caches are configured. **Hosted GitHub Actions success is not verified or claimed.**

This is exercise coverage, not a production certification: there is no real-provider contract suite, authenticated authorization suite, load test or always-on browser end-to-end job. Native expenses can change when older commitments reconcile; historical-rate tests guarantee the rate snapshot, not an immutable financial report.
