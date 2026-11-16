// Captures real browser output. Only use the isolated disposable setup in docs/DEMO.md.
import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

assert(process.argv.includes('--disposable-demo'), 'Read docs/DEMO.md and pass --disposable-demo.');
const base = 'http://127.0.0.1:4201';
const debuggerBase = 'http://127.0.0.1:9228';
const output = new URL('../docs/screenshots/', import.meta.url);
async function api(path, body, expected = 200) {
  const response = await fetch(base + '/api' + path, body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
  });
  const text = await response.text();
  assert.equal(response.status, expected, `${path}: ${text}`);
  return text ? JSON.parse(text) : null;
}
async function until(predicate, label) {
  const end = Date.now() + 20000;
  while (Date.now() < end) { if (await predicate()) return; await new Promise(r => setTimeout(r, 100)); }
  throw new Error('Timed out: ' + label);
}
// A second run on an already-used database fails before any mutations or screenshots.
assert.deepEqual(await api('/refills'), [], 'Refusing to duplicate demo data. Start a NEW disposable database.');
assert.deepEqual(await api('/demo/employees/5/purchases'), []);
assert.deepEqual(await api('/demo/employees/5/reimbursements'), []);
const employees = await api('/demo/employees');
assert.deepEqual(employees.map(e => [e.id, e.name, e.officeId]), [
  [1, 'Ada', 1], [2, 'Erik', 1], [3, 'Liv', 1], [4, 'Anu', 2], [5, 'Ravi', 2], [6, 'Priya', 2],
], 'Expected the fictional exercise seed data only.');

const target = await (await fetch(debuggerBase + '/json/new?about:blank', { method: 'PUT' })).json();
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(resolve => ws.addEventListener('open', resolve, { once: true }));
let serial = 0; const pending = new Map(); const exceptions = []; const screenshots = [];
ws.addEventListener('message', event => {
  const message = JSON.parse(event.data);
  if (message.method === 'Runtime.exceptionThrown') exceptions.push(message.params.exceptionDetails.text);
  if (message.id) {
    const task = pending.get(message.id); pending.delete(message.id);
    if (message.error) task.reject(new Error(JSON.stringify(message.error))); else task.resolve(message.result);
  }
});
const command = (method, params = {}) => new Promise((resolve, reject) => {
  const id = ++serial; pending.set(id, { resolve, reject }); ws.send(JSON.stringify({ id, method, params }));
});
async function evaluate(expression) {
  const result = await command('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(JSON.stringify(result.exceptionDetails));
  return result.result.value;
}
const text = () => evaluate('document.querySelector("main")?.innerText || ""');
const wait = phrase => until(async () => (await text()).includes(phrase), phrase);
async function click(label) {
  await until(() => evaluate(`[...document.querySelectorAll('button')].some(b => b.textContent.trim() === ${JSON.stringify(label)} && !b.disabled)`), label);
  await evaluate(`[...document.querySelectorAll('button')].find(b=>b.textContent.trim()===${JSON.stringify(label)}).click()`);
}
async function input(selector, value) {
  await until(() => evaluate(`!!document.querySelector(${JSON.stringify(selector)})`), selector);
  await evaluate(`(() => {const e=document.querySelector(${JSON.stringify(selector)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));})()`);
}
async function employee(name) {
  await until(() => evaluate(`!![...document.querySelectorAll('#employee option')].find(o=>o.textContent.includes(${JSON.stringify(name)}))`), 'employee options');
  await evaluate(`(() => {const s=document.querySelector('#employee');s.value=[...s.options].find(o=>o.textContent.includes(${JSON.stringify(name)})).value;s.dispatchEvent(new Event('change',{bubbles:true}));})()`);
  await until(() => evaluate(`!document.querySelector('main').innerText.includes('Loading')`), 'employee loading');
}
async function nav(route) {
  const origin = await evaluate('performance.timeOrigin');
  await evaluate(`document.querySelector('nav a[href="/${route}"]').click()`);
  await until(() => evaluate(`!!document.querySelector('app-${route}') && !document.querySelector('main').innerText.includes('Loading')`), route);
  assert.equal(await evaluate('performance.timeOrigin'), origin, 'Router navigation must not reload');
}
async function reload(name, phrase) {
  const origin = await evaluate('performance.timeOrigin');
  await command('Page.reload');
  await until(() => evaluate(`performance.timeOrigin !== ${origin} && !!document.querySelector('h1')`), 'page reload');
  await employee(name); await wait(phrase);
}
async function capture(filename, phrase) {
  await wait(phrase);
  await until(() => evaluate(`!document.querySelector('main').innerText.includes('Loading') && !document.querySelector('select').disabled`), 'settled screenshot');
  assert.equal(await evaluate('document.querySelectorAll("[role=alert]").length'), 0);
  assert.equal(await evaluate('getComputedStyle(document.querySelector(".sidebar")).display'), 'flex');
  await evaluate('window.scrollTo(0,0); document.fonts.ready');
  const metrics = await command('Page.getLayoutMetrics');
  const png = await command('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true,
    clip: { x: 0, y: 0, width: 1440, height: Math.ceil(metrics.cssContentSize.height), scale: 1 } });
  await mkdir(output, { recursive: true });
  await writeFile(new URL(filename, output), Buffer.from(png.data, 'base64'));
  screenshots.push(filename); console.log('Captured', filename);
}

