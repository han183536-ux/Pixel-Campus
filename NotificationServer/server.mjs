import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createSmartleadProvider, ConnectionError, loginSmartlead, getSmartleadLoginOptions } from './smartlead.mjs';
import { createBrowserBridge } from './browser-bridge.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
export const koreaDay = (value = new Date()) => new Date(new Date(value).getTime() + 9 * 3600000).toISOString().slice(0, 10);

export function normalize(payload) {
  if (!Array.isArray(payload?.notifications)) throw new Error('Expected notifications array');
  const seen = new Set();
  return payload.notifications.map(item => {
    for (const key of ['id', 'source_id', 'source_name', 'title', 'published_at'])
      if (typeof item?.[key] !== 'string' || !item[key].trim()) throw new Error(`Missing ${key}`);
    if (!/(Z|[+-]\d\d:\d\d)$/.test(item.published_at) || !Number.isFinite(Date.parse(item.published_at)))
      throw new Error('published_at requires an ISO timestamp with timezone');
    if (item.url && !/^https?:\/\//i.test(item.url)) throw new Error('Invalid notification URL');
    return { ...item, is_read: item.is_read === true, received_at: item.received_at || new Date().toISOString() };
  }).filter(item => {
    const key = JSON.stringify([item.source_id, item.id]);
    if (seen.has(key)) return false;
    seen.add(key); return true;
  }).sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at));
}

