import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Session } from './session.service';
import { RefillData } from './refill-data.service';
import { MoneyPipe } from './format';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, MoneyPipe],
  template: `
    <header class="page-heading">
      <div>
        <p class="eyebrow">YOUR OFFICE</p>
        <h1>A little care, shared.</h1>
        <p>Snacks are nice. Taking part is always optional.</p>
      </div>
      <span class="office-tag">{{ session.office()?.name }}</span>
    </header>
    @if (data.loading()) {
      <p role="status" class="card">Loading your office…</p>
    } @else if (data.error()) {
      <div class="notice error" role="alert">
        {{ data.error() }} <button (click)="data.reload()">Retry</button>
      </div>
    } @else if (session.employee()) {
      <div class="dashboard-grid">
        <section class="card counter-card">
          <p class="eyebrow">SNACK COUNTER</p>
          @if (data.current(); as refill) {
            <span class="badge">{{ refill.state }}</span>
            <h2>
              {{
                refill.beingHandled ? 'Someone is taking care of it.' : 'An opportunity to help.'
              }}
            </h2>
            <p>
              {{
                refill.beingHandled
                  ? 'No need to make another commitment. You can check back whenever you like.'
                  : 'The counter was reported low or empty. Volunteers can choose whether to help.'
              }}
            </p>
            <a class="button primary" routerLink="/volunteer"
              >View volunteer options <span aria-hidden="true">↗</span></a
            >
          } @else {
            <h2>How is the counter looking?</h2>
            <p>
              If snacks are running low or empty, let the office know. An empty counter is okay too.
            </p>
            <button class="primary" [disabled]="session.busy()" (click)="report()">
              {{ session.busy() ? 'Sending…' : 'Report low or empty snacks' }}
            </button>
          }
          <p class="small muted">One refill opportunity at a time. No public assignee names.</p>
        </section>
        @if (data.budget(); as b) {
          <section class="card">
            <p class="eyebrow">MONTHLY BUDGET · {{ b.month }}</p>
            <h2 class="amount">{{ b.availableMinor | money: b.currency }}</h2>
            <p>Available for purchase authorization</p>
            <dl>
              <div>
                <dt>Monthly limit</dt>
                <dd>{{ b.limitMinor | money: b.currency }}</dd>
              </div>
              <div>
                <dt>Actual spending</dt>
                <dd>{{ b.spentMinor | money: b.currency }}</dd>
              </div>
              <div>
                <dt>Outstanding commitments</dt>
                <dd>{{ b.reservedMinor | money: b.currency }}</dd>
              </div>
            </dl>
            <div class="allowance">
              <strong>{{ b.releasedMinor | money: b.currency }}</strong><span
                >Cumulative allowance released so far</span
              >
            </div>
            <p class="small muted">
              {{ allowanceLeft() | money: b.currency }} of released allowance remains. Unused
              allowance carries forward within the month. Exceeding it gives a warning, not an
              automatic rejection.
            </p>
          </section>
        }
      </div>
      <section class="card quiet">
        <h2>Help on your terms.</h2>
        <p>
          You can decline or ignore invitations without a penalty. Purchasing and HR reimbursement
          are separate decisions.
        </p>
        <a routerLink="/expenses">Your expenses & reimbursements →</a>
      </section>
      <button class="text-button" [disabled]="session.busy()" (click)="data.reload()">
        Refresh office status
      </button>
    }
  `,
})
export class Dashboard {
  session = inject(Session);
  data = inject(RefillData);
  constructor() {
    this.data.refreshOnEnter();
  }
  allowanceLeft = computed(() => {
    const b = this.data.budget();
    return b
      ? (BigInt(b.releasedMinor) - BigInt(b.spentMinor) - BigInt(b.reservedMinor)).toString()
      : '0';
  });
  report() {
    void this.session.perform(async () => {
      const employee = this.session.employee()!;
      try {
        const request = await this.session.api.report(employee.officeId, employee.id);
        this.session.message.set(
          `Refill #${request.id} is ${request.state.toLowerCase()}. Repeated reports share the same active opportunity.`,
        );
      } finally {
        await this.data.reload();
      }
    });
  }
}
