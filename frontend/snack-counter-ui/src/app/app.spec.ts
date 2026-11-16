import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { Api } from './api.service';

describe('App shell', () => {
  it('shows demo identity and separate HR navigation', async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        {
          provide: Api,
          useValue: {
            employees: () =>
              Promise.resolve([{ id: 1, name: 'Ada', officeId: 1, volunteers: true }]),
            offices: () => Promise.resolve([{ id: 1, name: 'Oslo' }]),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const element: HTMLElement = fixture.nativeElement;
    expect(element.textContent).toContain('Demo employee');
    expect(element.textContent).toContain('Ada · Oslo');
    expect(element.querySelector('nav[aria-label="HR demo navigation"]')).toBeTruthy();
    expect(element.textContent).toContain('no authentication');
  });
});
