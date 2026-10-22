import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {runInNewContext} from 'node:vm';

const script = await readFile(new URL('../Assets/Notifications/Resources/SmartleadLogin.txt', import.meta.url), 'utf8');
const logoutScript = await readFile(new URL('../Assets/Notifications/Resources/SmartleadLogout.txt', import.meta.url), 'utf8');
function login({origin = 'https://smartlead.hallym.ac.kr', path = '/login/index.php', saved = 'student-123',
  remember = true, typed = '', bridge = 'windows', missingForm = false,
  automatic = false, savedPassword = '', autoSubmit = false, requirePasswordEntry = false, prefilledPassword = '',
  existingSession = false} = {}) {
  const packets = [], readyPackets = [], elements = [], events = [];
  const submit = {parentNode: {insertBefore(element, anchor) { assert.equal(anchor, submit); elements.push(element); }}};
  const password = {value: prefilledPassword, dispatchEvent() {}};
  const form = {appendChild: element => elements.push(element), addEventListener: (name, fn) => {
      events[name] = () => { const event = {submitter: submit, prevented: false, preventDefault() { this.prevented = true; }, stopImmediatePropagation() {}};
        fn(event); if (!event.prevented) form.submissions++; };
    }, submissions: 0,
    querySelector: selector => selector === 'input[type="password"]' ? password : submit, requestSubmit() { events.submit(); }};
  const username = {value: typed, form, dispatchEvent() {}};
  const document = {
    body: {textContent: existingSession ? '이미 사용자로 로그인 하였습니다. 다른 사용자로 로그인하기전에 로그아웃 해야 합니다.' : ''},
    querySelector(selector) {
      assert.equal(selector, origin === 'https://sso.hallym.ac.kr' ? 'input[name="UserName"]' : 'input[name="username"]', 'Use the actual school or SSO field name');
      return missingForm || existingSession ? null : username;
    },
    getElementById: id => elements.flatMap(e => e.children).find(e => e.id === id),
    createElement: () => ({style: {}, children: [], appendChild(e) { this.children.push(e); },
      addEventListener(name, fn) { this[name] = fn; }}),
    createTextNode: text => ({text})
  };
  const send = value => {
    const packet = JSON.parse(value);
    if (packet.kind === 'login_ready') readyPackets.push(packet);
    else packets.push(packet);
  };
  const window = bridge === 'android' ? {Unity: {call: send}} : {chrome: {webview: {postMessage: send}}};
  const context = {location: {origin, pathname: path, href: origin + path}, window, document, Event: class {}};
  const source = script.replace('__LOGIN_SETTINGS__', () => JSON.stringify({nonce: 'test-nonce', username: saved, remember,
    automatic, password: savedPassword, autoSubmit, secureStorageAvailable: true, requirePasswordEntry}));
  const run = () => runInNewContext(source, context);
  run();
  return {packets, readyPackets, username, password, events, elements, run, form,
    checkbox: elements[0]?.children[0], automatic: elements[1]?.children[0]};
}

test('restores a saved ID without submitting the school form or reading a password', () => {
  const result = login();
  assert.equal(result.username.value, 'student-123');
  assert.equal(result.packets.length, 0);
  assert.equal(login({typed: 'another-student'}).username.value, 'another-student');
  assert.equal(login({remember: false}).username.value, '');
  result.run();
  assert.equal(result.elements.length, 2, 'Repeated injection adds no duplicate controls or handlers');
});

test('Windows and Android report only the chosen ID with page and nonce validation fields', () => {
  for (const bridge of ['windows', 'android']) {
    const result = login({bridge});
    result.username.value = ' new-student ';
    result.events.submit();
    assert.deepEqual(result.packets[0], {kind: 'login_submit', nonce: 'test-nonce',
      page: 'https://smartlead.hallym.ac.kr/login/index.php', remember: true, username: 'new-student', automatic: false, password: '', passwordEntered: false});
    result.checkbox.checked = false;
    result.checkbox.change();
    result.events.submit();
    assert.equal(result.packets[1].kind, 'login_preferences');
    assert.ok(result.packets.slice(1).every(p => !p.remember && p.username === ''));
  }
});

test('automatic login restores credentials and submits once on both bridges, while a retry remains manual', () => {
  for (const bridge of ['windows', 'android']) {
    const result = login({bridge, automatic: true, savedPassword: 'test-password', autoSubmit: true});
    assert.equal(result.password.value, 'test-password');
    assert.equal(result.packets.length, 1);
    assert.equal(result.packets[0].password, 'test-password');
    result.run();
    assert.equal(result.packets.length, 1);
    const retry = login({bridge, automatic: true, savedPassword: 'test-password', autoSubmit: false});
    assert.equal(retry.packets.length, 0);
    result.automatic.checked = false;
    result.automatic.change();
    result.events.submit();
    assert.ok(result.packets.slice(1).every(p => !p.automatic && p.password === ''));
  }
});

