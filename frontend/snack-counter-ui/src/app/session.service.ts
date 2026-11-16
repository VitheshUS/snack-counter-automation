import { computed, inject, Injectable, signal } from '@angular/core';
import { Api } from './api.service';
import { Employee, Office } from './api.models';
import { errorMessage } from './format';

@Injectable({ providedIn: 'root' })
export class Session {
  api = inject(Api);
  employees = signal<Employee[]>([]);
  offices = signal<Office[]>([]);
  selected = signal(0);
  employee = computed(() => this.employees().find((e) => e.id === this.selected()) ?? null);
  office = computed(() => this.offices().find((o) => o.id === this.employee()?.officeId));
  busy = signal(false);
  loading = signal(false);
  message = signal('');
  error = signal('');
  async load() {
    this.loading.set(true);
    this.error.set('');
    try {
      const [employees, offices] = await Promise.all([this.api.employees(), this.api.offices()]);
      this.employees.set(employees);
      this.offices.set(offices);
      if (!this.selected()) this.selected.set(employees[0]?.id ?? 0);
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.loading.set(false);
    }
  }
  async perform(work: () => Promise<void>) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await work();
    } catch (e) {
      this.error.set(errorMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
