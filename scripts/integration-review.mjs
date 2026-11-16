// Opt-in verification against a FRESH disposable demo database, never production.
// Start the API on :5187, Angular on :4200, and headless Chrome with
// --remote-debugging-port=9227 and a temporary --user-data-dir, then run with Node 24.
import assert from 'node:assert/strict';

const base = 'http://127.0.0.1:4200';
const debuggerBase = 'http://127.0.0.1:9227';
async function api(path, body, expected = 200) {
  const response = await fetch(base + '/api' + path, body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  });
  const text = await response.text();
  assert.equal(response.status, expected, `${path}: ${text}`);
  return text ? JSON.parse(text) : null;
}
async function until(test, description) {
  const end = Date.now() + 15000;
  while (Date.now() < end) { if (await test()) return; await new Promise(r => setTimeout(r, 100)); }
  throw new Error('Timed out: ' + description);
}
const errors = [];
const pages = [];
async function page(route, width = 1440) {
  const target = await (await fetch(debuggerBase + '/json/new?about:blank', { method: 'PUT' })).json();
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener('open', r, { once: true }));
  let next = 0; const pending = new Map();
  ws.addEventListener('message', e => {
    const message = JSON.parse(e.data);
    if (message.method === 'Runtime.exceptionThrown') errors.push(message.params.exceptionDetails.text);
    if (message.id) {
      const task = pending.get(message.id); pending.delete(message.id);
      if (message.error) task.reject(new Error(JSON.stringify(message.error))); else task.resolve(message.result);
    }
  });
  const command = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++next; pending.set(id, { resolve, reject }); ws.send(JSON.stringify({ id, method, params }));
  });
  const evaluate = async expression => {
    const value = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (value.exceptionDetails) throw new Error(JSON.stringify(value.exceptionDetails));
    return value.result.value;
  };
  const p = {
    command, evaluate, target,
    text: () => evaluate('document.querySelector("main")?.innerText || ""'),
    has: text => evaluate(`document.querySelector('main')?.innerText.includes(${JSON.stringify(text)}) ?? false`),
    wait: text => until(() => p.has(text), text),
    click: async text => {
      await until(() => evaluate(`[...document.querySelectorAll('button')].some(b => b.textContent.trim() === ${JSON.stringify(text)} && !b.disabled)`), 'button ' + text);
      await evaluate(`[...document.querySelectorAll('button')].find(b => b.textContent.trim() === ${JSON.stringify(text)}).click()`);
    },
    input: async (selector, value) => {
      await until(() => evaluate(`!!document.querySelector(${JSON.stringify(selector)})`), 'input ' + selector);
      await evaluate(`(() => { const e=document.querySelector(${JSON.stringify(selector)}); e.value=${JSON.stringify(value)}; e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true})); })()`);
    },
    employee: async name => {
      await until(() => evaluate(`!![...document.querySelectorAll('#employee option')].find(o=>o.textContent.includes(${JSON.stringify(name)}))`), 'identity selector');
      await evaluate(`(() => {const s=document.querySelector('#employee');s.value=[...s.options].find(o=>o.textContent.includes(${JSON.stringify(name)})).value;s.dispatchEvent(new Event('change',{bubbles:true}));})()`);
      await until(() => evaluate(`!document.querySelector('main')?.innerText.includes('Loading')`), 'employee data');
    },
    nav: async route => {
      const origin = await evaluate('performance.timeOrigin');
      const employee = await evaluate('document.querySelector("#employee").value');
      await evaluate(`document.querySelector('nav a[href="${route}"]').click()`);
      await until(() => evaluate(`location.pathname === ${JSON.stringify(route)} && !!document.querySelector('app-${route.slice(1)}') && !document.querySelector('main')?.innerText.includes('Loading')`), 'navigation ' + route);
      assert.equal(await evaluate('performance.timeOrigin'), origin, 'Angular navigation must not reload the page');
      assert.equal(await evaluate('document.querySelector("#employee").value'), employee, 'Identity must persist across routes');
      assert.equal(await evaluate('getComputedStyle(document.querySelector(".sidebar")).display'), 'flex');
    },
    close: () => { ws.close(); return fetch(debuggerBase + '/json/close/' + target.id); },
  };
  pages.push(p);
  await command('Runtime.enable'); await command('Page.enable');
  await command('Emulation.setDeviceMetricsOverride', { width, height: 1100, deviceScaleFactor: 1, mobile: width < 500 });
  await command('Page.navigate', { url: base + route });
  await until(() => evaluate('!!document.querySelector("h1") && !document.querySelector("main").innerText.includes("Loading")'), 'page ' + route);
  return p;
}

