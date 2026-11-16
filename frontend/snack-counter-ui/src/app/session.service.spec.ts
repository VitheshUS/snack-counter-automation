import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { HttpErrorResponse } from '@angular/common/http';
import { Session } from './session.service';
import { Api } from './api.service';

describe('Submission feedback', () => {
  it('prevents duplicate actions while pending and unlocks after failure', async () => {
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: {} }] });
    const session = TestBed.inject(Session);
    let reject!: (reason: unknown) => void;
    const work = vi.fn(
      () =>
        new Promise<void>((_, failure) => {
          reject = failure;
        }),
    );
    const pending = session.perform(work);
    expect(session.busy()).toBe(true);
    await session.perform(work);
    expect(work).toHaveBeenCalledTimes(1);
    reject(new HttpErrorResponse({ status: 409, error: { error: 'Reservation expired.' } }));
    await pending;
    expect(session.busy()).toBe(false);
    expect(session.error()).toContain('Reservation expired.');
    await session.perform(async () => {
      session.message.set('Saved');
    });
    expect(session.error()).toBe('');
    expect(session.message()).toBe('Saved');
  });
});
