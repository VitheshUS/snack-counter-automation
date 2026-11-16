# Design: voluntary coordination with financial visibility

## 1. From an empty counter to reimbursement

An employee reports low or empty snacks in their office. Repeated reports return one active opportunity rather than creating competing tasks. Eligible volunteers receive optional invitations, or can explicitly offer to help. One employee can reserve a refill; reservation is consent to coordinate, not an obligation to spend money.

A pre-purchase reservation can be released, expire, or be completed after checking that no refill is needed. Purchasing requires an estimate, the correct office currency, an unexpired owned reservation and sufficient monthly funds. Successful authorization reserves the estimate and marks Purchasing. Actual completion replaces that estimate with the full expense; an overrun is recorded and flagged for HR review.

The purchaser can then create a reimbursement claim with a receipt reference. Submission and HR decisions form a separate lifecycle: pending is not approved, and approved is not paid. A timeout requires reconciliation with the mock provider, not a second logical claim. Rejection does not erase spending already incurred by the office.

## 2. Main components

The Angular employee screens show office status, private invitations, personal expenses and claim progress. A visually separate HR workspace shows spending and contributions. The Minimal API exposes those operations; Core services enforce lifecycle, money and claim rules. EF Core and SQLite persist the data and enforce uniqueness. A small background worker expires pre-purchase reservations and offers Open opportunities; chat and HR adapters simulate external systems.

## 3. Persisted data and its purpose

Offices and employees establish location and volunteer eligibility. Refill requests record the office, original reporter, state and outcome; assignment attempts retain invitations, responses and reservation/purchase timestamps independently. Monthly budgets snapshot policy and currency. Purchase commitments retain estimates, actuals, original budget month, cancellation and review flags.

Reimbursement claims retain receipt references, immutable submission details, status and an internal idempotency key; mock HR records model external persistence. Contribution events prevent duplicate point awards. Monthly reporting-rate snapshots make consolidated reports independent of later configuration changes. Nothing requires committing the database or real receipts to Git.

## 4. Addressing the business problems

**Empty counters:** Open opportunities may remain unresolved. Declines and silence have no penalty. Cooldowns and a total invitation cap prevent endless reminders; responsibility is never assigned without acceptance. Automation coordinates employees without obligating them to participate.

**Duplicate purchases:** A unique active opportunity per office and a unique active reservation per request prevent competing commitments. Transactions and conditional updates protect acceptance and purchase authorization. Once money may have been spent, time alone cannot reopen the task: Purchasing becomes NeedsReview while funds remain committed. Employees must still follow the reserve-before-buying process; software cannot stop unreported purchases outside it.

**Unequal participation:** Invitation selection prefers fewer completed contributions, then the oldest previous invitation. It does not increase contribution counts for declines or silence. Returning volunteers can explicitly offer help. Modest report/inspection/refill points recognize participation, never purchase amounts; rewards remain HR's decision.

**Delayed reimbursement and unclear spending:** Separate expense and claim states expose costs even while HR takes time. Actual expenses replace estimates, so they are not counted twice. Outstanding commitments reduce monthly availability. Four cumulative allowance periods carry unused allowance forward within the month; exceeding the released allowance warns, while exceeding the monthly authorization budget rejects. Native currency totals and an explicit simulated exchange rate keep consolidated reports meaningful.

## 5. Trade-offs and deliberate exclusions

One backend, one SQLite database and direct services keep the exercise explainable and testable. Real SQLite connections test concurrency rather than mocked locks. Integer minor units avoid floating-point money and SQLite decimal limitations. The UI refreshes on actions and route entry rather than introducing a real-time messaging system.

Authentication, real chat/HR integration, uploaded receipt files, deployment, reward allocation and additional infrastructure are deliberately excluded. Startup upgrades the known demo schema; it is not a general migration framework. Simulations demonstrate recovery contracts, not a guarantee about an unknown real HR provider.

## 6. Assumptions and limits

There is one counter per office; reports and purchases belong to that office. Both supported currencies use 100 minor units. Financial reconciliation remains in the authorization budget month, including across month boundaries. Policies are configurable, but existing monthly snapshots remain unchanged. Approval timing is simulated with a minimum one-day wait and explicit decisions, not automatic approval after ten days.

Demo employee IDs provide filtering, not security. Production needs employee/HR authorization and private data protection. Real HR must support idempotency or reliable claim lookup for safe timeout recovery. See [architecture](architecture.md) for evolution and [workflow](workflow.md) for precise transitions.
