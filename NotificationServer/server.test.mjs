import test from 'node:test';
import assert from 'node:assert/strict';
import { createNotificationServer, koreaDay, normalize } from './server.mjs';

test('Korean date boundary and source-scoped deduplication', () => {
  assert.equal(koreaDay('2026-09-16T15:00:00Z'), '2026-09-17');
  assert.equal(koreaDay('2026-09-16T14:59:59Z'), '2026-09-16');
  const n = { id: '1', source_id: 'smartlead', source_name: 'SmartLEAD', title: 'Notice', published_at: '2026-09-17T00:00:00+09:00' };
  assert.equal(normalize({ notifications: [n, n, { ...n, source_id: 'other' }] }).length, 2);
  assert.throws(() => normalize({ notifications: [{ ...n, published_at: '2026-09-17' }] }));
  assert.throws(() => normalize({ notifications: [{ ...n, url: 'javascript:alert(1)' }] }));
});

test('sample API, source filtering, unavailable source and browser isolation', async () => {
  const server = createNotificationServer({ mode: 'sample' });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  try {
    const response = await (await fetch(base + '/api/notifications')).json();
    assert.equal(response.notifications.length, 3);
    assert.ok(response.notifications.every(n => koreaDay(n.published_at) === koreaDay()));
    const filtered = await (await fetch(base + '/api/notifications?source=smartlead')).json();
    assert.equal(filtered.notifications.length, 1);
    assert.equal((await fetch(base + '/api/notifications', { headers: { Origin: 'https://example.com' } })).status, 403);
    assert.equal((await fetch(base + '/api/notifications', { method: 'POST' })).status, 405);
  } finally { await new Promise(resolve => server.close(resolve)); }
  const missing = createNotificationServer({ mode: 'file', file: 'missing-test-input.json' });
  await new Promise(resolve => missing.listen(0, '127.0.0.1', resolve));
  try { assert.equal((await fetch(`http://127.0.0.1:${missing.address().port}/api/notifications`)).status, 503); }
  finally { await new Promise(resolve => missing.close(resolve)); }
});
