import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Session } from './session.service';
import { RefillData } from './refill-data.service';
import { TickDatePipe, toMinor } from './format';

@Component({
  selector: 'app-volunteer',
  imports: [FormsModule, TickDatePipe],
  template: `
    <header class="page-heading">
      <div>
        <p class="eyebrow">VOLUNTEER SPACE</p>
        <h1>A helping hand. If you like.</h1>
        <p>Private invitations for {{ session.employee()?.name }}. No obligation, ever.</p>
      </div>
    </header>
    @if (data.loading()) {
      <p class="card" role="status">Loading private invitations…</p>
    } @else if (data.error()) {
      <div class="notice error" role="alert">
        {{ data.error() }} <button (click)="data.reload()">Retry</button>
      </div>
    } @else if (data.current(); as refill) {
      <section class="card">
        <div class="section-top">
          <h2>Refill #{{ refill.id }}</h2>
          <span class="badge">{{ refill.state }}</span>
        </div>
        @if (refill.state === 'Open') {
          <p>No one has committed yet. It's fine to leave this opportunity open.</p>
          @if (invited()) {
            <div class="notice">
              <strong>You have a private invitation.</strong>
              <p>
                Respond by {{ invited()!.invitationExpiresAt | tickDate }}. The server checks
                availability when you respond.
              </p>
              <div class="actions">
                <button class="primary" [disabled]="session.busy()" (click)="act('accept')">
                  Accept invitation</button
                ><button [disabled]="session.busy()" (click)="act('decline')">No thanks</button>
              </div>
            </div>
          } @else {
            <p class="muted">
              No active invitation. You can still offer help, including after declining or ignoring
              an earlier invitation.
            </p>
          }
          <button
            [disabled]="session.busy() || !session.employee()?.volunteers"
            (click)="act('offer')"
          >
            I'd like to offer help
          </button>
        } @else if (ownsReservation()) {
          <p>
            You reserved this refill until {{ ownsReservation()!.reservationExpiresAt | tickDate }}.
            Please confirm purchasing before spending.
          </p>
          <div class="actions">
            <button [disabled]="session.busy()" (click)="act('release')">Release reservation</button
            ><button [disabled]="session.busy()" (click)="act('unnecessary')">
              No refill is needed
            </button>
          </div>
          <form (ngSubmit)="act('purchase')" class="form-block">
            <label for="estimate">Estimated purchase amount ({{ data.budget()?.currency }})</label
            ><input
              id="estimate"
              name="estimate"
              inputmode="decimal"
              placeholder="0.00"
              [(ngModel)]="estimate"
              required
            />
            <p class="small muted">
              The estimate reserves budget. Budget authorization does not guarantee HR
              reimbursement.
            </p>
            <button class="primary" [disabled]="session.busy() || !estimate">
              Confirm intention to purchase
            </button>
          </form>
        } @else if (ownsPurchase()) {
          <p>
            Your purchase commitment stays reserved, including when the task needs review. It will
            not expire into someone else's refill.
          </p>
          @if (refill.state === 'NeedsReview') {
            <div class="notice warning">
              This purchase needs follow-up. Record the actual spending, or explicitly confirm that
              nothing was purchased.
            </div>
          }
          <form class="form-block" (ngSubmit)="act('complete')">
            <label for="actual">Actual expense ({{ data.budget()?.currency }})</label
            ><input
              id="actual"
              name="actual"
              inputmode="decimal"
              placeholder="0.00"
              [(ngModel)]="actual"
              required
            /><button class="primary" [disabled]="session.busy() || !actual">
              Complete purchase
            </button>
          </form>
          <div class="form-block">
            <h3>Changed plans?</h3>
            <label class="check"
              ><input type="checkbox" [(ngModel)]="nothingBought" />I have not purchased
              anything.</label
            ><button [disabled]="session.busy() || !nothingBought" (click)="act('cancel-purchase')">
              Cancel before buying
            </button>
          </div>
        } @else {
          <p>
            Someone is already handling this refill. Their identity is private. No further action is
            needed.
          </p>
        }
      </section>
    } @else {
      <section class="card">
        <h2>No active refill opportunity.</h2>
        <p>
          Check the dashboard if snacks are running low. There is no need to create a task just to
          volunteer.
        </p>
      </section>
    }
    @if (warning()) {
      <div class="notice warning" role="status">{{ warning() }}</div>
    }
    <section class="card">
      <h2>Your invitation history</h2>
      @for (invite of data.invitations(); track $index) {
        <div class="list-row">
          <div>
            <strong>Refill #{{ invite.refillRequestId }}</strong>
            <p class="small muted">{{ invite.invitedAt | tickDate }}</p>
          </div>
          <span class="badge">{{ invite.state }}</span>
        </div>
      } @empty {
        <p class="muted">
          No invitations yet. Invitations are optional and arrive only when the backend offers an
          opportunity.
        </p>
      }
    </section>
    <button
      class="text-button"
      [disabled]="session.busy() || data.loading()"
      (click)="data.reload()"
    >
      Refresh private status
    </button>
  `,
})
export class Volunteer {
  session = inject(Session);
  data = inject(RefillData);
  estimate = '';
  actual = '';
  nothingBought = false;
  warning = signal('');
  constructor() {
    this.data.refreshOnEnter();
    effect(() => {
      this.session.selected();
      this.estimate = '';
      this.actual = '';
      this.nothingBought = false;
      this.warning.set('');
    });
  }
  invited = computed(() =>
    this.data
      .invitations()
      .find((i) => i.refillRequestId === this.data.current()?.id && i.state === 'Invited'),
  );
  ownsReservation = computed(() =>
    this.data.current()?.state === 'Reserved'
      ? this.data
          .invitations()
          .find((i) => i.refillRequestId === this.data.current()?.id && i.state === 'Reserved')
      : undefined,
  );
  ownsPurchase = computed(
    () =>
      ['Purchasing', 'NeedsReview'].includes(this.data.current()?.state ?? '') &&
      this.data
        .invitations()
        .some((i) => i.refillRequestId === this.data.current()?.id && i.state === 'Purchasing'),
  );
  act(action: string) {
    void this.session.perform(async () => {
      const request = this.data.current();
      const employee = this.session.employee();
      const budget = this.data.budget();
      if (!request || !employee || !budget) return;
      const body = {
        employeeId: employee.id,
        currency: budget.currency,
        estimatedAmountMinor: action === 'purchase' ? toMinor(this.estimate) : undefined,
        actualAmountMinor:
          action === 'complete'
            ? toMinor(this.actual, true)
            : action === 'cancel-purchase'
              ? 0
              : undefined,
      };
      if (action === 'cancel-purchase' && !this.nothingBought)
        throw new Error('Confirm you have not purchased anything.');
      try {
        const result = await this.session.api.action(request.id, action, body);
        this.warning.set(
          result?.allowanceWarning
            ? 'Purchase authorized. This exceeds the currently released allowance, but fits the monthly budget.'
            : result?.requiresHrReview
              ? 'Actual expense recorded in full. The overrun is flagged for HR review.'
              : '',
        );
        this.session.message.set('Your response was saved. Thank you for checking in.');
        this.nothingBought = false;
      } finally {
        await this.data.reload();
      }
    });
  }
}
