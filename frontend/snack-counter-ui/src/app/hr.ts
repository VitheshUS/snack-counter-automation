import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Session } from './session.service';
import { Claim, Consolidated, Contribution, ContributionTotal, Spending } from './api.models';
import { errorMessage, MoneyPipe, TickDatePipe } from './format';

@Component({
  selector: 'app-hr',
  imports: [FormsModule, MoneyPipe, TickDatePipe],
  template: `
    <header class="page-heading">
      <div>
        <p class="eyebrow">HR WORKSPACE · DEMO</p>
        <h1>A clear view of shared effort.</h1>
        <p>Office spending and contributions. Rewards remain HR's decision.</p>
      </div>
    </header>
    <div class="notice warning">
      Demo HR area — no real authorization. Production must restrict these routes and employee
      contribution data to authorized HR staff.
    </div>
    <form class="inline-form card" (ngSubmit)="load()">
      <label for="month">Reporting month</label
      ><input id="month" type="month" name="month" [(ngModel)]="month" required /><button
        class="primary"
        [disabled]="loading() || session.busy()"
      >
        Load report
      </button>
    </form>
    @if (loading()) {
      <p role="status" class="card">Loading spending and contributions…</p>
    } @else if (error()) {
      <div class="notice error" role="alert">
        {{ error() }} <button (click)="load()">Retry</button>
      </div>
    } @else {
      <div class="dashboard-grid">
        @for (office of native(); track office.officeId) {
          <section class="card">
            <p class="eyebrow">{{ office.office }} · {{ office.month }}</p>
            <h2 class="amount">{{ office.spentMinor | money: office.currency }}</h2>
            <p>Actual expenditure, independent of HR approval</p>
            <dl>
              <div>
                <dt>Outstanding commitments</dt>
                <dd>{{ office.outstandingMinor | money: office.currency }}</dd>
              </div>
            </dl>
          </section>
        }
      </div>
      @if (report(); as r) {
        <section class="card consolidated">
          <p class="eyebrow">CONSOLIDATED · {{ r.month }} · SIMULATED RATE</p>
          <h2>{{ r.spentMinor | money: r.currency }} actual spending</h2>
          <p>
            {{ r.outstandingMinor | money: r.currency }} outstanding commitments, reported
            separately.
          </p>
          <p class="small">
            1 {{ r.exchangeRate.sourceCurrency }} = {{ r.exchangeRate.numerator }} /
            {{ r.exchangeRate.denominator }} {{ r.exchangeRate.targetCurrency }}. Rate recorded
            {{ r.exchangeRate.recordedAt | tickDate }} for {{ r.exchangeRate.month }}.
          </p>
          <p class="small muted">
            The backend retains the rate for this period. Native currencies are never added
            directly.
          </p>
        </section>
      }
      <section class="card">
        <h2>Contribution summary</h2>
        <p class="muted">
          Points recognize participation, never spending amounts. No penalties for declining or
          ignoring.
        </p>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Employee</th>
                <th scope="col">Points</th>
                <th scope="col">Reports</th>
                <th scope="col">Inspections</th>
                <th scope="col">Refills</th>
              </tr>
            </thead>
            <tbody>
              @for (row of board(); track row.employeeId) {
                <tr>
                  <td>{{ row.name }}</td>
                  <td>{{ row.points }}</td>
                  <td>{{ row.reports }}</td>
                  <td>{{ row.inspections }}</td>
                  <td>{{ row.refills }}</td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="5">No contribution data yet.</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
      <section class="card">
        <h2>Contribution history</h2>
        <p class="small muted">
          All-time activity; the month filter applies only to spending reports.
        </p>
        @for (event of history(); track event.id) {
          <div class="list-row">
            <div>
              <strong>{{ name(event.employeeId) }} · {{ event.kind }}</strong>
              <p class="small muted">
                Refill #{{ event.refillRequestId }} · {{ event.earnedAt | tickDate }}
              </p>
            </div>
            <span class="badge">+{{ event.points }} points</span>
          </div>
        } @empty {
          <p>No contributions recorded yet.</p>
        }
      </section>
    }
    <section class="card simulation">
      <p class="eyebrow">SIMULATION CONTROLS — HR ONLY IN PRODUCTION</p>
      <h2>Simulate an HR decision</h2>
      <p>
        Enter a known claim ID from the employee's reimbursement screen. The backend does not
        provide an HR claim list.
      </p>
      <label for="hrClaim">Claim ID</label
      ><input id="hrClaim" type="number" min="1" step="1" [(ngModel)]="claimId" />
      <div class="actions">
        <button [disabled]="session.busy() || !claimId" (click)="decide('Approved')">
          Simulate approval</button
        ><button [disabled]="session.busy() || !claimId" (click)="decide('Rejected')">
          Simulate rejection</button
        ><button [disabled]="session.busy() || !claimId" (click)="decide('Reimbursed')">
          Confirm simulated payment
        </button>
      </div>
      <p class="small muted">
        Approval/rejection requires at least one backend-clock day after submission. Payment
        requires prior approval. These actions never change the recorded expense.
      </p>
      @if (decision(); as c) {
        <p role="status">
          Claim #{{ c.id }}: <strong>{{ c.state }}</strong> ·
          {{ c.amountMinor | money: c.currency }}
        </p>
      }
    </section>
  `,
})
export class Hr {
  session = inject(Session);
  month = `${new Date().getFullYear()}-${String(new Date().getMonth() + 1).padStart(2, '0')}`;
  native = signal<Spending[]>([]);
  report = signal<Consolidated | null>(null);
  board = signal<ContributionTotal[]>([]);
  history = signal<Contribution[]>([]);
  loading = signal(false);
  error = signal('');
  decision = signal<Claim | null>(null);
  claimId: number | null = null;
  private generation = 0;
  constructor() {
    void this.load();
  }
  async load() {
    if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(this.month)) {
      this.error.set('Choose a valid reporting month.');
      return;
    }
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set('');
    try {
      const [native, report, board, history] = await Promise.all([
        this.session.api.native(this.month),
        this.session.api.consolidated(this.month),
        this.session.api.leaderboard(),
        this.session.api.history(),
      ]);
      if (generation !== this.generation) return;
      this.native.set(native);
      this.report.set(report);
      this.board.set(board);
      this.history.set(history);
    } catch (e) {
      if (generation === this.generation) this.error.set(errorMessage(e));
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
  name(id: number) {
    return this.board().find((e) => e.employeeId === id)?.name ?? `Employee #${id}`;
  }
  decide(decision: 'Approved' | 'Rejected' | 'Reimbursed') {
    void this.session.perform(async () => {
      if (!this.claimId || !Number.isSafeInteger(this.claimId) || this.claimId < 1)
        throw new Error('Enter a valid claim ID.');
      this.decision.set(await this.session.api.decision(this.claimId, decision));
      this.session.message.set('The simulated HR decision was recorded.');
    });
  }
}
