import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { Session } from './session.service';
import { Budget, Invitation, Refill } from './api.models';
import { errorMessage } from './format';

@Injectable({ providedIn: 'root' })
export class RefillData {
  session = inject(Session);
  budget = signal<Budget | null>(null);
  requests = signal<Refill[]>([]);
  invitations = signal<Invitation[]>([]);
  loading = signal(false);
  error = signal('');
  current = computed(() => this.requests().find((r) => r.state !== 'Completed') ?? null);
  private generation = 0;
  private initialized = false;
  constructor() {
    effect(() => {
      const employee = this.session.employee();
      untracked(() => {
        if (employee) {
          this.initialized = true;
          void this.reload();
        }
      });
    });
  }
  refreshOnEnter() {
    // This service survives route changes. Re-entering must not reuse an expired
    // reservation or another browser's old status indefinitely.
    if (this.initialized && !this.loading()) void this.reload();
  }
  async reload() {
    const employee = this.session.employee();
    if (!employee) return;
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set('');
    this.budget.set(null);
    this.requests.set([]);
    this.invitations.set([]);
    try {
      // GET /refills sweeps expiry; fetch it before private assignment state.
      const requests = await this.session.api.refills();
      const [budget, invitations] = await Promise.all([
        this.session.api.budget(employee.officeId),
        this.session.api.invitations(employee.id),
      ]);
      if (generation !== this.generation || employee.id !== this.session.selected()) return;
      this.budget.set(budget);
      this.invitations.set(invitations);
      this.requests.set(requests.filter((r) => r.officeId === employee.officeId));
    } catch (e) {
      if (generation === this.generation) this.error.set(errorMessage(e));
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
}