export function createNotificationServer(options = {}) {
  const now = options.now || Date.now;
  const mode = options.mode || process.env.NOTIFICATION_MODE || 'browser';
  if (!['sample', 'file', 'api', 'smartlead', 'browser'].includes(mode)) throw new Error('Unknown notification mode');
  const browser = createBrowserBridge({now});
  let provider = options.provider || (mode === 'smartlead' ? createSmartleadProvider() : null);
  const authenticate = options.authenticate || loginSmartlead;
  let generation = 0, authenticating = false, loginAttempt = -Infinity;
  const upstream = options.upstream || process.env.NOTIFICATION_UPSTREAM_URL;
  const inputFile = options.file || process.env.NOTIFICATION_FILE || path.join(here, 'smartlead.notifications.json');
  let cache = null, updated = null, lastAttempt = 0, pending = null, stale = false;
  let connectionError = null;
  async function load() {
    if (pending) return pending;
    if (lastAttempt && now() - lastAttempt < 30000) {
      if (cache === null && connectionError) throw connectionError;
      return;
    }
    const version = generation, currentProvider = provider;
    const task = (async () => {
      lastAttempt = now();
      try {
        let payload;
        if (mode === 'smartlead') {
          payload = await currentProvider.load();
        } else if (mode === 'api') {
          if (!upstream || new URL(upstream).protocol !== 'https:') throw new Error('Approved HTTPS API URL required');
          const token = process.env.NOTIFICATION_UPSTREAM_TOKEN;
          const response = await fetch(upstream, {
            headers: token ? { Authorization: `Bearer ${token}` } : {},
            signal: AbortSignal.timeout(8000), redirect: 'error'
          });
          if (!response.ok) throw new Error('Upstream request failed');
          // Site-specific adapters should convert the official API response here.
          payload = await response.json();
        } else {
          const file = mode === 'sample' ? path.join(here, '../Assets/Notifications/Samples/notifications.json') : inputFile;
          payload = JSON.parse((await readFile(file, 'utf8')).replaceAll('{{TODAY}}', koreaDay()));
        }
        if (version !== generation) throw new ConnectionError('session_changed', '로그인 계정이 변경되었습니다.');
        cache = normalize(payload); updated = new Date().toISOString(); stale = false; connectionError = null;
      } catch (error) {
        if (version !== generation) throw new ConnectionError('session_changed', '로그인 계정이 변경되었습니다.');
        connectionError = error instanceof ConnectionError ? error : new ConnectionError('source_unavailable', '알림 제공 서버에 연결하지 못했습니다. 서버 설정을 확인하세요.');
        stale = true;
        if (cache === null) throw connectionError;
      }
    })();
    pending = task;
    try { await task; } finally { if (pending === task) pending = null; }
  }
  return http.createServer(async (req, res) => {
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    res.setHeader('Cache-Control', 'no-store');
    const send = (code, body) => { res.writeHead(code); res.end(JSON.stringify(body)); };
    // Local single-user prototype; reject browser cross-origin access by default.
    if (!/^(127\.0\.0\.1|localhost)(:\d+)?$/.test(req.headers.host || '')) return send(403, {error:'invalid_host'});
    const url = new URL(req.url, 'http://127.0.0.1');
    if (mode === 'browser' && await browser.handle(req,res,url)) return;
    if (req.headers.origin) return send(403, { error: 'Cross-origin browser access is disabled' });
    if (mode === 'browser' && req.method === 'GET' && url.pathname === '/api/notifications') {
      const data=browser.snapshot();
      data.notifications=data.notifications.filter(n=>(url.searchParams.get('today')==='false'||koreaDay(n.received_at)===koreaDay())&&(!url.searchParams.has('source')||n.source_id===url.searchParams.get('source')));
      return send(200,data);
    }
    if (mode === 'browser' && req.method === 'GET' && url.pathname === '/api/sources') return send(200,{sources:[{id:'smartlead',name:'SmartLEAD 브라우저'}]});
    if (req.method === 'POST' && ['/api/auth/login', '/api/auth/logout'].includes(url.pathname)) {
      if (mode !== 'smartlead') return send(409, {error:'wrong_mode',message:'서버를 SmartLEAD 모드로 실행하세요.'});
      if (req.headers['x-notification-client'] !== 'unity' || !req.headers['content-type']?.startsWith('application/json'))
        return send(403, {error:'invalid_client',message:'Unity 로그인 요청만 허용합니다.'});
      if (url.pathname.endsWith('/logout')) {
        generation++; provider = createSmartleadProvider({token:''});
        cache = null; updated = null; lastAttempt = 0; pending = null; stale = false; connectionError = null;
        return send(200, {message:'로그아웃했습니다.'});
      }
      if (authenticating || now() - loginAttempt < 3000) return send(429, {error:'login_busy',message:'잠시 후 다시 로그인하세요.'});
      authenticating = true; loginAttempt = now();
      let version = generation;
      try {
        let chunks = [], size = 0;
        req.setTimeout(10000, () => req.destroy());
        for await (const chunk of req) {
          size += chunk.length;
          if (size > 8192) return send(413, {error:'body_too_large',message:'입력 길이가 너무 깁니다.'});
          chunks.push(chunk);
        }
        let credentials;
        try { credentials = JSON.parse(Buffer.concat(chunks).toString('utf8')); } catch { return send(400, {error:'invalid_json',message:'로그인 요청 형식 오류'}); }
        chunks = null;
        if (typeof credentials?.username !== 'string' || !credentials.username.trim() || credentials.username.length > 256 ||
            typeof credentials.password !== 'string' || !credentials.password || credentials.password.length > 2048)
          return send(400, {error:'invalid_credentials',message:'아이디와 비밀번호를 입력하세요.'});
        generation++; version = generation; provider = createSmartleadProvider({token:''});
        cache = null; updated = null; lastAttempt = 0; pending = null; stale = false; connectionError = null;
        let next;
        try { next = await authenticate(credentials.username.trim(), credentials.password); }
        finally { credentials.password = ''; credentials.username = ''; }
        const first = normalize(await next.load()); // Validate identity and notification permission before committing a session.
        if (generation !== version) return send(409, {error:'session_changed',message:'로그인이 취소되었습니다.'});
        generation++; provider = next; cache = first; updated = new Date().toISOString();
        pending = null; lastAttempt = now(); stale = false; connectionError = null;
        return send(200, {message:'학교 계정으로 로그인했습니다.'}); // Never return the school token to Unity.
      } catch (error) {
        return send(401, {error:error instanceof ConnectionError ? error.code : 'login_failed',
          message:error instanceof ConnectionError ? error.message : '학교 로그인에 실패했습니다.'});
      } finally { authenticating = false; }
    }
    if (req.method !== 'GET') return send(405, { error: 'GET only' });
    if (url.pathname === '/api/auth/capabilities') {
      if (mode !== 'smartlead') return send(200, {available:false,code:'wrong_mode',message:'SmartLEAD 서버 모드가 아닙니다.'});
      try { return send(200, await getSmartleadLoginOptions()); }
      catch (error) { return send(503, {available:false,code:'config_unavailable',message:error instanceof ConnectionError ? error.message : '학교 로그인 지원 여부 확인 실패'}); }
    }
    if (url.pathname === '/health') return send(200, { status: 'ok', mode, auth_supported: mode === 'smartlead' });
    if (url.pathname === '/api/connection') return send(200, {
      mode, configured: mode !== 'smartlead' || provider.configured,
      state: connectionError ? connectionError.code : (updated ? 'connected' : 'waiting'),
      message: connectionError?.message || '', updated_at: updated
    });
    if (!['/api/notifications', '/api/sources'].includes(url.pathname)) return send(404, { error: 'Not found' });
    const version = generation;
    try {
      await load();
      if (version !== generation) return send(409, {error:'session_changed',message:'로그인 계정이 변경되었습니다.'});
      if (url.pathname === '/api/sources') {
        const sources = mode === 'smartlead' ? [{id: 'smartlead', name: '한림 SmartLEAD'}] : [...new Map(cache.map(n => [n.source_id, { id: n.source_id, name: n.source_name }])).values()];
        return send(200, { sources, stale });
      }
      const notifications = cache.filter(n =>
        (url.searchParams.get('today') === 'false' || koreaDay(n.published_at) === koreaDay()) &&
        (!url.searchParams.has('source') || n.source_id === url.searchParams.get('source')));
      send(200, { notifications, updated_at: updated, stale, mode, timezone: 'Asia/Seoul', message: connectionError?.message || '' });
    } catch { send(503, { error: connectionError?.code || 'source_unavailable', message: connectionError?.message || '알림 연결 실패' }); }
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.PORT || 8787);
  createNotificationServer().listen(port, '127.0.0.1', () => console.log(`Notification API: http://127.0.0.1:${port}`));
}
