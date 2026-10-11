import assert from 'node:assert/strict';
import fs from 'node:fs';
import { randomUUID } from 'node:crypto';

// Run against a disposable/local development database, never a live hotel.
const base = process.env.TEST_API_URL || 'http://localhost:5080/api';
const envFile = new URL('../.env', import.meta.url);
const localEnv = fs.existsSync(envFile)
  ? Object.fromEntries(fs.readFileSync(envFile, 'utf8').split(/\r?\n/)
      .filter(line => line && !line.startsWith('#') && line.includes('='))
      .map(line => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1)]))
  : {};
const adminPassword = process.env.TEST_ADMIN_PASSWORD || localEnv.ADMIN_PASSWORD;
assert.ok(adminPassword, 'Set TEST_ADMIN_PASSWORD or run scripts/start-local.ps1 first.');
let passed = 0;

async function request(route, { method = 'GET', token, body, key } = {}, retry = 0) {
  const headers = { 'Content-Type': 'application/json' };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (key) headers['Idempotency-Key'] = key;
  const response = await fetch(base + route, {
    method, headers, body: body === undefined ? undefined : JSON.stringify(body)
  });
  if (response.status === 429 && retry < 3) {
    await new Promise(resolve => setTimeout(resolve, 1100 * (retry + 1)));
    return request(route, { method, token, body, key }, retry + 1);
  }
  const text = await response.text();
  let data;
  try { data = text ? JSON.parse(text) : null; }
  catch { throw new Error(`${method} ${route} returned non-JSON (${response.status})`); }
  return { status: response.status, data };
}

function check(name, assertion) {
  assertion();
  passed++;
  console.log(`PASS ${name}`);
}
function dateAfter(days) {
  return new Date(Date.now() + days * 86400000).toISOString().slice(0, 10);
}
async function register(suffix) {
  const response = await request('/auth/register', {
    method: 'POST', body: {
      email: `test-${suffix}-${randomUUID()}@example.test`,
      password: `Vesper!${randomUUID()}9`, firstName: 'API', lastName: 'Test', phoneNumber: '+15555550123'
    }
  });
  assert.ok([200, 201].includes(response.status), JSON.stringify(response.data));
  assert.ok(response.data.accessToken);
  return response.data.accessToken;
}

const health = await request('/health');
check('API and database health', () => assert.equal(health.status, 200));
const rooms = await request('/catalog?kind=Room');
check('seeded rooms use distinct covers and galleries', () => {
  assert.equal(rooms.status, 200, JSON.stringify(rooms.data));
  const showcaseRooms = ['The Vesper Suite', 'Panorama Residence', 'The Penthouse']
    .map(name => rooms.data.find(room => room.name === name));
  assert.ok(showcaseRooms.every(Boolean));
  assert.equal(new Set(showcaseRooms.map(room => room.imageUrl)).size, 3);
  assert.ok(showcaseRooms.every(room => room.galleryImages.length >= 3));
});
const adminLogin = await request('/auth/login', {
  method: 'POST', body: { email: localEnv.ADMIN_EMAIL || 'admin@vesper.local', password: adminPassword }
});
assert.equal(adminLogin.status, 200, JSON.stringify(adminLogin.data));
const admin = adminLogin.data.accessToken;
const user = await register('owner');
const otherUser = await register('other');

const unauthorizedAdmin = await request('/admin/resources', { token: user });
check('admin endpoints require role', () => assert.equal(unauthorizedAdmin.status, 403));
assert.equal((await request('/bookings/mine')).status, 401);

async function createResource(kind, fields = {}) {
  const response = await request('/admin/resources', { method: 'POST', token: admin, body: {
    kind, name: `Test ${kind} ${randomUUID().slice(0, 8)}`, description: 'Disposable integration-test inventory.',
    tagline: 'Integration fixture', basePrice: 250, priceUnit: kind === 'Room' ? 'night' : kind === 'Activity' ? 'person' : 'hour',
    maxGuests: 4, imageUrl: '/images/suite.jpg', features: ['Test fixture'], ...fields
  }});
  assert.equal(response.status, 201, JSON.stringify(response.data));
  return response.data;
}
async function addUnit(resource) {
  const r = await request(`/admin/resources/${resource.id}/units`, {
    method: 'POST', token: admin, body: { name: `UNIT-${randomUUID().slice(0, 8)}` }
  });
  assert.equal(r.status, 201, JSON.stringify(r.data));
}
function hold(resource, extra = {}) {
  return { kind: resource.kind, resourceId: resource.id, start: dateAfter(7), end: dateAfter(9),
    guests: 2, expectedOfferVersion: resource.offerVersion, ...extra };
}
async function createHold(body, key = randomUUID(), token = user) {
  return request('/bookings', { method: 'POST', token, body, key });
}
async function cancel(id, token = user) {
  return request(`/bookings/${id}/cancel`, { method: 'POST', token, body: { reason: 'Integration test cleanup' } });
}