assert.deepEqual(await api('/refills'), [], 'Use a fresh disposable database for this smoke test.');
try {
  const budget = await api('/offices/1/budget');
  const inrBudget = await api('/offices/2/budget');
  assert.equal(budget.limitMinor, 600000); assert.equal(inrBudget.limitMinor, 1500000);
  console.log('Actual budget responses:', JSON.stringify({ oslo: budget, bengaluru: inrBudget }));
  const reporter = await page('/dashboard');
  let text = (await reporter.text()).replace(/\s/g, '');
  assert(text.includes('6000,00kr'), 'NOK must be converted once');
  const volunteer = await page('/volunteer'); // Ada, employee B.
  const competitor = await page('/volunteer');
  await reporter.employee('Liv'); // Employee A reports, so both invitees are different employees.
  await reporter.click('Report low or empty snacks');
  await reporter.wait('An opportunity to help.');
  const refill = (await api('/refills'))[0];
  assert.equal((await api('/offices/1/refills', { employeeId: 3 })).id, refill.id);
  // Explicitly invoke the existing invitation endpoint instead of waiting for the worker interval.
  await api(`/refills/${refill.id}/invite`, {});
  await volunteer.click('Refresh private status'); await volunteer.wait('Accept invitation');
  await competitor.employee('Erik'); await competitor.wait('Accept invitation');
  await volunteer.click('Accept invitation'); await volunteer.wait('Confirm intention to purchase');
  await competitor.click('Accept invitation'); await competitor.wait('Action unavailable');
  await competitor.wait('Someone is already handling');
  await reporter.click('Refresh office status'); await reporter.wait('Someone is taking care of it.');
  assert(!await reporter.evaluate('document.querySelector(".counter-card").innerText.includes("Ada")'), 'Public status must omit assignee name');
  await reporter.nav('/volunteer'); await reporter.wait('Someone is already handling');
  await reporter.nav('/dashboard'); // A non-default employee selection also survives navigation.
  // Choose just above released allowance while staying within the monthly hard limit.
  const expectWarning = budget.releasedMinor < budget.limitMinor;
  const estimate = expectWarning ? budget.releasedMinor + 100 : 10000;
  const actual = estimate > 20000 ? estimate - 20000 : estimate - 1000;
  await volunteer.input('#estimate', (estimate / 100).toFixed(2));
  await volunteer.click('Confirm intention to purchase');
  if (expectWarning) await volunteer.wait('Purchase authorized.');
  else { await volunteer.wait('Actual expense'); console.log('Fourth allocation period: no weekly advisory expected; warning behavior remains covered by clock-controlled tests.'); }
  let balance = await api('/offices/1/budget'); assert.equal(balance.reservedMinor, estimate); assert.equal(balance.spentMinor, 0);
  await volunteer.input('#actual', (actual / 100).toFixed(2));
  await volunteer.click('Complete purchase'); await volunteer.wait('No active refill opportunity.');
  balance = await api('/offices/1/budget'); assert.equal(balance.reservedMinor, 0); assert.equal(balance.spentMinor, actual);
  await volunteer.nav('/expenses'); await volunteer.wait('No reimbursement claim yet.');
  await volunteer.input('input[name="description"]', 'Synthetic demonstration snacks');
  await volunteer.input('input[name="receipt"]', 'private-demo:integration-review');
  await volunteer.click('Create claim'); await volunteer.wait('Submit to simulated HR');
  await volunteer.click('Submit to simulated HR'); await volunteer.wait('Pending HR');
  const before = await api('/demo/employees/1/reimbursements'); assert.equal(before.length, 1);
  const oldOrigin = await volunteer.evaluate('performance.timeOrigin');
  await volunteer.command('Page.reload');
  await until(() => volunteer.evaluate(`performance.timeOrigin !== ${oldOrigin}`), 'full-page refresh');
  await volunteer.wait('Pending HR');
  await volunteer.wait('private-demo:integration-review');
  const after = await api('/demo/employees/1/reimbursements'); assert.equal(after[0].id, before[0].id);
  assert.deepEqual(await api('/demo/employees/2/reimbursements'), []);
  assert.deepEqual(await api('/demo/employees/999/reimbursements'), []);
  await volunteer.nav('/hr'); await volunteer.wait('Contribution summary');
  const report = await api(`/reports/monthly/${budget.month}/consolidated`);
  assert.equal(report.nativeTotals.find(n => n.officeId === 1).spentMinor, actual);
  assert.equal(report.spentMinor, actual);
  const board = await api('/demo/hr/contributions/leaderboard');
  assert.equal(board.find(e => e.employeeId === 1).points, 5); assert.equal(board.find(e => e.employeeId === 3).points, 1);
  await volunteer.nav('/dashboard'); await volunteer.wait('Available for purchase authorization');
  console.log('Browser main workflow passed: reporting, deduplication, two invitees, stale acceptance conflict, privacy, warning, purchase, reconciliation, claim submission/discovery after reload, HR reports, points, SPA navigation.');

  const next = await api('/offices/1/refills', { employeeId: 3 });
  await api(`/refills/${next.id}/offer`, { employeeId: 1 }, 204);
  await api(`/refills/${next.id}/purchase`, { employeeId: 1, estimatedAmountMinor: balance.availableMinor + 1, currency: 'NOK' }, 409);
  await api(`/refills/${next.id}/purchase`, { employeeId: 1 }, 400);
  await api(`/refills/${next.id}/purchase`, { employeeId: 1, estimatedAmountMinor: 10000, currency: 'NOK' });
  await api(`/refills/${next.id}/cancel-purchase`, { employeeId: 1, actualAmountMinor: 0, currency: 'NOK' });
  assert.equal((await api('/offices/1/budget')).availableMinor, balance.availableMinor);
  await api(`/refills/${next.id}/offer`, { employeeId: 2 }, 204);
  await api(`/refills/${next.id}/unnecessary`, { employeeId: 2 }, 204);
  const bengaluru = await api('/offices/2/refills', { employeeId: 6 });
  await api(`/refills/${bengaluru.id}/invite`, {});
  assert.equal((await api('/demo/employees/4/invitations')).filter(i => i.state === 'Invited').length, 1);
  await api(`/refills/${bengaluru.id}/decline`, { employeeId: 4 }, 204);
  assert.equal((await api('/demo/employees/6/invitations')).filter(i => i.state === 'Invited').length, 1);
  await api(`/refills/${bengaluru.id}/accept`, { employeeId: 5 }, 204);
  await api(`/refills/${bengaluru.id}/purchase`, { employeeId: 5, estimatedAmountMinor: 20000, currency: 'INR' });
  await api(`/refills/${bengaluru.id}/complete`, { employeeId: 5, actualAmountMinor: 15000, currency: 'INR' });
  const purchase = (await api('/demo/employees/5/purchases'))[0];
  const claim = await api(`/purchases/${purchase.id}/reimbursement`, { employeeId: 5, description: 'Synthetic snacks', receiptReference: 'private-demo:inr' });
  assert.equal((await api(`/reimbursements/${claim.id}/submit`, { employeeId: 5, demoMode: 'DefiniteFailure' })).state, 'SubmissionFailed');
  assert.equal((await api(`/reimbursements/${claim.id}/submit`, { employeeId: 5, demoMode: 'TimeoutAfterCreation' })).state, 'SubmissionUncertain');
  assert.equal((await api(`/reimbursements/${claim.id}/submit`, { employeeId: 5 })).state, 'SubmissionUncertain');
  assert.equal((await api(`/reimbursements/${claim.id}/reconcile`, { employeeId: 5 })).state, 'PendingHR');
  await api(`/demo/hr/reimbursements/${claim.id}/decision`, { decision: 'Approved' }, 409);
  await api(`/demo/hr/reimbursements/${claim.id}/decision`, { decision: 'Reimbursed' }, 409);
  const consolidated = await api(`/reports/monthly/${budget.month}/consolidated`);
  assert.equal(consolidated.nativeTotals.find(n => n.officeId === 2).spentMinor, 15000);
  assert.equal(consolidated.spentMinor, actual + Math.round(15000 * consolidated.exchangeRate.numerator / consolidated.exchangeRate.denominator));
  console.log('Additional HTTP scenarios passed: offer, zero-spending cancellation, unnecessary completion, budget rejection, 400 validation, decline/reassignment, INR spending, definite HR failure, uncertain submission and reconciliation, early approval/payment rejection.');

  for (const route of ['/dashboard', '/volunteer', '/expenses', '/hr']) {
    const mobile = await page(route, 390);
    assert(await mobile.evaluate('document.documentElement.scrollWidth <= innerWidth'), route + ' mobile overflow');
    assert(await mobile.evaluate('!!document.querySelector(".sidebar nav")'), 'Navigation missing');
    console.log('390px route check passed:', route);
  }
  assert.deepEqual(errors, [], 'No uncaught browser exceptions');
  console.log('All live integration checks passed. Expiry and delayed HR decisions are verified separately with the injected-clock tests.');
} finally {
  await Promise.all(pages.map(p => p.close().catch(() => {})));
}
