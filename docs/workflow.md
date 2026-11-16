# Implemented workflows

These diagrams describe the current services; refill state, invitation attempts, financial commitments and HR claims are separate records.

## Refill opportunity

```mermaid
stateDiagram-v2
    [*] --> Open: Valid low or empty report
    Open --> Reserved: Eligible volunteer accepts or offers
    Reserved --> Open: Release or 15-minute expiry
    Reserved --> Completed: Confirm no refill necessary
    Reserved --> Purchasing: Valid estimate and budget authorization
    Purchasing --> NeedsReview: Unfinished for 24 hours
    Purchasing --> Completed: Record actual expense
    NeedsReview --> Completed: Owner records actual expense
    Purchasing --> Open: Owner confirms zero-spending cancellation
    NeedsReview --> Open: Owner confirms zero-spending cancellation
    Completed --> [*]
```

Repeated reports return the active opportunity. After completion, a subsequent report creates a **new** request. Purchasing/NeedsReview never reopen through automatic expiry; only explicit zero-spending cancellation releases the financial commitment.

## Invitations, consent and reassignment

```mermaid
flowchart TD
    A[Open opportunity] --> B{Notification slots and retry budget available?}
    B -->|No| C[Leave opportunity Open]
    B -->|Yes| D[Select eligible volunteers by completed contributions and invitation history]
    D --> E[Persist Invited attempt and send private simulated message]
    E --> F{Volunteer response}
    F -->|Decline| G[Declined attempt; no penalty]
    F -->|No response for 30 minutes| H[Ignored attempt; no penalty]
    F -->|Accept| I{Request Open and acceptance eligible?}
    I -->|No| J[Reject acceptance conflict]
    I -->|Yes| K[Reserved attempt; one winner]
    K -->|Release or pre-purchase expiry| L[Released or Expired attempt; request Open]
    G --> A
    H --> A
    L --> A
    C --> M[Eligible employee may explicitly offer help]
    M --> I
```

The worker checks Open requests every 30 seconds; decline and reservation expiry also trigger replacement offers. There are normally at most two active invitations, a one-hour cooldown and six total notification attempts per refill; cooldown/cap are configurable. Explicit offers require no new notification and allow a previously declining/ignoring volunteer to return. Repeated notifications stop at the cap; the request may remain Open.

## Purchase authorization and reconciliation

```mermaid
flowchart TD
    A[Reserved owner supplies estimate and currency] --> B{Owned reservation unexpired at conditional update?}
    B -->|No| X[Reject; no commitment or state change]
    B -->|Yes| C{Positive supported amount and matching currency?}
    C -->|No| X
    C -->|Yes| D{Estimate fits limit minus actuals minus outstanding estimates?}
    D -->|No| X
    D -->|Yes| E[Atomically record commitment and mark Purchasing]
    E --> F{Total authorization exceeds released allowance?}
    F -->|Yes| G[Return advisory; purchase remains authorized]
    F -->|No| H[Purchase authorized without advisory]
    G --> I[Owner records actual expense]
    H --> I
    I --> J[Replace reserved estimate with full actual expense]
    J --> K{Actual exceeds estimate?}
    K -->|Yes| L[Persist overrun and HR-review flag]
    K -->|No| M[Release unused estimate if any]
    L --> N[Completed; original office and budget month retained]
    M --> N
```

Allowance periods are days 1–7, 8–14, 15–21 and 22 onward in the configured office timezone. A month change during authorization rejects with a retry message. Explicit cancellation with zero spending reopens the refill; ordinary pre-purchase release has no budget effect. Actual overruns may leave negative monthly availability: incurred spending is recorded rather than discarded.

## Reimbursement claim

```mermaid
stateDiagram-v2
    [*] --> Draft: Completed positive purchase and receipt reference
    Draft --> SubmissionPending: Persist submission intent
    SubmissionFailed --> SubmissionPending: Retry with same key
    SubmissionPending --> PendingHR: Mock HR accepts
    SubmissionPending --> SubmissionFailed: Definite failure
    SubmissionPending --> SubmissionUncertain: Timeout or uncertain error
    SubmissionPending --> PendingHR: Reconcile found claim
    SubmissionUncertain --> PendingHR: Reconcile found claim
    SubmissionPending --> SubmissionFailed: Reconcile found no claim
    SubmissionUncertain --> SubmissionFailed: Reconcile found no claim
    PendingHR --> Approved: Explicit HR approval after minimum wait
    PendingHR --> Rejected: Explicit HR rejection after minimum wait
    Approved --> Reimbursed: Explicit payment confirmation
```

Reconciliation copies the provider's current state, including Approved, Rejected or Reimbursed if it advanced externally. There is one logical claim per completed purchase and a unique provider key. Pending/uncertain submissions cannot blindly resubmit. Reads/discovery do not create claims or contact HR. Rejected claims are terminal; approval never implies payment. HR decisions never alter office spending. [Detailed contracts and simulation modes](../PHASE2B.md).
