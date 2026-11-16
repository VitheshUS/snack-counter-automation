import { HttpErrorResponse } from '@angular/common/http';
import { errorMessage, money, parseApiJson, TickDatePipe, toMinor } from './format';

describe('Financial and timestamp formatting', () => {
  it.each([
    [600000, 'NOK', '6000,00kr'],
    [450000, 'NOK', '4500,00kr'],
    [1500000, 'INR', '₹15,000.00'],
    [1125000, 'INR', '₹11,250.00'],
    [12345, 'NOK', '123,45kr'],
    [12345, 'INR', '₹123.45'],
  ])('converts %s minor units to %s exactly once', (minor, currency, expected) => {
    expect(money(minor, currency as string).replace(/\s/g, '')).toBe(expected);
    expect(money(String(minor), currency as string).replace(/\s/g, '')).toBe(expected);
  });
  it('does not accept already rounded unsafe numeric money', () => {
    expect(() => money(Number.MAX_SAFE_INTEGER + 1, 'NOK')).toThrow('exact integer');
  });
  it('shows specific 400 validation errors rather than a generic title', () => {
    expect(
      errorMessage(
        new HttpErrorResponse({
          status: 400,
          error: {
            title: 'Validation failed',
            errors: { receiptReference: ['A receipt reference is required.'] },
          },
        }),
      ),
    ).toBe('A receipt reference is required.');
  });
  it('parses amounts exactly without floating point multiplication', () => {
    expect(toMinor('0.29')).toBe(29);
    expect(toMinor('1500.05')).toBe(150005);
    expect(toMinor('0', true)).toBe(0);
  });
  it('rejects invalid precision, zero estimates, negatives and amounts beyond the backend limit', () => {
    for (const value of ['1.001', '0', '-1', '1e5', '10000000000.01', '', 'NaN'])
      expect(() => toMinor(value)).toThrow();
  });
  it('formats NOK with exact minor units', () => {
    expect(money('9007199254740993', 'NOK').replace(/\s/g, '')).toContain('90071992547409,93');
  });
  it('uses INR currency and Indian digit grouping', () => {
    expect(money('123456789', 'INR')).toBe('₹12,34,567.89');
  });
  it('preserves negative amounts smaller than one unit', () => {
    expect(money(-1, 'INR')).toBe('-₹0.01');
  });
  it('keeps unsafe integers exact before JSON parsing while preserving quoted text', () => {
    const data = parseApiJson<{ amount: string; ticks: string; id: number; description: string }>(
      ' {"amount":9007199254740993,"ticks":639000000000000001,"id":2,"description":"price 9007199254740993"}',
    );
    expect(data.amount).toBe('9007199254740993');
    expect(data.ticks).toBe('639000000000000001');
    expect(data.id).toBe(2);
    expect(data.description).toBe('price 9007199254740993');
  });
  it('handles 204 responses and .NET ticks', () => {
    expect(parseApiJson('')).toBeNull();
    expect(new TickDatePipe().transform('621355968000000000')).toBe(new Date(0).toLocaleString());
  });
  it('explains backend conflicts and offline errors', () => {
    expect(
      errorMessage(
        new HttpErrorResponse({ status: 409, error: '{"error":"Monthly budget exhausted."}' }),
      ),
    ).toContain('Monthly budget exhausted.');
    expect(errorMessage(new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach the API');
  });
});
