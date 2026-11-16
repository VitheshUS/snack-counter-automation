# AI assistance disclosure

This project was developed with ChatGPT/Codex assistance. AI supported requirements analysis, design discussion, implementation, test generation, integration investigation, browser verification and documentation. The disclosure below is limited to the development conversation and artifacts available in this workspace; it does not invent a separate prompt history or claim independent human sign-off.

## Human direction and review focus

The human supplied the business context, stack, phased scope and acceptance criteria. Those instructions established voluntary participation, no penalties for decline/silence, fair opportunities, private assignment identities, office-specific budgets, soft weekly allowance and separate expense/reimbursement lifecycles. Later human review requests specifically raised expiry concurrency, returning volunteers, reimbursement discovery, currency display and application usability/navigation.

These requirements and review questions guided changes. The available history does **not** establish that a human independently inspected every implementation line or manually reproduced every automated scenario. That review remains important before treating the submission as approved or deploying anything.

## Concise development chronology

1. **Phase 1:** Codex implemented the .NET solution, seeded SQLite model, refill/invitation lifecycle, simulated chat and database-backed tests. Subsequent requested fixes added atomic expiry checks, bounded reassignment and repeat participation.
2. **Phase 2A:** Office policies, integer minor-unit budget accounting, cumulative allowance, commitments, actual reconciliation and cancellation were implemented and tested.
3. **Phase 2B:** Persistent mock HR claims, idempotent submission/reconciliation, reporting-rate snapshots and unique contribution events were added. Approval and payment remained distinct.
4. **Phase 3:** Angular screens were integrated against inspected backend contracts, including precision-safe money display and useful conflict feedback. Frontend build/tests and live proxy/browser checks validated the integration.
5. **Final integration review:** The human requested investigation of NOK formatting, claim rediscovery and navigation. Codex added read-only claim discovery, route-entry status refresh, validation feedback and a console-logging fix for restricted Windows environments, with regression tests.
6. **Demonstration and documentation:** A fresh disposable database supplied the actual Bengaluru ₹1,000/₹900 scenario and six browser screenshots. Capture duplication guards, build/tests and HTTP/browser assertions were used as evidence; no fake visualizations or immediate HR approvals were substituted.

## NOK formatting report and stabilization

An earlier display was reported as `600000 kr` instead of 6,000 NOK. Inspection of the current code found the shared formatter already dividing by 100, and live output showed `6 000,00 kr`. The exact cause of the earlier observation could not be established from this checkout. It would be inaccurate to claim that a missing division was found and repaired in that review.

The corrective work therefore stabilized and verified the boundary: explicit two-decimal formatting, rejection of unsafe numeric integers, and regression cases for both offices' limits/allowances and large/fractional amounts. Browser screenshots confirmed correct NOK output. No second conversion or backend-accounting adjustment was introduced. The detailed evidence is in [the integration review](../INTEGRATION_REVIEW.md).

## Validation and limitations of AI output

Generated code was checked with SQLite-backed concurrency and injected-clock tests, frontend contract/component tests, production builds and actual HTTP/browser demonstrations. Test failures exposed issues such as an EF query translation problem and asynchronous test-rendering waits; fixes were followed by reruns. Live testing also exposed a Windows logging-provider failure that ordinary service tests did not reveal.

Latest verified totals are **66 backend and 29 frontend tests (95 total)**. These checks provide bounded evidence, not proof of production correctness. AI can misread contracts, overlook races, overstate results or propose unnecessary infrastructure. Human verification should still examine transaction boundaries, provider recovery guarantees, privacy/authorization, money semantics and usability. Real HR, authentication, operational deployment and production security were not validated by these local simulations.

No hosted CI success, comprehensive human audit or real reimbursement/payment is claimed. See [tests](test-cases.md) and [demo evidence](DEMO.md) for reproducible checks.