const room = await createResource('Room');
await addUnit(room);
const roomRequest = hold(room);
const key = randomUUID();
const created = await createHold(roomRequest, key);
check('room hold reserves physical inventory', () => {
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.equal(created.data.status, 'Pending');
  assert.equal(created.data.totalAmount, 500);
});
const booking = created.data;
const replay = await createHold(roomRequest, key);
check('retry returns original booking', () => assert.equal(replay.data.id, booking.id));
const altered = await createHold({ ...roomRequest, guests: 3 }, key);
check('same idempotency key rejects different payload', () => assert.equal(altered.status, 409));
const conflict = await createHold(roomRequest);
check('overlapping room cannot be booked twice', () => assert.equal(conflict.status, 409));
const inaccessible = await request(`/bookings/${booking.id}`, { token: otherUser });
check('another user cannot read booking', () => assert.ok([403, 404].includes(inaccessible.status)));
const forbidden = await request(`/bookings/${booking.id}/confirm`, { method: 'POST', token: otherUser });
check('another user cannot confirm booking', () => assert.ok([403, 404].includes(forbidden.status)));

const priceChange = await request(`/admin/resources/${room.id}`, { method: 'PUT', token: admin,
  body: { ...room, basePrice: 700, expectedVersion: room.offerVersion }
});
assert.equal(priceChange.status, 200, JSON.stringify(priceChange.data));
const confirmed = await request(`/bookings/${booking.id}/confirm`, { method: 'POST', token: user });
check('confirmation preserves held price', () => {
  assert.equal(confirmed.status, 200, JSON.stringify(confirmed.data));
  assert.equal(confirmed.data.totalAmount, 500);
  assert.equal(confirmed.data.status, 'Confirmed');
});
assert.equal((await cancel(booking.id)).status, 200);
const staleOffer = await createHold(roomRequest);
check('new booking rejects stale offer version', () => assert.equal(staleOffer.status, 409));
const currentRoom = priceChange.data;
const attempts = await Promise.all(Array.from({ length: 16 }, () => createHold(hold(currentRoom))));
const successes = attempts.filter(r => r.status === 201);
check('16 concurrent requests allocate the last room once', () => {
  assert.equal(successes.length, 1, JSON.stringify(attempts.map(r => ({ status: r.status, error: r.data?.code }))));
  assert.ok(attempts.every(r => r.status === 201 || r.status === 409));
});
for (const success of successes) await cancel(success.data.id);

const activity = await createResource('Activity', { basePrice: 90 });
const sessionResponse = await request(`/admin/resources/${activity.id}/sessions`, { method: 'POST', token: admin, body: {
  startUtc: `${dateAfter(8)}T18:00:00Z`, endUtc: `${dateAfter(8)}T20:00:00Z`, capacity: 4, price: 90
}});
assert.equal(sessionResponse.status, 201, JSON.stringify(sessionResponse.data));
const session = sessionResponse.data;
const latestActivity = (await request(`/catalog/${activity.id}`)).data;
const activityRequest = hold(latestActivity, { sessionId: session.id, start: session.startUtc, end: session.endUtc });
const seatAttempts = await Promise.all(Array.from({ length: 8 }, () => createHold(activityRequest)));
const seatSuccesses = seatAttempts.filter(r => r.status === 201);
check('concurrent activity bookings respect capacity', () => {
  assert.equal(seatSuccesses.length, 2, JSON.stringify(seatAttempts.map(r => ({ status: r.status, error: r.data?.code }))));
  assert.ok(seatAttempts.every(r => [201, 409].includes(r.status)));
});
for (const success of seatSuccesses) await cancel(success.data.id);

const car = await createResource('Transportation');
await addUnit(car);
const carRequest = hold(car, { start: `${dateAfter(9)}T18:00:00Z`, end: `${dateAfter(9)}T20:00:00Z`,
  pickupLocation: 'Vesper Hotel', dropoffLocation: 'Airport' });
const firstCar = await createHold(carRequest);
assert.equal(firstCar.status, 201, JSON.stringify(firstCar.data));
const turnaround = await createHold({ ...carRequest, start: `${dateAfter(9)}T20:15:00Z`, end: `${dateAfter(9)}T21:15:00Z` });
check('vehicle turnaround buffer is enforced', () => assert.equal(turnaround.status, 409));
const afterBuffer = await createHold({ ...carRequest, start: `${dateAfter(9)}T20:30:00Z`, end: `${dateAfter(9)}T21:30:00Z` });
check('vehicle becomes available at end of turnaround', () => assert.equal(afterBuffer.status, 201, JSON.stringify(afterBuffer.data)));
await cancel(firstCar.data.id);
if (afterBuffer.status === 201) await cancel(afterBuffer.data.id);

const invalidGuests = await createHold({ ...carRequest, guests: 99 });
check('server validates passenger limit', () => assert.ok([400, 422].includes(invalidGuests.status)));
const dashboard = await request('/admin/dashboard', { token: admin });
check('dashboard aggregates stored bookings', () => {
  assert.equal(dashboard.status, 200, JSON.stringify(dashboard.data));
  assert.ok(dashboard.data.totalBookings >= 1);
});
const audit = await request('/admin/audit', { token: admin });
check('admin changes have audit history', () => {
  assert.equal(audit.status, 200);
  assert.ok(audit.data.totalCount > 0);
});
for (const resource of [currentRoom, latestActivity, car]) {
  const latest = (await request('/admin/resources', { token: admin })).data.find(r => r.id === resource.id);
  await request(`/admin/resources/${resource.id}`, { method: 'PUT', token: admin,
    body: { ...latest, isActive: false, expectedVersion: latest.offerVersion }
  });
}
console.log(`\n${passed} checks passed against ${base}`);
