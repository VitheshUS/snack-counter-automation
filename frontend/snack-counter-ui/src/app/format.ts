import { Pipe, PipeTransform } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Integer } from './api.models';

// Keep unsafe JSON integers (including .NET ticks) as strings before JSON.parse
// rounds them. Quoted strings are consumed whole and never rewritten.
export function parseApiJson<T>(text: string): T {
  if (!text) return null as T;
  return JSON.parse(
    text.replace(
      /"(?:\\.|[^"\\])*"|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g,
      (token, number: string | undefined) =>
        number && !Number.isSafeInteger(Number(number)) ? JSON.stringify(number) : token,
    ),
  ) as T;
}
export function toMinor(input: string, allowZero = false): number {
  if (!/^\d+(\.\d{1,2})?$/.test(input.trim()))
    throw new Error('Enter an amount with up to two decimal places.');
  const [whole, fraction = ''] = input.trim().split('.');
  const value = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
  if (value > 1_000_000_000_000n || (allowZero ? value < 0n : value <= 0n))
    throw new Error('Enter a valid amount within the supported range.');
  return Number(value); // Backend's per-operation cap is below Number.MAX_SAFE_INTEGER.
}
export function money(value: Integer | null | undefined, currency = 'NOK'): string {
  if (value == null) return '—';
  if (typeof value === 'number' && !Number.isSafeInteger(value))
    throw new Error('Money must be an exact integer in minor units.');
  const minor = BigInt(value);
  const absolute = minor < 0n ? -minor : minor;
  // This is the single minor-to-major conversion for both NOK and INR.
  const whole = minor < 0n ? -(absolute / 100n) : absolute / 100n;
  return new Intl.NumberFormat(currency === 'INR' ? 'en-IN' : 'nb-NO', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })
    .formatToParts(minor < 0n && whole === 0n ? -0 : whole)
    .map((p) => (p.type === 'fraction' ? (absolute % 100n).toString().padStart(2, '0') : p.value))
    .join('');
}
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    let details = error.error;
    if (typeof details === 'string') {
      try {
        details = JSON.parse(details);
      } catch {
        details = null;
      }
    }
    const validation =
      details?.errors && typeof details.errors === 'object'
        ? Object.values(details.errors)
            .flat()
            .filter((v) => typeof v === 'string')
            .join(' ')
        : '';
    const message = details?.error || validation || details?.title;
    if (error.status === 409)
      return `${message || 'This opportunity changed while you were responding.'} Refresh and try again if you still want to help.`;
    return (
      message ||
      (error.status === 0
        ? 'Cannot reach the API. Check that the backend is running.'
        : `Request failed (${error.status}). Please try again.`)
    );
  }
  return error instanceof Error ? error.message : 'Something went wrong. Please try again.';
}
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(value: Integer | null | undefined, currency = 'NOK') {
    return money(value, currency);
  }
}
@Pipe({ name: 'tickDate' })
export class TickDatePipe implements PipeTransform {
  transform(value: Integer | null | undefined) {
    if (value == null) return '—';
    return new Date(Number((BigInt(value) - 621355968000000000n) / 10000n)).toLocaleString();
  }
}
