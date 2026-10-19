import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';

const script = await readFile(new URL('../Assets/Notifications/Resources/SmartleadCapture.txt', import.meta.url), 'utf8');
const origin = 'https://smartlead.hallym.ac.kr';

async function capture({text = JSON.stringify({code: '100', html: '<div class="media-lists"></div>'}),
  url = origin + '/theme/coursemosv2/action.php', status = 200, loginHtml = false,
  lateMoodleResult = false, loggedIn = true, loginLink = false, schoolRows = [],
  historyRows = null, historyEmpty = false, historyLogin = false, bridge = 'windows'} = {}) {
  const events = [], requests = [];
  let resolveMoodle;
  const moodleResult = new Promise(resolve => { resolveMoodle = resolve; });
  const context = {
    location: {origin, href: origin + '/local/ubion/user/index.php'},
    window: {M: {cfg: {sesskey: 'test-session'}}, chrome: {webview: {
      postMessage: value => events.push(JSON.parse(value))
    }}},
    document: {body: {classList: {contains: name => loggedIn && name === 'loggedin'}},
      querySelector: selector => loginLink && selector.includes('/login/index.php') ? {} : null},
    URL, URLSearchParams, AbortController,
    setTimeout: () => 1, clearTimeout: () => {},
    fetch: async (path, options) => {
      requests.push({path, options});
      if (path === '/local/ubnotification/') return {
        ok: true, status: 200, url: origin + path, text: async () => '<html data-history-fixture></html>'
      };
      return {ok: status >= 200 && status < 300, status, url, text: async () => text};
    },
    DOMParser: class {
      parseFromString(html) {
        if (html.includes('data-history-fixture')) return {
          querySelector: selector => selector.includes('notloggedin') ? historyLogin ? {} : null :
            historyRows === null ? null : {
              textContent: historyEmpty ? '알림이 없습니다.' : '학교 알림 기록',
              querySelectorAll: () => historyRows,
              querySelector: () => null
            }
        };
        return {
          querySelectorAll: () => schoolRows,
          querySelector: selector => selector === '.media-lists' ? {textContent: ''} : loginHtml ? {} : null
        };
      }
    }
  };
  if (bridge === 'android' || bridge === 'both') context.window.Unity = {call: value => events.push(JSON.parse(value))};
  if (bridge === 'android' || bridge === 'missing') delete context.window.chrome;
  if (lateMoodleResult) context.require = (_modules, callback) => callback({call: () => [moodleResult]});
  runInNewContext(script, context);
  await new Promise(resolve => setImmediate(resolve));
  if (lateMoodleResult) {
    resolveMoodle({notifications: [], unreadcount: 0});
    await new Promise(resolve => setImmediate(resolve));
  }
  return {events, requests};
}

test('school notification request uses the required AJAX header and preserves session validation', async () => {
  const {events, requests} = await capture();
  assert.equal(requests.length, 2);
  const {path, options} = requests[0];
  assert.equal(path, '/theme/coursemosv2/action.php');
  assert.equal(options.method, 'POST');
  assert.equal(options.credentials, 'same-origin');
  assert.equal(options.headers['X-Requested-With'], 'XMLHttpRequest');
  assert.match(options.headers.Accept, /application\/json/);
  assert.equal(options.body.get('type'), 'userInfoNotifications');
  assert.equal(options.body.get('sesskey'), 'test-session');
  assert.deepEqual(events.filter(e => e.source !== 'school_history' && e.kind !== 'session').map(e => [e.kind, e.source, e.total]), [
    ['notifications', 'school_api', 0], ['messages', 'school_api', 0]
  ]);
});

test('Android Unity.call delivers the same authenticated notification packets as Windows without duplicate delivery', async () => {
  const windows = await capture();
  const android = await capture({bridge: 'android'});
  const both = await capture({bridge: 'both'});
  assert.deepEqual(android.events, windows.events);
  assert.deepEqual(both.events, windows.events);
  assert.ok(android.events.every(event => event.nonce && event.page.startsWith(origin)));
  const anonymous = await capture({bridge: 'android', loggedIn: false});
  assert.equal(anonymous.events[0].kind, 'authentication');
  const unavailable = await capture({bridge: 'missing'});
  assert.equal(unavailable.events.length, 0);
});

