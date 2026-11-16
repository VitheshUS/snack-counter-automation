import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { vi } from 'vitest';
import { Expenses } from './expenses';
import { Session } from './session.service';
import { Api } from './api.service';

describe('Expense claim discovery', () => {
  const purchase = {
    id: 5,
    officeId: 1,
    month: '2026-11',
    estimateMinor: 15000,
    actualMinor: 12345,
    cancelled: false,
    requiresHrReview: false,
  };
  const claim = {
    id: 8,
    purchaseCommitmentId: 5,
    employeeId: 2,
    currency: 'NOK',
    amountMinor: 12345,
    description: 'Snacks',
    receiptReference: 'private-ref',
    state: 'PendingHR',
    updatedAt: '639000000000000000',
  };
  function setup(existing = true) {
    const api = {
      purchases: vi.fn().mockResolvedValue([purchase]),
      claims: vi.fn().mockResolvedValue(existing ? [claim] : []),
      native: vi.fn().mockResolvedValue([{ officeId: 1, currency: 'NOK' }]),
      draft: vi.fn(),
      submit: vi.fn(),
    };
    TestBed.configureTestingModule({
      imports: [Expenses],
      providers: [{ provide: Api, useValue: api }],
    });
    const session = TestBed.inject(Session);
    session.employees.set([
      { id: 2, officeId: 1, name: 'Erik', volunteers: true },
      { id: 5, officeId: 2, name: 'Ravi', volunteers: true },
    ]);
    session.selected.set(2);
    const fixture = TestBed.createComponent(Expenses);
    return { api, session, fixture };
  }
  it('automatically displays existing status, receipt and correctly scaled expense after refresh', async () => {
    const { api, fixture } = setup();
    await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Pending HR');
    expect(fixture.nativeElement.textContent).toContain('private-ref');
    expect(fixture.nativeElement.textContent).toContain('123,45');
    expect(fixture.nativeElement.textContent).not.toContain('API has no');
    await fixture.componentInstance.load();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Claim #8');
    expect(api.claims).toHaveBeenCalledWith(2);
    expect(api.draft).not.toHaveBeenCalled();
    expect(api.submit).not.toHaveBeenCalled();
  });
  it('shows a friendly missing-claim state without creating a claim', async () => {
    const { api, fixture } = setup(false);
    await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No reimbursement claim yet');
    expect(api.draft).not.toHaveBeenCalled();
  });
  it('leaves loading state and offers retry when discovery fails', async () => {
    const { api, fixture } = setup();
    api.claims.mockRejectedValue(new HttpErrorResponse({ status: 0 }));
    await fixture.whenStable();
    expect(fixture.componentInstance.loading()).toBe(false);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Cannot reach the API');
    api.claims.mockResolvedValue([claim]);
    await fixture.componentInstance.load();
    await fixture.whenStable();
    expect(fixture.componentInstance.error()).toBe('');
    expect(fixture.nativeElement.textContent).toContain('private-ref');
  });
  it('discards old employee responses after switching demo identity', async () => {
    const { api, fixture, session } = setup();
    let resolveOld!: (value: unknown[]) => void;
    api.claims.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveOld = resolve;
        }),
    );
    fixture.detectChanges();
    session.selected.set(5);
    api.purchases.mockResolvedValue([]);
    api.claims.mockResolvedValue([]);
    fixture.detectChanges();
    resolveOld([claim]);
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Ravi');
    expect(fixture.nativeElement.textContent).not.toContain('private-ref');
    expect(fixture.componentInstance.purchases()).toEqual([]);
  });
});
