import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {runInNewContext} from 'node:vm';

const script = await readFile(new URL('../Assets/Notifications/Resources/SmartleadCoursework.txt', import.meta.url), 'utf8');
const origin = 'https://smartlead.hallym.ac.kr';
const emptyDoc = {body: {textContent: ''}, querySelector: () => null, querySelectorAll: () => []};
const cell = textContent => ({textContent, querySelector: () => null});
const statusDoc = (status, due = '', extension = '') => ({
  ...emptyDoc,
  querySelectorAll: selector => selector.includes('submissionstatustable') ? [
    ['제출 상태', status], ['마감일', due], ['연장 기한', extension]
  ].map(pair => ({querySelectorAll: () => pair.map(cell)})) : []
});
async function capture({api, pages = {}, loggedIn = true, stallBody = false, bridge = 'windows'} = {}) {
  const events = [], requests = [];
  const context = {
    location: {origin, href: origin + '/local/ubion/user/index.php'},
    window: {M: {cfg: {sesskey: 'test', userid: loggedIn ? 42 : 0}},
      chrome: {webview: {postMessage: value => events.push(JSON.parse(value))}}},
    document: {body: {classList: {contains: name => name === (loggedIn ? 'loggedin' : 'notloggedin')}}, querySelector: () => null},
    URL, AbortController, setTimeout: stallBody ? callback => setTimeout(callback, 5) : () => 1,
    clearTimeout: stallBody ? clearTimeout : () => {},
    DOMParser: class {parseFromString(value) {return pages[value] || {...emptyDoc, body: {textContent: String(value).replace(/<[^>]*>/g, '')}};}},
    fetch: async (path, options) => {
      requests.push({path, options});
      if (path.startsWith('/lib/ajax/')) {
        const call = JSON.parse(options.body)[0];
        return {ok: true, url: origin + path, text: async () => {
          if (stallBody) return new Promise((_, reject) => options.signal.addEventListener('abort', () => reject(Error('body timeout'))));
          try { return JSON.stringify([{data: await api(call)}]); }
          catch { return JSON.stringify([{error: true}]); }
        }};
      }
      const url = new URL(path, origin);
      const key = url.pathname + url.search;
      const page = pages[key];
      return {ok: !!page, url: url.href, text: async () => key};
    }
  };
  if (bridge === 'android' || bridge === 'both') context.window.Unity = {call: value => events.push(JSON.parse(value))};
  if (bridge === 'android' || bridge === 'missing') delete context.window.chrome;
  runInNewContext(script, context);
  for (let i = 0; i < 30; i++) await new Promise(resolve => setImmediate(resolve));
  if (stallBody) await new Promise(resolve => setTimeout(resolve, 30));
  return {events, requests};
}
const roster = [{id: 7, fullname: '프로그래밍 (123456-01)'}, {id: 8, fullname: '그래픽스'}];
const assignment = (id, cmid, name = '과제') => ({id, cmid, name, duedate: 1800000000, intro: '<p>과제 설명</p>'});

test('Android bridge streams courses, assignments and deadlines with the same protocol and only one delivery', async () => {
  const api = ({methodname}) => {
    if (methodname.includes('timeline')) return {courses: [roster[0]], nextoffset: 0};
    if (methodname === 'mod_assign_get_assignments') return {courses: [{id: 7, assignments: [assignment(11, 91)]}]};
    if (methodname === 'mod_assign_get_submission_status') return {lastattempt: {submission: {status: 'new'}}};
    return {events: []};
  };
  const windows = await capture({api});
  for (const bridge of ['android', 'both']) {
    const result = await capture({api, bridge});
    assert.deepEqual(result.events, windows.events);
    const task = result.events.find(e => e.kind === 'assignment');
    assert.equal(task.items[0].dueAt, 1800000000);
    assert.ok(task.nonce && task.page.startsWith(origin));
  }
  const anonymous = await capture({api, bridge: 'android', loggedIn: false});
  assert.equal(anonymous.requests.length, 0);
});

test('all enrolled courses are paginated independently of notifications, including empty courses', async () => {
  const {events, requests} = await capture({api: ({methodname, args}) => {
    if (methodname.includes('timeline')) return args.offset === 0 ? {courses: [roster[0]], nextoffset: 1} : {courses: [roster[1]], nextoffset: 0};
    if (methodname === 'mod_assign_get_assignments') return {courses: roster.map(c => ({id: c.id, assignments: []}))};
    throw Error('unexpected API');
  }});
  assert.deepEqual(events.filter(e => e.kind === 'course').map(e => e.items[0].courseId), [7, 8]);
  assert.equal(events.find(e => e.kind === 'course').items[0].courseCode, '123456-01');
  assert.equal(events.find(e => e.kind === 'courses_complete').historyLimited, false);
  assert.equal(events.filter(e => e.kind === 'assignments_complete' && e.found).length, 2);
  assert.ok(!events.some(e => e.kind === 'assignment'));
  assert.ok(requests.every(r => !r.path.includes('/login/')));
});

