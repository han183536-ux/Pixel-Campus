import { randomBytes, createHash, timingSafeEqual } from 'node:crypto';

export function createBrowserBridge({now = Date.now} = {}) {
  let token = randomBytes(24).toString('hex'), records = new Map(), lastReceived = null;
  const snapshot = () => ({
    notifications: [...records.values()].filter(n => now() - Date.parse(n.received_at) < 86400000 * 2),
    updated_at: lastReceived, stale: false, mode: 'browser',
    message: lastReceived ? '브라우저에서 실제 링크 수신 · ' + lastReceived : '브라우저 연결 대기 · 확장 프로그램에서 알림 목록을 보내 주세요.'
  });
  async function handle(req, res, url) {
    if (!url.pathname.startsWith('/api/browser/')) return false;
    const send = (status, value) => { res.writeHead(status); res.end(JSON.stringify(value)); return true; };
    const origin = req.headers.origin;
    const extension = origin && /^chrome-extension:\/\/[a-p]{32}$/.test(origin);
    const ingest = url.pathname === '/api/browser/ingest';
    if (origin && !(extension && ingest)) return send(403, {message:'허용되지 않은 브라우저 요청입니다.'});
    if (extension) {
      res.setHeader('Access-Control-Allow-Origin', origin);
      res.setHeader('Vary', 'Origin');
      res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
      res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
      if (req.method === 'OPTIONS') return send(204, {});
    }
    if (url.pathname === '/api/browser/pairing' && req.method === 'GET')
      return send(200, {token, count:records.size, last_received:lastReceived});
    if (url.pathname === '/api/browser/reset' && req.method === 'POST' && req.headers['x-notification-client'] === 'unity') {
      token = randomBytes(24).toString('hex'); records.clear(); lastReceived = null;
      return send(200, {message:'연결과 수신 기록을 초기화했습니다.'});
    }
    if (!ingest || req.method !== 'POST') return send(404, {message:'경로를 확인하세요.'});
    const supplied = String(req.headers.authorization || '').replace(/^Bearer /, '');
    if (Buffer.byteLength(supplied) !== Buffer.byteLength(token) || !timingSafeEqual(Buffer.from(supplied), Buffer.from(token)))
      return send(401, {message:'Unity의 새 연결 코드를 복사해서 다시 연결하세요.'});
    if (!req.headers['content-type']?.startsWith('application/json')) return send(415,{message:'JSON 요청이 필요합니다.'});
    try {
      const payload = await new Promise((resolve,reject)=>{
        let chunks=[], size=0, failed=false;
        req.setTimeout(5000,()=>req.destroy());
        req.on('data',chunk=>{size+=chunk.length; if(size>131072){if(!failed){failed=true;reject(new Error('size'));}return;} if(!failed)chunks.push(chunk);});
        req.on('end',()=>{if(failed)return;try{resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));}catch{reject(new Error('json'));}});
        req.on('error',reject);
      });
      if (!Array.isArray(payload.items) || payload.items.length > 100) throw new Error('items');
      const batch = payload.items.map(item=>{
        if (typeof item.title !== 'string' || !item.title.trim() || item.title.length>500 || typeof item.url !== 'string' || item.url.length>2048) throw new Error('item');
        const link = new URL(item.url);
        if (link.origin !== 'https://smartlead.hallym.ac.kr' || link.username || link.password || /\/(login|logout)(\/|\.|$)/i.test(link.pathname)) throw new Error('url');
        for(const key of link.searchParams.keys()) if(/token|sesskey|password|secret/i.test(key)) throw new Error('secret');
        link.hash=''; link.searchParams.sort();
        return {title:item.title.trim(),url:link.href};
      });
      const received = new Date(now()).toISOString();
      let added=0;
      for(const item of batch){
        const id=createHash('sha256').update(item.url).digest('hex');
        if(records.has(id)) continue;
        records.set(id,{id:token.slice(0,8)+':'+id,source_id:'smartlead',source_name:'SmartLEAD 브라우저',source_type:'browser_observation',
          title:item.title,url:item.url,published_at:received,received_at:received,is_read:false,
          message:'브라우저 목록에서 수신한 실제 링크입니다. 표시 시각은 게시일이 아닌 Unity 수신 시각입니다.',target_object_id:'building_smartlead',icon_type:'notice'});
        added++;
      }
      while(records.size>500)records.delete(records.keys().next().value);
      lastReceived=received;
      return send(200,{added,total:records.size,message:`새 링크 ${added}개 수신 (전체 ${records.size}개)`});
    } catch { return send(400,{message:'전달할 항목을 확인하세요. SmartLEAD의 제목과 공지 링크만 허용합니다.'}); }
  }
  return {handle,snapshot};
}