test('HTML and malformed responses report failure without reporting an empty school inbox', async () => {
  for (const text of ['\r\n<!DOCTYPE html><html>Ajax 전용 페이지 입니다.</html>', 'broken-json']) {
    const {events: allEvents} = await capture({text});
    const events = allEvents.filter(e => e.source === 'school');
    assert.equal(events.length, 1);
    assert.equal(events[0].kind, 'api_status');
    assert.equal(events[0].source, 'school');
    assert.doesNotMatch(events[0].error, /Unexpected token/);
  }
  const {events} = await capture({status: 503});
  assert.match(events.find(e => e.source === 'school').error, /HTTP 503/);
});

test('expired school sessions request authentication and suppress late Moodle zero counts', async () => {
  for (const options of [
    {url: origin + '/login/index.php'},
    {text: '<!DOCTYPE html><form action="/login/index.php"></form>', loginHtml: true},
    {text: JSON.stringify({code: '399', msg: '로그인 필요'})}
  ]) {
    const {events} = await capture({...options, lateMoodleResult: true});
    assert.deepEqual(events.filter(e => e.kind !== 'session').map(e => e.kind), ['authentication']);
    assert.equal(events.find(e => e.kind === 'authentication').error, 'login_required');
  }
});

test('anonymous or unconfirmed pages cannot report the school endpoint empty list as a logged-in inbox', async () => {
  for (const options of [{loggedIn: false}, {loginLink: true}]) {
    const {events, requests} = await capture(options);
    assert.equal(requests.length, 0);
    assert.deepEqual(events.filter(e => e.kind !== 'session').map(e => e.kind), ['authentication']);
  }
});

test('course identity uses explicit metadata or a school course link, never an activity ID or a sender guess', async () => {
  const row = (attributes, href) => ({
    getAttribute: name => attributes[name] ?? null,
    matches: () => false,
    classList: {contains: () => false},
    textContent: '과제 안내',
    querySelector: selector => selector === '.media-body-message, .media-body' ? {textContent: '과제 안내'} : null,
    querySelectorAll: () => href ? [{getAttribute: () => href}] : []
  });
  const schoolRows = [
    row({'data-courseid': '42', 'data-coursename': '프로그래밍'}, '/mod/assign/view.php?id=999'),
    row({}, '/course/view.php?id=57'),
    row({}, '/mod/forum/view.php?id=123'),
    row({}, 'https://another-school.test/course/view.php?id=88')
  ];
  const {events, requests} = await capture({schoolRows});
  const items = events.find(e => e.kind === 'notifications').items;
  assert.deepEqual(items.map(item => [item.courseId, item.courseName]), [
    [42, '프로그래밍'], [57, ''], [0, ''], [0, '']
  ]);
  assert.deepEqual(requests.map(r => r.path), ['/theme/coursemosv2/action.php', '/local/ubnotification/']);
});

function historyRow(id, read, title) {
  const attributes = {'data-id': String(id), 'data-read': read ? '1' : '0'};
  return {
    getAttribute: name => attributes[name] ?? null,
    matches: () => false,
    textContent: title,
    querySelector: selector => selector === '.media-body-message, .media-body' ? {textContent: title} : null,
    querySelectorAll: () => []
  };
}

test('notification details retain longer body text and only expose school original links', async () => {
  const body = '상세 공지 내용 '.repeat(100);
  const makeRow = href => {
    const row = historyRow(12, false, body);
    const originalQuery = row.querySelector;
    row.querySelector = selector => selector === 'a.move_link[href], a[href]' ?
      {getAttribute: () => href} : originalQuery(selector);
    return row;
  };
  const {events} = await capture({historyRows: [
    makeRow('/mod/forum/discuss.php?d=12'),
    makeRow('https://other.example/notice'),
    makeRow('javascript:alert(1)'),
    makeRow('#')
  ]});
  const items = events.find(e => e.source === 'school_history' && e.kind === 'notifications').items;
  assert.equal(items[0].body, body.trim());
  assert.equal(items[0].title.length, 200);
  assert.equal(items[0].url, origin + '/mod/forum/discuss.php?d=12');
  assert.ok(items.slice(1).every(item => item.url === ''));
});