test('submission states and personal extension deadlines are preserved without confusing module and assignment IDs', async () => {
  const {events, requests} = await capture({api: ({methodname, args}) => {
    if (methodname.includes('timeline')) return {courses: [roster[0]], nextoffset: 0};
    if (methodname === 'mod_assign_get_assignments') return {courses: [{id: 7, assignments: [assignment(11, 91), assignment(12, 92), assignment(13, 93)]}]};
    return {lastattempt: {submission: {status: args.assignid === 11 ? 'submitted' : args.assignid === 12 ? 'draft' : 'new'}, extensionduedate: args.assignid === 12 ? 1800100000 : 0}};
  }});
  const items = events.filter(e => e.kind === 'assignment').map(e => e.items[0]);
  assert.deepEqual(items.map(i => [i.submitted, i.submissionKnown]), [[true, true], [false, true], [false, true]]);
  assert.equal(items[1].dueAt, 1800100000);
  assert.equal(items[1].body, '과제 설명');
  assert.equal(items[1].url, origin + '/mod/assign/view.php?id=92');
  assert.deepEqual(requests.filter(r => JSON.parse(r.options.body)[0].methodname === 'mod_assign_get_submission_status')
    .map(r => JSON.parse(r.options.body)[0].args.assignid), [11, 12, 13]);
});

test('unavailable assignment APIs fall back to school pages and parse Korean dates in Korea time', async () => {
  const link = {textContent: '보고서', getAttribute: () => '/mod/assign/view.php?id=91'};
  const root = {textContent: '과제 목록', querySelector: () => ({querySelectorAll: () => []}), querySelectorAll: () => [link]};
  const {events} = await capture({api: ({methodname}) => {
    if (methodname.includes('timeline')) return {courses: [roster[0]], nextoffset: 0};
    throw Error('not AJAX-enabled');
  }, pages: {
    '/mod/assign/index.php?id=7': {...emptyDoc, querySelector: selector => selector === '#region-main' ? root : null},
    '/mod/assign/view.php?id=91': statusDoc('아직 제출하지 않았습니다.', '2026년 10월 23일 금요일 오후 11:59', '2026년 10월 24일 토요일 오전 12:30')
  }});
  // Full original URLs are also used by assignmentPage.
  const complete = events.find(e => e.kind === 'assignments_complete');
  assert.equal(complete.found, true);
  const item = events.find(e => e.kind === 'assignment').items[0];
  assert.equal(item.submitted, false);
  assert.equal(item.submissionKnown, true);
  assert.equal(item.dueAt, Date.parse('2026-10-24T00:30:00+09:00') / 1000);
});

test('unknown submission status and failed lists are never reported as confirmed empty coursework', async () => {
  const {events} = await capture({api: ({methodname}) => {
    if (methodname.includes('timeline')) return {courses: roster, nextoffset: 0};
    if (methodname === 'mod_assign_get_assignments') return {courses: [{id: 7, assignments: [assignment(11, 91)]}, {id: 8, assignments: []}]};
    throw Error('unavailable');
  }});
  assert.equal(events.find(e => e.kind === 'assignment').items[0].submissionKnown, false);
  assert.equal(events.find(e => e.kind === 'assignments_complete' && e.courseId === 7).historyLimited, true);
  const failed = await capture({api: () => {throw Error('unavailable');}});
  assert.ok(failed.events.some(e => e.kind === 'coursework_status'));
  assert.ok(!failed.events.some(e => e.kind === 'courses_complete'));
  const anonymous = await capture({loggedIn: false});
  assert.equal(anonymous.requests.length, 0);
});

test('calendar deadlines remain available when roster and assignment endpoints are blocked', async () => {
  const event = {id: 81, name: '보고서 마감', description: '<p>설명</p>', modulename: 'assign', eventtype: 'due',
    timestart: 1800000000, course: roster[0], action: {url: origin + '/mod/assign/view.php?id=91'}};
  const {events} = await capture({api: ({methodname}) => {
    if (methodname === 'core_calendar_get_action_events_by_timesort') return {events: [event,
      {...event, id: 82, eventtype: 'open'},
      {...event, id: 83, modulename: 'quiz'},
      {...event, id: 84, action: {url: 'https://other.example/mod/assign/view.php?id=99'}}]};
    throw Error('API blocked');
  }});
  const schedules = events.filter(e => e.kind === 'schedule');
  assert.equal(schedules.length, 1);
  assert.equal(schedules[0].items[0].title, '보고서 마감');
  assert.equal(schedules[0].items[0].courseId, 7);
  assert.equal(schedules[0].items[0].dueAt, 1800000000);
  assert.equal(schedules[0].items[0].submissionKnown, false);
  assert.ok(events.some(e => e.kind === 'coursework_status'));
  assert.equal(events.at(-1).kind, 'coursework_complete');
});

