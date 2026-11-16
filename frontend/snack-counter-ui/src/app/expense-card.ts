import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Claim, Purchase } from './api.models';
import { Session } from './session.service';
import { MoneyPipe, TickDatePipe } from './format';

@Component({
  selector: 'app-expense-card',
  imports: [FormsModule, MoneyPipe, TickDatePipe],
  template: `
    <article class="card">
      <div class="section-top">
        <div>
          <p class="eyebrow">PURCHASE #{{ purchase().id }} · {{ purchase().month }}</p>
          <h2>{{ purchase().actualMinor | money: currency() }}</h2>
        </div>
        <span class="badge">Completed expense</span>
      </div>
      @if (purchase().requiresHrReview) {
        <p class="notice warning">
          This expense exceeded its estimate and is flagged for HR review.
        </p>
      }
      @if (claim(); as c) {
        <div class="claim-panel">
          <div class="section-top">
            <strong>Claim #{{ c.id }}</strong
            ><span class="badge">{{ labels[c.state] }}</span>
          </div>
          <p>{{ c.amountMinor | money: c.currency }} · {{ c.description }}</p>
          <p class="small">Receipt reference: {{ c.receiptReference }}</p>
          <p class="small muted">Updated {{ c.updatedAt | tickDate }}</p>
          @if (c.state === 'Approved') {
            <p>Approved by HR. Payment has not been confirmed.</p>
          }
          @if (c.state === 'Reimbursed') {
            <p>Payment confirmation has been simulated.</p>
          }
          @if (c.state === 'SubmissionUncertain' || c.state === 'SubmissionPending') {
            <p>The submission outcome needs checking. Reconcile before submitting again.</p>
          }
          @if (c.state === 'Rejected') {
            <p>HR rejected this claim. The office expense remains unchanged.</p>
          }
          <div class="actions">
            @if (c.state === 'Draft' || c.state === 'SubmissionFailed') {
              <button class="primary" [disabled]="session.busy()" (click)="update('submit')">
                Submit to simulated HR
              </button>
            }
            @if (!['Draft', 'SubmissionFailed', 'Rejected', 'Reimbursed'].includes(c.state)) {
              <button [disabled]="session.busy()" (click)="update('reconcile')">
                Reconcile with HR
              </button>
            }
            <button [disabled]="session.busy()" (click)="update('claim')">Refresh status</button>
          </div>
        </div>
      } @else if (positiveExpense()) {
        <p>
          No reimbursement claim yet. Add a description and receipt reference to create a draft.
        </p>
        <form class="form-block" (ngSubmit)="create()">
          <label [for]="'description-' + purchase().id">Description</label
          ><input
            [id]="'description-' + purchase().id"
            name="description"
            [(ngModel)]="description"
            maxlength="500"
            required
          />
          <label [for]="'receipt-' + purchase().id">Private receipt reference</label
          ><input
            [id]="'receipt-' + purchase().id"
            name="receipt"
            [(ngModel)]="receipt"
            maxlength="500"
            placeholder="Your private receipt identifier"
            required
          />
          <p class="small muted">
            References only. No receipt image is uploaded. Use synthetic references for this demo.
          </p>
          <button
            class="primary"
            [disabled]="session.busy() || !description.trim() || !receipt.trim()"
          >
            Create claim
          </button>
        </form>
      } @else {
        <p class="muted">No reimbursement claim is available for a zero-expense completion.</p>
      }
    </article>
  `,
})
export class ExpenseCard {
  purchase = input.required<Purchase>();
  currency = input.required<string>();
  existingClaim = input<Claim | null>(null);
  session = inject(Session);
  claim = signal<Claim | null>(null);
  description = '';
  receipt = '';
  constructor() {
    effect(() => this.claim.set(this.existingClaim()));
  }
  labels: Record<string, string> = {
    Draft: 'Draft',
    SubmissionPending: 'Submission pending',
    PendingHR: 'Pending HR',
    SubmissionFailed: 'Submission failed',
    SubmissionUncertain: 'Submission uncertain',
    Approved: 'Approved · not paid',
    Rejected: 'Rejected',
    Reimbursed: 'Payment confirmed (demo)',
  };
  positiveExpense() {
    return BigInt(this.purchase().actualMinor ?? 0) > 0n;
  }
  create() {
    void this.session.perform(async () => {
      const claim = await this.session.api.draft(
        this.purchase().id,
        this.session.selected(),
        this.description,
        this.receipt,
      );
      this.claim.set(claim);
      this.session.message.set(
        `Claim #${claim.id} loaded. Creating a draft does not submit it to HR.`,
      );
    });
  }
  update(action: 'submit' | 'reconcile' | 'claim') {
    void this.session.perform(async () => {
      this.claim.set(await this.session.api[action](this.claim()!.id, this.session.selected()));
      this.session.message.set('Claim status refreshed. HR approval and payment are separate.');
    });
  }
}