try {
  await command('Runtime.enable'); await command('Page.enable');
  await command('Emulation.setDeviceMetricsOverride', { width: 1440, height: 1100, deviceScaleFactor: 1, mobile: false });
  await command('Page.navigate', { url: base + '/dashboard' });
  await until(() => evaluate('!!document.querySelector("h1")'), 'app startup');
  await employee('Priya'); await wait('Available for purchase authorization');
  assert((await text()).includes('₹15,000.00'));
  await click('Report low or empty snacks'); await wait('An opportunity to help.');
  const refill = (await api('/refills'))[0]; assert.equal(refill.officeId, 2);
  assert.equal((await api('/offices/2/refills', { employeeId: 6 })).id, refill.id);
  await reload('Priya', 'An opportunity to help.');
  await capture('01-bengaluru-overview-open.png', 'An opportunity to help.');

  // Normal invitation dispatch endpoint; the worker may already have sent these.
  await api(`/refills/${refill.id}/invite`, {});
  assert((await api('/demo/employees/5/invitations')).some(i => i.state === 'Invited'));
  await employee('Ravi'); await nav('volunteer'); await wait('Accept invitation');
  await click('Accept invitation'); await wait('Confirm intention to purchase');
  await api(`/refills/${refill.id}/accept`, { employeeId: 4 }, 409);
  await reload('Ravi', 'Confirm intention to purchase');
  await input('#estimate', '1000.00');
  await capture('02-bengaluru-volunteer-reserved.png', 'You reserved this refill');

  await click('Confirm intention to purchase'); await wait('Actual expense');
  let budget = await api('/offices/2/budget');
  assert.equal(budget.reservedMinor, 100000); assert.equal(budget.spentMinor, 0); assert.equal(budget.availableMinor, 1400000);
  await nav('dashboard'); await reload('Ravi', 'Someone is taking care of it.');
  assert((await text()).includes('₹1,000.00'));
  assert.equal(await evaluate('document.querySelector(".counter-card").innerText.includes("Ravi")'), false);
  await capture('03-bengaluru-purchase-commitment.png', 'Purchasing');
  await nav('volunteer'); await input('#actual', '900.00');
  await click('Complete purchase'); await wait('No active refill opportunity.');
  budget = await api('/offices/2/budget');
  assert.equal(budget.spentMinor, 90000); assert.equal(budget.reservedMinor, 0); assert.equal(budget.availableMinor, 1410000);
  await nav('expenses'); await wait('No reimbursement claim yet.');
  await input('input[name="description"]', 'Mixed office snacks — fictional Bengaluru demonstration');
  await input('input[name="receipt"]', 'FICTIONAL-DEMO-BLR-SNACKS-001 (not a real receipt)');
  await click('Create claim'); await wait('Submit to simulated HR');
  await click('Submit to simulated HR'); await wait('Pending HR');
  const claims = await api('/demo/employees/5/reimbursements');
  assert.equal(claims.length, 1); assert.equal(claims[0].state, 'PendingHR');
  assert.equal(claims[0].amountMinor, 90000); assert.equal(claims[0].currency, 'INR');
  await reload('Ravi', 'Pending HR'); // Refresh resets identity to Ada; select Ravi again through the real UI.
  await wait('FICTIONAL-DEMO-BLR-SNACKS-001');
  assert((await text()).includes('₹900.00'));
  assert.deepEqual((await api('/demo/employees/5/reimbursements')).map(c => c.id), claims.map(c => c.id));
  assert.deepEqual(await api('/demo/employees/4/reimbursements'), []);
  await capture('04-bengaluru-expenses-pending-hr.png', 'Pending HR');
  // No fake immediate approval or payment. Verify the real timing/state guards.
  await api(`/demo/hr/reimbursements/${claims[0].id}/decision`, { decision: 'Approved' }, 409);
  await api(`/demo/hr/reimbursements/${claims[0].id}/decision`, { decision: 'Reimbursed' }, 409);

  await nav('hr'); await wait('Contribution summary');
  const report = await api(`/reports/monthly/${budget.month}/consolidated`);
  assert.equal(report.nativeTotals.find(o => o.officeId === 2).spentMinor, 90000);
  assert.equal(report.nativeTotals.find(o => o.officeId === 2).outstandingMinor, 0);
  assert.equal(report.spentMinor, 90000 * report.exchangeRate.numerator / report.exchangeRate.denominator);
  const board = await api('/demo/hr/contributions/leaderboard');
  assert.equal(board.find(e => e.employeeId === 5).points, 5);
  assert.equal(board.find(e => e.employeeId === 6).points, 1);
  assert((await text()).includes('₹900.00'));
  await capture('05-hr-monthly-spending-contributions.png', 'Ravi');

  await employee('Ada'); await nav('dashboard'); await wait('Available for purchase authorization');
  const nok = await api('/offices/1/budget'); assert.equal(nok.limitMinor, 600000); assert.equal(nok.availableMinor, 600000);
  assert((await text()).replace(/\s/g, '').includes('6000,00kr'));
  await capture('06-oslo-overview-nok-budget.png', 'Available for purchase authorization');
  assert.deepEqual(exceptions, [], 'No uncaught browser exceptions');
  console.log(JSON.stringify({ result: 'passed', month: budget.month, estimateMinor: 100000, actualMinor: 90000,
    availableMinor: budget.availableMinor, reservedMinor: budget.reservedMinor, claimState: claims[0].state,
    raviPoints: 5, priyaPoints: 1, screenshotDirectory: fileURLToPath(output), screenshots }, null, 2));
} finally {
  ws.close(); await fetch(debuggerBase + '/json/close/' + target.id).catch(() => {});
}
