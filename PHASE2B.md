# Phase 2B demo

Run `dotnet run --project backend/SnackCounter.Api`. Existing refill and budget endpoints are unchanged.
All monetary amounts use integer minor units (100 per NOK/INR). All timestamps are UTC .NET ticks.

## Reimbursement endpoints

| Method | Route | Body / query |
| --- | --- | --- |
| GET | `/demo/employees/{employeeId}/purchases` | Find the employee's purchase IDs |
| POST | `/purchases/{purchaseId}/reimbursement` | `employeeId`, `description`, `receiptReference` |
| GET | `/reimbursements/{id}?employeeId=2` | View local claim status |
| POST | `/reimbursements/{id}/submit` | `employeeId`, optional `demoMode` |
| POST | `/reimbursements/{id}/reconcile` | `employeeId` |
| POST | `/demo/hr/reimbursements/{id}/decision` | `decision`: `Approved`, `Rejected`, or `Reimbursed` |

The claimant, amount and currency come from the completed purchase and its original budget month.
Cancelled, unfinished, zero-expense and unnecessary-refill tasks cannot create a claim.
Draft creation is idempotent: later calls return the original claim, including its original receipt reference and description.
References are opaque private identifiers; the application does not fetch URLs, read local paths or accept file uploads.
Use synthetic references for this demo. Keep the SQLite database private; it contains claim/receipt and employee data.

`demoMode` accepts `Success`, `DefiniteFailure`, `TimeoutAfterCreation`, or `TimeoutBeforeCreation`.
These are simulation controls, not production HR submission options.

States: `Draft -> SubmissionPending -> PendingHR | SubmissionFailed | SubmissionUncertain`.
Only Draft and SubmissionFailed can submit. Pending/Uncertain require reconciliation by the same internal key.
If mock HR finds a claim, reconciliation copies its state. If a pending/uncertain claim is absent, it becomes
SubmissionFailed and can retry using the same key. A process crash after persisting submission intent is handled the same way.
The mock provider also enforces unique submission keys, including across restarts.

HR normally takes 1–10 days. This demo permits manual approval/rejection after one injected-clock day;
it never approves automatically or assumes approval after ten days. Approval is not payment.
Only explicit `Reimbursed` payment confirmation after Approved marks a claim paid. Rejection is terminal.
The decision endpoint synchronizes local status; external changes can also be synchronized through reconciliation.
None of these operations alter expenses, budget reservations or contribution points.
Purchase overruns are copied into the claim and mock HR review flag; they are not automatically approved or rejected.

Real HR must support idempotent submission or reliable claim lookup to guarantee safe timeout recovery.
The mock uses the same SQLite database for persistence, but submissions occur outside the local claim-state transaction.
There is no background reimbursement retry/poller; callers explicitly submit and reconcile.

## Reports and contributions

| Method | Route | Result |
| --- | --- | --- |
| GET | `/reports/monthly/{yyyy-MM}` | Each office's native-currency actual spending and outstanding estimates |
| GET | `/reports/monthly/{yyyy-MM}/consolidated` | NOK totals, native totals and recorded exchange rate |
| GET | `/demo/hr/contributions` | Contribution event history |
| GET | `/demo/hr/contributions/leaderboard` | Points and report/inspection/refill counts per employee |

`Reporting:InrToNokNumerator` / `InrToNokDenominator` configure the simulated NOK-per-INR rate (default 12/100).
The first consolidated report snapshots the rate for that reporting month; later configuration changes do not
rewrite that month's rate. The response includes source/target currencies, reporting month and recording timestamp.
Conversion uses C# decimal arithmetic, rounded to the nearest NOK minor unit, ties to even, per native total.
No live currency service is used. Native currencies are never directly added together.
Reports use the purchase's authorization budget month, including later actual reconciliation.
Historical rates are stable; historical spending can change when an outstanding purchase is reconciled.

Task completion awards 5 refill points, or 2 inspection points for unnecessary/zero-expense completion,
and 1 point for the original confirmed report. Each event is unique per request, employee and kind.
Amounts spent, reimbursement outcomes, duplicate reports and submission retries do not increase points.
Declining, ignoring and cancelling earn no points and have no penalty. Rewards remain HR's decision.
Existing completed tasks are not backfilled with contribution events.

## Demo limitations

Employee IDs are trusted simulated identities. Production must authenticate employees, enforce claim/receipt ownership,
and restrict HR decisions, contribution history and leaderboards to authorized HR staff. Demo routes provide no real access control.
Receipt references are stored in plaintext SQLite; use appropriate access controls/encryption for production data.
Startup extends the existing schema idempotently without deleting financial data; this is not a general migration framework.
Local SQLite files are ignored and removed from Git tracking, but previously committed versions remain in Git history.