test('large detail previews stay inside the Unity bridge limit and report partial history', async () => {
  const {events} = await capture({historyRows: Array.from({length: 50}, (_, i) =>
    historyRow(i + 1, false, '긴 본문 '.repeat(500)))});
  const result = events.find(e => e.source === 'school_history' && e.kind === 'notifications');
  assert.ok(result.items.length > 0 && result.items.length < 50);
  assert.equal(result.historyLimited, true);
  assert.equal(result.total, 50);
  assert.ok(JSON.stringify(result).length <= 60000);
  assert.ok(result.items.every(item => item.body.length <= 2000));
});

test('history page preserves both read and unread records even when the user-menu inbox is empty', async () => {
  const {events, requests} = await capture({
    historyRows: [historyRow(1, true, '이미 읽은 과제 안내'), historyRow(2, false, '새 공지')],
    lateMoodleResult: true
  });
  const results = events.filter(e => e.kind === 'notifications');
  const latest = results.at(-1);
  assert.equal(latest.source, 'school_history');
  assert.equal(latest.total, 2);
  assert.equal(latest.count, 1);
  assert.deepEqual(latest.items.map(e => [e.title, e.read, e.readKnown]), [
    ['이미 읽은 과제 안내', true, true], ['새 공지', false, true]
  ]);
  assert.equal(requests[1].path, '/local/ubnotification/');
  assert.equal(requests[1].options.method, undefined); // Fetch defaults to GET of the list, not an item.
});

test('all-read history remains visible with an unread count of zero', async () => {
  const {events} = await capture({historyRows: [historyRow(7, true, '읽은 알림')]});
  const result = events.find(e => e.kind === 'notifications' && e.source === 'school_history');
  assert.equal(result.total, 1);
  assert.equal(result.count, 0);
  assert.equal(result.items[0].read, true);
});

test('unrecognized history markup reports failure while an explicit empty list reports zero', async () => {
  const failed = await capture({historyRows: []});
  assert.ok(failed.events.some(e => e.kind === 'api_status' && e.source === 'school_history'));
  assert.ok(!failed.events.some(e => e.kind === 'notifications' && e.source === 'school_history'));
  const empty = await capture({historyRows: [], historyEmpty: true});
  const result = empty.events.find(e => e.kind === 'notifications' && e.source === 'school_history');
  assert.equal(result.total, 0);
});

test('school history identifies the displayed course name and section code without treating the code as a Moodle ID', async () => {
  const {events} = await capture({historyRows: [
    historyRow(1, true, '소프트웨어캡스톤디자인 (603103-01) - 강의 개요4시간전체 공지사항이 등록되었습니다.'),
    historyRow(2, true, '3차원그래픽스 (603147-02) -7주차 [10월13일 - 10월19일]5시간전체'),
    historyRow(3, true, '과목명 (특강) (603103-02) – 새 공지'),
    historyRow(4, true, '개인 안내 - 강의 개요'),
    historyRow(5, true, '접수 안내 (20261013) - 등록 완료')
  ]});
  const items = events.find(e => e.source === 'school_history' && e.kind === 'notifications').items;
  assert.deepEqual(items.slice(0, 3).map(e => [e.courseName, e.courseCode, e.courseId, e.title]), [
    ['소프트웨어캡스톤디자인', '603103-01', 0, '강의 개요4시간전체 공지사항이 등록되었습니다.'],
    ['3차원그래픽스', '603147-02', 0, '7주차 [10월13일 - 10월19일]5시간전체'],
    ['과목명 (특강)', '603103-02', 0, '새 공지']
  ]);
  assert.ok(items.slice(3).every(e => e.courseName === '' && e.courseCode === ''));
});

test('confirmed login emits a session event before notification retrieval, including an empty inbox', async () => {
  const {events} = await capture();
  assert.equal(events[0].kind, 'session');
  assert.equal(events[0].authenticated, true);
  assert.ok(events.some(e => e.kind === 'notifications' && e.total === 0));
  const anonymous = await capture({loggedIn: false});
  assert.ok(!anonymous.events.some(e => e.kind === 'session'));
});


