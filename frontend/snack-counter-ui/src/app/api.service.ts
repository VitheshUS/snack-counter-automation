import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, map } from 'rxjs';
import * as M from './api.models';
import { parseApiJson } from './format';

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  private get<T>(path: string) {
    return firstValueFrom(
      this.http.get(`/api${path}`, { responseType: 'text' }).pipe(map(parseApiJson<T>)),
    );
  }
  private post<T>(path: string, body: unknown) {
    return firstValueFrom(
      this.http.post(`/api${path}`, body, { responseType: 'text' }).pipe(map(parseApiJson<T>)),
    );
  }
  employees() {
    return this.get<M.Employee[]>('/demo/employees');
  }
  offices() {
    return this.get<M.Office[]>('/offices');
  }
  refills() {
    return this.get<M.Refill[]>('/refills');
  }
  budget(office: number) {
    return this.get<M.Budget>(`/offices/${office}/budget`);
  }
  invitations(employee: number) {
    return this.get<M.Invitation[]>(`/demo/employees/${employee}/invitations`);
  }
  report(office: number, employeeId: number) {
    return this.post<M.Refill>(`/offices/${office}/refills`, { employeeId });
  }
  action(id: number, action: string, body: M.ActionBody) {
    return this.post<M.BudgetResult | null>(`/refills/${id}/${action}`, body);
  }
  purchases(employee: number) {
    return this.get<M.Purchase[]>(`/demo/employees/${employee}/purchases`);
  }
  claims(employee: number) {
    return this.get<M.Claim[]>(`/demo/employees/${employee}/reimbursements`);
  }
  draft(id: number, employeeId: number, description: string, receiptReference: string) {
    return this.post<M.Claim>(`/purchases/${id}/reimbursement`, {
      employeeId,
      description,
      receiptReference,
    });
  }
  claim(id: number, employee: number) {
    return this.get<M.Claim>(`/reimbursements/${id}?employeeId=${employee}`);
  }
  submit(id: number, employeeId: number) {
    return this.post<M.Claim>(`/reimbursements/${id}/submit`, { employeeId });
  }
  reconcile(id: number, employeeId: number) {
    return this.post<M.Claim>(`/reimbursements/${id}/reconcile`, { employeeId });
  }
  native(month: string) {
    return this.get<M.Spending[]>(`/reports/monthly/${month}`);
  }
  consolidated(month: string) {
    return this.get<M.Consolidated>(`/reports/monthly/${month}/consolidated`);
  }
  leaderboard() {
    return this.get<M.ContributionTotal[]>('/demo/hr/contributions/leaderboard');
  }
  history() {
    return this.get<M.Contribution[]>('/demo/hr/contributions');
  }
  decision(id: number, decision: 'Approved' | 'Rejected' | 'Reimbursed') {
    return this.post<M.Claim>(`/demo/hr/reimbursements/${id}/decision`, { decision });
  }
}