test('remembering a password requires the automatic-login option and enabling it also remembers the ID', () => {
  const result = login({remember: false});
  result.password.value = 'typed-test-password';
  result.events.submit();
  assert.equal(result.packets[0].password, '');
  result.automatic.checked = true;
  result.automatic.change();
  assert.equal(result.checkbox.checked, true);
  result.events.submit();
  assert.equal(result.packets.at(-1).password, 'typed-test-password');
  result.checkbox.checked = false;
  result.checkbox.change();
  assert.equal(result.automatic.checked, false);
  assert.equal(result.packets.at(-1).password, '');
});

test('after logout, school and SSO forms clear old passwords and require a fresh manual submission', () => {
  for (const page of [{path: '/login.php'}, {origin: 'https://sso.hallym.ac.kr', path: '/Account/LogIn'}]) {
    const result = login({...page, automatic: true, savedPassword: 'old-saved-password', prefilledPassword: 'old-form-password',
      autoSubmit: true, requirePasswordEntry: true});
    assert.equal(result.password.value, '');
    assert.equal(result.password.autocomplete, 'off');
    assert.equal(result.automatic.checked, false);
    assert.equal(result.packets.length, 0, 'Logout must override old automatic-login settings.');
    result.events.submit();
    assert.equal(result.packets.at(-1).passwordEntered, false);
    result.password.value = 'manually-entered-password';
    result.events.submit();
    assert.equal(result.packets.at(-1).passwordEntered, true);
    assert.equal(result.packets.at(-1).password, '', 'Manual authentication does not opt into storing a password.');
  }
});

test('school submission proceeds normally without Unity acknowledgement or replaying the submit event', () => {
  const result = login({origin: 'https://sso.hallym.ac.kr', path: '/Account/LogIn'});
  assert.equal(result.readyPackets.length, 1);
  result.password.value = 'manually-entered-password';
  result.events.submit();
  assert.equal(result.packets.at(-1).passwordEntered, true);
  assert.equal(result.form.submissions, 1, 'The school receives the original submit event immediately.');
  assert.equal(result.packets.length, 1, 'Only one passive submission snapshot is sent.');
});

test('already-logged-in school dialog is detected without claiming a new password submission', () => {
  const result = login({path: '/login.php', existingSession: true});
  assert.equal(result.packets.length, 1);
  assert.deepEqual(result.packets[0], {kind: 'login_existing_session', nonce: 'test-nonce', page: 'https://smartlead.hallym.ac.kr/login.php'});
  assert.equal(result.readyPackets.length, 0, 'A session confirmation dialog is not a fresh login form.');
});

test('external origins, other school pages and unsupported forms are untouched', () => {
  for (const options of [{origin: 'https://example.com'}, {origin: 'https://sso.hallym.ac.kr', path: '/Account/ResetPassword'},
    {origin: 'https://sso.hallym.ac.kr.evil.example', path: '/Account/LogIn'},
    {path: '/login/logout.php'}, {missingForm: true}]) {
    const result = login(options);
    assert.equal(result.username.value, '');
    assert.equal(result.elements.length, 0);
    assert.equal(result.packets.length, 0);
  }
});

test('actual SmartLEAD landing page and Hallym SSO form show options before the login button and restore credentials', () => {
  for (const page of [{path: '/login.php'}, {origin: 'https://sso.hallym.ac.kr', path: '/Account/LogIn'}]) {
    for (const bridge of ['windows', 'android']) {
      const result = login({...page, bridge, automatic: true, savedPassword: 'fake-sso-password', autoSubmit: true});
      assert.equal(result.elements.length, 2);
      assert.equal(result.automatic.id, 'pixel-campus-auto-login');
      assert.equal(result.packets.length, 1);
      assert.equal(result.packets[0].password, 'fake-sso-password');
      assert.equal(result.packets[0].page, (page.origin || 'https://smartlead.hallym.ac.kr') + page.path);
      result.run();
      assert.equal(result.packets.length, 1);
    }
  }
});

test('campus logout requests the school logout with its session key and acknowledges on both bridges', async () => {
  for (const bridge of ['windows', 'android']) {
    for (const fail of [false, true]) {
      const packets = [], requests = [];
      const send = value => packets.push(JSON.parse(value));
      const window = {M: {cfg: {sesskey: 'fake-session-key'}},
        ...(bridge === 'android' ? {Unity: {call: send}} : {chrome: {webview: {postMessage: send}}})};
      await runInNewContext(logoutScript, {window, location: {origin: 'https://smartlead.hallym.ac.kr', href: 'https://smartlead.hallym.ac.kr/local/ubion/user/index.php'},
        document: {querySelector: () => ({getAttribute: () => 'https://other.example/login/logout.php?sesskey=wrong'})},
        URL, AbortController, setTimeout: () => 1, clearTimeout() {},
        fetch: async (url, options) => {
          requests.push({url, options});
          if (fail) throw Error('offline');
          return {text: async () => ''};
        }});
      assert.equal(requests.length, 1);
      assert.equal(requests[0].url, 'https://smartlead.hallym.ac.kr/login/logout.php?sesskey=fake-session-key');
      assert.equal(requests[0].options.credentials, 'same-origin');
      assert.equal(packets.length, 1);
      assert.equal(packets[0].kind, 'logout_complete');
      assert.equal(packets[0].nonce, '__UNITY_NONCE__');
      assert.ok(packets[0].page.includes('/local/ubion/user/'));
    }
  }
});
