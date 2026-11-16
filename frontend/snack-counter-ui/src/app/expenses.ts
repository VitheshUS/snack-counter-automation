import { Component, effect, inject, signal, untracked } from '@angular/core';
import { Claim, Purchase } from './api.models';
import { Session } from './session.service';
import { errorMessage } from './format';
import { ExpenseCard } from './expense-card';

@Component({
  selector: 'app-expenses',
  imports: [ExpenseCard],
  template: `
    <header class="page-heading">
      <div>
        <p class="eyebrow">YOUR PURCHASES</p>
        <h1>Expenses, without the guesswork.</h1>
        <p>Private purchase records for {{ session.employee()?.name }}.</p>
      </div>
    </header>
    <div class="notice">
      Purchase authorization is not a promise of reimbursement. HR approval and payment confirmation
      are separate steps. Approval normally takes 1–10 days; this demo never approves automatically.
    </div>
    @if (loading()) {
      <p class="card" role="status">Loading your completed purchases…</p>
    } @else if (error()) {
      <div class="notice error" role="alert">
        {{ error() }} <button (click)="load()">Retry</button>
      </div>
    } @else {
      @for (purchase of purchases(); track purchase.id) {
        <app-expense-card
          [purchase]="purchase"
          [currency]="currencies()[purchase.month]"
          [existingClaim]="claims()[purchase.id] ?? null"
        />
      } @empty {
        <section class="card">
          <h2>No completed purchases yet.</h2>
          <p>
            Once you record an actual purchase, it will appear here. Cancelled commitments are
            excluded.
          </p>
        </section>
      }
    }
    <button class="text-button" [disabled]="session.busy() || loading()" (click)="load()">
      Refresh purchases
    </button>
  `,
})
export class Expenses {
  session = inject(Session);
  purchases = signal<Purchase[]>([]);
  currencies = signal<Record<string, string>>({});
  claims = signal<Record<number, Claim>>({});
  loading = signal(false);
  error = signal('');
  private generation = 0;
  constructor() {
    effect(() => {
      const employee = this.session.employee();
      untracked(() => {
        this.purchases.set([]);
        if (employee) void this.load();
      });
    });
  }
  async load() {
    const employee = this.session.employee();
    if (!employee) return;
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set('');
    try {
      const [allPurchases, claims] = await Promise.all([
        this.session.api.purchases(employee.id),
        this.session.api.claims(employee.id),
      ]);
      const purchases = allPurchases.filter((p) => !p.cancelled && p.actualMinor != null);
      // Purchase response omits currency: use the original budget month's native report.
      const currencies: Record<string, string> = {};
      await Promise.all(
        [...new Set(purchases.map((p) => p.month))].map(async (month) => {
          const row = (await this.session.api.native(month)).find(
            (o) => o.officeId === employee.officeId,
          );
          if (!row) throw new Error('The original purchase currency could not be loaded.');
          currencies[month] = row.currency;
        }),
      );
      if (generation === this.generation && employee.id === this.session.selected()) {
        this.currencies.set(currencies);
        this.claims.set(Object.fromEntries(claims.map((c) => [c.purchaseCommitmentId, c])));
        this.purchases.set(purchases);
      }
    } catch (e) {
      if (generation === this.generation) this.error.set(errorMessage(e));
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
}
