import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Api } from './api.service';

describe('API contracts', () => {
  it('discovers employee claims with a read-only GET', async () => {
    const promise = api.claims(2);
    const request = http.expectOne('/api/demo/employees/2/reimbursements');
    expect(request.request.method).toBe('GET');
    request.flush(
      '[{"id":8,"purchaseCommitmentId":5,"state":"PendingHR","receiptReference":"private-ref"}]',
    );
    expect((await promise)[0].receiptReference).toBe('private-ref');
  });
  let api: Api;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(Api);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('sends the exact purchase authorization body through the proxy', async () => {
    const promise = api.action(7, 'purchase', {
      employeeId: 2,
      estimatedAmountMinor: 50000,
      currency: 'NOK',
    });
    const request = http.expectOne('/api/refills/7/purchase');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      employeeId: 2,
      estimatedAmountMinor: 50000,
      currency: 'NOK',
    });
    request.flush('{"success":true,"allowanceWarning":true,"requiresHrReview":false,"error":null}');
    expect((await promise)?.allowanceWarning).toBe(true);
  });
  it('handles no-content invitation responses', async () => {
    const promise = api.action(7, 'decline', { employeeId: 2 });
    http.expectOne('/api/refills/7/decline').flush(null, { status: 204, statusText: 'No Content' });
    expect(await promise).toBeNull();
  });
  it('uses the owner query for claim lookup', async () => {
    const promise = api.claim(8, 2);
    http.expectOne('/api/reimbursements/8?employeeId=2').flush('{"id":8,"state":"Approved"}');
    expect((await promise).state).toBe('Approved');
  });
  it('preserves receipt references in claim creation without inventing amount/currency fields', async () => {
    const promise = api.draft(5, 2, 'Snacks', 'private-ref');
    const request = http.expectOne('/api/purchases/5/reimbursement');
    expect(request.request.body).toEqual({
      employeeId: 2,
      description: 'Snacks',
      receiptReference: 'private-ref',
    });
    request.flush('{"id":9}');
    expect((await promise).id).toBe(9);
  });
  it('uses the backend consolidated reporting endpoint', async () => {
    const promise = api.consolidated('2026-11');
    http
      .expectOne('/api/reports/monthly/2026-11/consolidated')
      .flush('{"spentMinor":9007199254740993}');
    expect((await promise).spentMinor).toBe('9007199254740993');
  });
});
