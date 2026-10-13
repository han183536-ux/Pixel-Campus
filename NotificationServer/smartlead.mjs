// Moodle popup notifications API. Only the authenticated user's notifications are queried.
export const SMARTLEAD = 'https://smartlead.hallym.ac.kr';
export class ConnectionError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}
const fail = (code, message) => new ConnectionError(code, message);
const messages = {
  invalidtoken: 'SmartLEAD 토큰이 만료되었거나 유효하지 않습니다. 서버를 종료하고 다시 로그인하세요.',
  accessexception: '이 계정 또는 토큰에 알림 API 권한이 없습니다. 학교 관리자에게 웹서비스 권한을 확인하세요.',
  webservice_access_exception: '이 토큰에 알림 API 권한이 없습니다.',
  wsfunctionnotavailable: '학교에서 개인 알림 API를 허용하지 않았습니다.',
  requireloginerror: 'SmartLEAD 로그인이 필요합니다.'
};

export function plainText(value) {
  return String(value || '').replace(/<script\b[^>]*>[\s\S]*?<\/script>/gi, '')
    .replace(/<style\b[^>]*>[\s\S]*?<\/style>/gi, '')
    .replace(/<\s*(br\b[^>]*|\/p|\/div)>/gi, '\n').replace(/<[^>]*>/g, '')
    .replace(/&(amp|lt|gt|quot|apos|nbsp);/g, (_, key) => ({ amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' })[key])
    .replace(/&#(x[0-9a-f]+|\d+);/gi, (_, code) => {
      const n = code[0].toLowerCase() === 'x' ? parseInt(code.slice(1), 16) : Number(code);
      return n > 0 && n <= 0x10ffff ? String.fromCodePoint(n) : '';
    }).trim();
}

export function mapNotification(n, userId) {
  const seconds = Number(n.timecreated);
  if (!Number.isSafeInteger(Number(n.id)) || Number(n.id) <= 0 || !Number.isFinite(seconds) || seconds <= 0)
    throw fail('invalid_response', 'SmartLEAD 알림 데이터 형식이 변경되었습니다.');
  let url = '';
  try { if (n.contexturl) { const link = new URL(n.contexturl, SMARTLEAD); if (['https:', 'http:'].includes(link.protocol)) url = link.href; } } catch {}
  return {
    id: `${userId}:${n.id}:${seconds}`,
    source_id: 'smartlead', source_name: '한림 SmartLEAD', source_type: 'personal_notification',
    title: plainText(n.subject || n.shortenedsubject) || 'SmartLEAD 알림',
    message: plainText(n.fullmessage || n.fullmessagehtml || n.smallmessage || n.text),
    url, published_at: new Date(seconds * 1000).toISOString(),
    is_read: n.read === true || n.read === 1 || n.read === '1' || Number(n.timeread) > 0,
    priority: 'normal', target_object_id: 'building_smartlead', icon_type: 'notice'
  };
}

export function createSmartleadProvider({ token = process.env.SMARTLEAD_TOKEN, fetchImpl = fetch, now = () => Date.now() } = {}) {
  let identity = null;
  async function call(fn, params, signal) {
    if (!token?.trim()) throw fail('not_configured', '학교 계정으로 로그인하면 개인 알림을 가져옵니다.');
    const body = new URLSearchParams({ wstoken: token, moodlewsrestformat: 'json', wsfunction: fn, ...params });
    let response;
    try { response = await fetchImpl(`${SMARTLEAD}/webservice/rest/server.php`, {
      method: 'POST', body, signal, redirect: 'error'
    }); } catch { throw fail('network_error', 'SmartLEAD 서버에 연결하지 못했습니다. 네트워크를 확인하세요.'); }
    if (!response.ok) throw fail('upstream_error', `SmartLEAD 서버 응답 오류 (HTTP ${response.status}).`);
    let data;
    try { data = await response.json(); } catch { throw fail('invalid_response', 'SmartLEAD가 JSON 대신 다른 응답을 반환했습니다.'); }
    if (data?.exception || data?.errorcode) {
      identity = null;
      const code = Object.hasOwn(messages, data.errorcode) ? data.errorcode : 'api_error';
      throw fail(code, messages[code] || 'SmartLEAD API 요청이 거부되었습니다. 계정과 웹서비스 권한을 확인하세요.');
    }
    return data;
  }
  return {
    configured: Boolean(token?.trim()),
    async load() {
      const signal = AbortSignal.timeout(8000); // Whole poll, including all pages, fits Unity's timeout.
      if (!identity) {
        const site = await call('core_webservice_get_site_info', {}, signal);
        if (!Number.isSafeInteger(site?.userid) || site.userid <= 0 || !Array.isArray(site.functions))
          throw fail('invalid_response', 'SmartLEAD 계정 정보를 확인하지 못했습니다.');
        if (!site.functions.some(f => f.name === 'message_popup_get_popup_notifications'))
          throw fail('wsfunctionnotavailable', messages.wsfunctionnotavailable);
        identity = site.userid;
      }
      const user = identity;
      const koreanDay = new Date(now() + 9 * 3600000).toISOString().slice(0, 10);
      const start = Date.parse(koreanDay + 'T00:00:00+09:00');
      const notifications = [];
      for (let page = 0; page < 20; page++) {
        const data = await call('message_popup_get_popup_notifications', {
          useridto: '0', newestfirst: '1', limit: '100', offset: String(page * 100)
        }, signal);
        if (!Array.isArray(data?.notifications)) throw fail('invalid_response', 'SmartLEAD 알림 목록을 해석하지 못했습니다.');
        const rows = data.notifications;
        for (const row of rows) {
          if (Number(row.useridto) !== user) throw fail('invalid_response', '알림의 수신 계정이 일치하지 않습니다.');
          if (row.deleted === true || row.deleted === 1 || row.deleted === '1') continue;
          const mapped = mapNotification(row, user);
          if (Date.parse(mapped.published_at) >= start && Date.parse(mapped.published_at) < start + 86400000)
            notifications.push(mapped);
        }
        if (rows.length < 100 || rows.some(n => Number(n.timecreated) * 1000 < start)) return { notifications };
      }
      throw fail('too_many_notifications', '오늘 알림이 조회 상한(2,000개)을 넘었습니다. 일부 결과로 덮어쓰지 않았습니다.');
    }
  };
}

export async function getSmartleadLoginOptions(fetchImpl = fetch) {
  let response, result;
  try {
    response = await fetchImpl(`${SMARTLEAD}/lib/ajax/service-nologin.php`, {
      method: 'POST', redirect: 'error', signal: AbortSignal.timeout(4000),
      headers: {'Content-Type': 'application/json'},
      body: JSON.stringify([{index:0, methodname:'tool_mobile_get_public_config', args:{}}])
    });
    if (!response.ok) throw new Error();
    result = await response.json();
  } catch { throw fail('config_unavailable', '학교 로그인 지원 여부를 확인하지 못했습니다. 잠시 후 연결 방식 확인을 눌러 주세요.'); }
  const entry = Array.isArray(result) ? result[0] : result;
  const config = entry?.error === false ? entry.data : null;
  if (!config || ![0,1].includes(Number(config.enablemobilewebservice)))
    throw fail('config_unavailable', '학교 로그인 설정을 해석하지 못했습니다. 학교 관리자에게 웹서비스 지원 여부를 확인하세요.');
  if (Number(config.enablewebservices) === 0 || Number(config.enablemobilewebservice) === 0)
    return {available:false, code:'mobile_service_disabled', message:'SmartLEAD에서 모바일 앱 로그인을 비활성화했습니다. 이 방식으로는 계정 로그인과 알림 연결이 불가능합니다. 학교 관리자에게 개인 알림 API 사용과 토큰 발급을 문의해야 합니다.'};
  if ([2,3].includes(Number(config.typeoflogin)))
    return {available:false, code:'browser_login_required', message:'학교가 브라우저 인증을 요구합니다. 현재 Unity의 아이디·비밀번호 로그인 방식은 지원되지 않습니다.'};
  return {available:true, code:'password_login_available', message:'학교 계정 로그인을 시도할 수 있습니다. 개인 알림 API 권한은 로그인 후 확인합니다.'};
}

export async function loginSmartlead(username, password, fetchImpl = fetch) {
  const options = await getSmartleadLoginOptions(fetchImpl);
  if (!options.available) throw fail(options.code, options.message);
  let response;
  try {
    response = await fetchImpl(`${SMARTLEAD}/login/token.php`, {
      method: 'POST', redirect: 'error', signal: AbortSignal.timeout(10000),
      body: new URLSearchParams({ username, password, service: 'moodle_mobile_app' })
    });
  } catch { throw fail('network_error', '학교 로그인 서버에 연결하지 못했습니다. 잠시 후 다시 시도하세요.'); }
  if (!response.ok) throw fail('login_unavailable', '학교 로그인 서버가 요청을 거부했습니다.');
  let data;
  try { data = await response.json(); } catch { throw fail('login_unavailable', '학교 로그인이 다른 인증 방식을 요구합니다. 학교의 웹서비스 사용 가능 여부를 확인하세요.'); }
  if (!data?.token || typeof data.token !== 'string') {
    if (data?.errorcode === 'invalidlogin') throw fail('invalid_login', '아이디 또는 비밀번호를 확인하세요. 통합인증 전용 계정은 이 로그인 방식을 지원하지 않을 수 있습니다.');
    const reasons = {
      servicenotavailable: '학교에서 요청한 앱 웹서비스를 허용하지 않았습니다.',
      cannotcreatetoken: '이 계정은 웹서비스 토큰 발급 권한이 없습니다.',
      enablewsdescription: '학교에서 외부 웹서비스를 비활성화했습니다.',
      missingrequiredcapability: '이 계정에 웹서비스 이용 권한이 없습니다.',
      usernotconfirmed: '학교에서 계정 확인을 완료해야 합니다.',
      passwordisexpired: '학교 사이트에서 비밀번호 만료 상태를 확인해야 합니다.'
    };
    const code = Object.hasOwn(reasons, data?.errorcode) ? data.errorcode : 'login_unavailable';
    throw fail(code, reasons[code] || '학교에서 로그인 토큰을 발급하지 않았습니다. 현재 응답만으로 원인을 확정할 수 없습니다.');
  }
  return createSmartleadProvider({ token: data.token, fetchImpl });
}