test('calendar pages stream deadlines before coursework completion and report failed calendars explicitly', async () => {
  const {events} = await capture({api: ({methodname, args}) => {
    if (methodname === 'core_calendar_get_action_events_by_timesort') return {events: Array.from({length: args.aftereventid ? 1 : 50}, (_, i) => ({
      id: args.aftereventid + i + 1, name: '과제 ' + i, modulename: 'assign', eventtype: 'due', course: roster[0],
      timestart: 1800000000 + i, action: {url: origin + '/mod/assign/view.php?id=' + (args.aftereventid + i + 1)}
    }))};
    if (methodname.includes('timeline')) return {courses: [], nextoffset: 0};
    return {courses: []};
  }});
  assert.equal(events.filter(e => e.kind === 'schedule').length, 51);
  assert.ok(events.findIndex(e => e.kind === 'schedule') < events.findIndex(e => e.kind === 'coursework_complete'));
  assert.equal(events.find(e => e.kind === 'schedule_complete').found, true);
  const failed = await capture({api: () => {throw Error('unavailable');}});
  assert.equal(failed.events.find(e => e.kind === 'schedule_complete').found, false);
  assert.equal(failed.events.at(-1).kind, 'coursework_complete');
});

test('a stalled response body times out instead of leaving coursework permanently loading', async () => {
  const {events} = await capture({api: () => ({}), stallBody: true});
  assert.equal(events.find(e => e.kind === 'schedule_complete').found, false);
  assert.ok(events.some(e => e.kind === 'coursework_status'));
  assert.equal(events.at(-1).kind, 'coursework_complete');
});

test('live Coursemos layout reads course-info-container and role=main assignment columns without region-main', async () => {
  const courseLink = {textContent: '일본문화콘텐츠산책', getAttribute: () => '/course/view.php?id=25987', querySelector: () => null};
  const taskLink = {textContent: '감상문', getAttribute: () => '/mod/assign/view.php?id=696730'};
  const headers = ['주', '과제', '종료 일시', '제출', '성적'].map(cell);
  const cells = ['6주차', '감상문', '2026-10-13 18:00', '제출 완료', '-'].map(cell);
  const table = {querySelectorAll: selector => selector.includes('thead') ? headers : [{
    querySelector: () => taskLink, querySelectorAll: () => cells
  }]};
  const root = {textContent: '과제', querySelectorAll: () => [taskLink], querySelector: selector => selector === 'table.generaltable' ? table : null};
  const {events, requests} = await capture({api: () => {throw Error('external_functions missing');}, pages: {
    '/my/': {...emptyDoc, querySelectorAll: selector => selector.includes('.course-info-container') ? [courseLink] : []},
    '/mod/assign/index.php?id=25987': {...emptyDoc, querySelector: selector => selector === '[role="main"]' ? root : null}
  }});
  assert.equal(events.find(e => e.kind === 'course').items[0].courseId, 25987);
  const task = events.find(e => e.kind === 'assignment').items[0];
  assert.equal(task.title, '감상문');
  assert.equal(task.submissionKnown, true);
  assert.equal(task.submitted, true);
  assert.equal(task.dueAt, Date.parse('2026-10-13T18:00:00+09:00') / 1000);
  assert.ok(!requests.some(r => r.path.includes('/mod/assign/view.php')));
});

test('Coursemos submission-detail label and numeric deadline are parsed when the index lacks status columns', async () => {
  const link = {textContent: '보고서', getAttribute: () => '/mod/assign/view.php?id=91'};
  const root = {textContent: '과제', querySelectorAll: () => [link], querySelector: () => ({querySelectorAll: () => []})};
  const doc = {...emptyDoc, querySelectorAll: () => [
    ['제출 여부', '미제출'], ['종료 일시', '2026-10-25 23:59'], ['마감까지 남은 기한', '3일 2시간']
  ].map(pair => ({querySelectorAll: () => pair.map(cell)}))};
  const {events} = await capture({api: ({methodname}) => {
    if (methodname.includes('timeline')) return {courses: [roster[0]], nextoffset: 0};
    throw Error('blocked');
  }, pages: {'/mod/assign/index.php?id=7': {...emptyDoc, querySelector: selector => selector === '#region-main' ? root : null},
    '/mod/assign/view.php?id=91': doc}});
  const task = events.find(e => e.kind === 'assignment').items[0];
  assert.equal(task.submissionKnown, true);
  assert.equal(task.submitted, false);
  assert.equal(task.dueAt, Date.parse('2026-10-25T23:59:00+09:00') / 1000);
});
