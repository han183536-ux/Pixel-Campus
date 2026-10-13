import test from 'node:test';
import assert from 'node:assert/strict';
import { createSmartleadProvider, mapNotification, ConnectionError } from './smartlead.mjs';
import { createNotificationServer, normalize } from './server.mjs';

const now = () => Date.parse('2026-09-17T13:00:00+09:00');
const row = (id = 1, time = '2026-09-17T09:00:00+09:00') => ({
  id, useridto: 42, subject: '강의 &amp; 과제', fullmessagehtml: '<p>첫째</p><script>secret()</script><p>둘째</p>',
  timecreated: Date.parse(time) / 1000, contexturl: '/mod/assign/view.php?id=123', read: true
});
const site = { userid: 42, functions: [{ name: 'message_popup_get_popup_notifications' }] };
function mockProvider(responses) {
  const calls = [];
  return { calls, provider: createSmartleadProvider({ token: 'test-token-never-log', now, fetchImpl: async (url, options) => {
    calls.push({url, options});
    if (!responses.length) throw new Error('Unexpected request');
    const data = responses.shift();
    return {ok: true, json: async () => data};
  } }) };
}

test('private notifications paginate, honor KST midnight, preserve read status and use POST token', async () => {
  const first = Array.from({length: 100}, (_, i) => row(i + 1));
  const { provider, calls } = mockProvider([site, {notifications: first}, {notifications: [
    row(101, '2026-09-16T15:00:00Z'), row(102, '2026-09-16T14:59:59Z')
  ]}]);
  const data = await provider.load();
  assert.equal(data.notifications.length, 101);
  assert.equal(data.notifications[0].source_id, 'smartlead');
  assert.equal(data.notifications[0].title, '강의 & 과제');
  assert.equal(data.notifications[0].message, '첫째\n둘째');
  assert.equal(normalize(data)[0].is_read, true);
  assert.equal(calls[2].options.body.get('offset'), '100');
  assert.equal(calls[1].options.body.get('useridto'), '0');
  assert.equal(calls[0].options.method, 'POST');
  assert.equal(calls[0].options.body.get('wstoken'), 'test-token-never-log');
  assert.equal(calls[0].options.redirect, 'error');
  assert.ok(!calls[0].url.includes('test-token'));
  assert.equal(mapNotification({...row(), contexturl: 'javascript:alert(1)'}, 42).url, '');
  assert.equal(mapNotification({...row(), contexturl: ''}, 42).url, '');
  assert.notEqual(mapNotification(row(), 42).id, mapNotification(row(), 43).id);
});

test('missing token, rejected token, unsupported function, wrong recipient and invalid data fail clearly', async () => {
  await assert.rejects(createSmartleadProvider({token: ''}).load(), {code: 'not_configured'});
  await assert.rejects(mockProvider([{exception:'moodle_exception',errorcode:'invalidtoken',message:'secret'}]).provider.load(), {code:'invalidtoken'});
  await assert.rejects(mockProvider([{userid:42, functions:[]}]).provider.load(), {code:'wsfunctionnotavailable'});
  await assert.rejects(mockProvider([site, {notifications:[{...row(), useridto:7}]}]).provider.load(), {code:'invalid_response'});
  await assert.rejects(mockProvider([site, {notifications:[{...row(), timecreated:'oops'}]}]).provider.load(), {code:'invalid_response'});
  const empty = await mockProvider([site, {notifications:[]}]).provider.load();
  assert.deepEqual(empty.notifications, []);
});

test('connection status, failure backoff and retention distinguish errors from zero notifications', async () => {
  let clock = 100000, loads = 0;
  const server = createNotificationServer({mode:'smartlead', now:()=>clock, provider:{configured:true, load:async()=>{
    loads++;
    if (loads === 1) return {notifications:[mapNotification(row(),42)]};
    throw new ConnectionError('invalidtoken','다시 로그인하세요.');
  }}});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base=`http://127.0.0.1:${server.address().port}`;
  try {
    const get=async path=>(await fetch(base+path)).json();
    assert.equal((await get('/api/connection')).state,'waiting');
    assert.equal((await get('/api/notifications?today=false')).notifications.length,1);
    clock+=31000;
    const stale=await get('/api/notifications?today=false');
    assert.equal(stale.stale,true);
    assert.equal(stale.notifications.length,1);
    assert.equal(stale.message,'다시 로그인하세요.');
    assert.equal((await get('/api/connection')).state,'invalidtoken');
    await get('/api/notifications');
    assert.equal(loads,2);
  } finally {await new Promise(resolve=>server.close(resolve));}
  const missing=createNotificationServer({mode:'smartlead',provider:createSmartleadProvider({token:''})});
  await new Promise(resolve=>missing.listen(0,'127.0.0.1',resolve));
  try {
    const r=await fetch(`http://127.0.0.1:${missing.address().port}/api/notifications`);
    assert.equal(r.status,503); assert.equal((await r.json()).error,'not_configured');
  } finally {await new Promise(resolve=>missing.close(resolve));}
});
