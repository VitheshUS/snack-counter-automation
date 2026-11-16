# Snack Counter frontend

Angular 22 standalone components, Router and HttpClient. Node.js 24.15 or newer within Node 24 is required. All commands use local project dependencies; no global Angular CLI is needed.

From the repository root, start the backend in one terminal:

```powershell
dotnet run --project backend/SnackCounter.Api --no-launch-profile --urls http://127.0.0.1:5187
```

In another terminal:

```powershell
cd frontend/snack-counter-ui
npm.cmd ci
npm.cmd start
```

Open http://localhost:4200. On shells without PowerShell's script restriction, `npm` works in place of `npm.cmd`.
`proxy.conf.cjs` forwards `/api/**` to port 5187 and strips `/api`. No backend CORS or business-rule changes are needed.
If changing the backend port, update the proxy and restart the frontend server.

```powershell
npm.cmd run build
npm.cmd test -- --watch=false
```

Production output is `dist/snack-counter-ui/browser`. A future production host must serve the SPA with route fallback and forward `/api` to the backend. This phase does not configure hosting or deployment.

## Routes

- `/dashboard`: select a demo employee, view their office, report low/empty snacks, see active refill status and real budget figures.
- `/volunteer`: private invitations, acceptance/decline, explicit offers, release, unnecessary-refill completion, purchase authorization/reconciliation, and explicit zero-spending cancellation.
- `/expenses`: completed purchases, automatic existing-claim discovery, receipt-reference claim creation, submission, status refresh and HR reconciliation.
- `/hr`: monthly native/consolidated spending, backend exchange-rate metadata, all-time contribution summary/history, and separate demo HR decisions.

Loading, empty, success and error states are included. HTTP 409 responses display the backend reason; volunteer actions refresh state after conflicts. The backend remains authoritative for concurrency and eligibility.

Amounts are entered as decimal strings and converted exactly into integer minor units with BigInt. The API's per-operation cap permits safe numeric JSON request values. Response parsing preserves unsafe JSON integers as strings, and money formatting uses BigInt rather than dividing floating-point values. .NET ticks are converted using integer arithmetic.

## Actual backend integration and limitations

The frontend uses the contracts in `Program.cs`, `Phase2BEndpoints.cs` and `PHASE2B.md`.
Purchases omit currency, so the UI resolves it from their original month's native spending report instead of guessing from today's policy.

Expenses loads existing claims using the read-only `/demo/employees/{employeeId}/reimbursements` endpoint. It filters by both claim and completed-purchase ownership and never creates or submits a claim during discovery. Status and receipt references reload when revisiting or refreshing the page. No receipt references are saved in browser storage. After a full browser refresh, select the appropriate demo employee again if needed; employee selection persists across Angular navigation, not across full application restarts.

The backend still has no HR-wide claim list. The HR simulation panel accepts a known claim ID from Expenses.

HR decisions require a known claim ID. Approval/rejection are allowed only after at least one backend-clock day; the UI cannot fast-forward that clock. Payment must be explicitly confirmed after approval. This is a real backend restriction, not an inactive demo button.

Invitations are dispatched by the existing backend worker every 30 seconds or following decline/expiry. The UI does not send extra invitations. Use the refresh controls to retrieve current status; there is no polling or push notification stream.

Employee selection simulates identity only. Production must authenticate employees and authorize access to their claims, receipts and invitations. HR reports, contribution history/leaderboards, and simulation controls require separate HR authorization. The HR navigation boundary is visual, not access control. Empty counters, declines and ignored invitations are acceptable and carry no UI punishment.

Frontend tests cover money precision, request contracts, error handling and the demo shell. No automated browser end-to-end suite is configured.
