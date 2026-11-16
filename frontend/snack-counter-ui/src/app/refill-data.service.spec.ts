import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { Api } from './api.service';
import { Session } from './session.service';
import { RefillData } from './refill-data.service';

describe('Refill status freshness', () => {
  it('refreshes the shared status when entering a route again', async () => {
    const api = {
      refills: vi.fn().mockResolvedValue([]),
      budget: vi.fn().mockResolvedValue({ currency: 'NOK' }),
      invitations: vi.fn().mockResolvedValue([]),
    };
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: api }] });
    const session = TestBed.inject(Session);
    session.employees.set([{ id: 1, name: 'Ada', officeId: 1, volunteers: true }]);
    session.selected.set(1);
    const data = TestBed.inject(RefillData);
    TestBed.tick();
    await data.reload(); // Wait for a complete load; earlier request is generation guarded.
    expect(data.current()).toBeNull();
    api.refills.mockResolvedValue([{ id: 9, officeId: 1, state: 'Reserved', beingHandled: true }]);
    data.refreshOnEnter();
    await vi.waitFor(() => expect(data.current()?.id).toBe(9));
    expect(data.current()?.beingHandled).toBe(true);
  });
});
