// Rebuild the public-board catalogue from official university navigation.
// node NotificationServer/discover-university-boards.mjs
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const root = new URL('../', import.meta.url);
const configPath = new URL('Assets/Notifications/Resources/UniversityNoticeBoards.json', root);
const existing = JSON.parse(await readFile(configPath, 'utf8'));
const clean = s => s.replace(/<[^>]*>/g, ' ').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
const allowed = u => u.protocol === 'https:' && (!u.port || u.port === '443') &&
  !u.username && !u.password && (u.hostname === 'hallym.ac.kr' || u.hostname.endsWith('.hallym.ac.kr'));
const queue = [], visited = new Set(), boards = new Map(), failures = [], sites = new Map();
function enqueue(value, name, depth) {
  try {
    const u = new URL(value); u.hash = ''; u.search = '';
    if (!allowed(u) || /login|logout|download|admin/i.test(u.pathname) || visited.has(u.href) || queue.some(p => p.url === u.href)) return;
    if (queue.length + visited.size < 650) queue.push({url: u.href, name, depth});
  } catch {}
}
for (const [url, name] of [['https://www.hallym.ac.kr/hallym/index.do', '한림대학교'],
  ['https://www.hallym.ac.kr/hlsw/index.do', 'SW중심대학사업단'],
  ['https://sw.hallym.ac.kr/sw/index.do', '소프트웨어학부']]) enqueue(url, name, 0);
for (const b of existing.boards.slice(0, 5)) boards.set(new URL(b.url).pathname, b);
async function get(url) {
  // Validate each redirect before requesting its destination.
  for (let i = 0; i < 5; i++) {
    const response = await fetch(url, {redirect: 'manual', signal: AbortSignal.timeout(10000)});
    if (response.status >= 300 && response.status < 400) {
      const next = new URL(response.headers.get('location'), url);
      if (!allowed(next)) throw Error('공식 도메인 밖으로 이동');
      url = next.href; continue;
    }
    if (!response.ok) throw Error('HTTP ' + response.status);
    return {html: await response.text(), url};
  }
  throw Error('redirect limit');
}
async function scan(page) {
  visited.add(page.url);
  try {
    const {html, url} = await get(page.url);
    const siteMatch = new URL(url).pathname.match(/^\/(?:sites\/)?([^/]+)\//);
    const site = siteMatch?.[1] || new URL(url).hostname;
    const title = clean(html.match(/<title[^>]*>([\s\S]*?)<\/title>/i)?.[1] || page.name);
    if (!sites.has(site)) sites.set(site, {name: page.name, url, boards: 0});
    for (const match of html.matchAll(/href\s*=\s*["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi)) {
      let link; try { link = new URL(match[1].replace(/&amp;/g, '&'), url); } catch { continue; }
      if (!allowed(link)) continue;
      const label = clean(match[2]);
      const rss = link.pathname.match(/^\/bbs\/([^/]+)\/\d+\/rssList\.do$/);
      if (rss) {
        link.search = '?row=50';
        const key = link.pathname;
        if (!boards.has(key)) boards.set(key, {name: (sites.get(site).name + ' · ' + title.replace(/\s*[|>\-]\s*/g, ' · ')).slice(0, 110), url: link.href});
        sites.get(site).boards++;
      } else if (/\/(?:index|subview)\.do$/.test(link.pathname) &&
        (page.depth === 0 || /공지|소식|알림|모집|교육신청|행사|장학|취업|자료실/.test(label) || /\/index\.do$/.test(link.pathname))) {
        if (page.depth < 3) enqueue(link.href, /\/index\.do$/.test(link.pathname) ? label || page.name : page.name, page.depth + 1);
      }
    }
  } catch (error) { failures.push({...page, error: error.message}); }
}
// Four requests at a time, one finite catalogue crawl; never crawl articles or forms.
while (queue.length) {
  const batch = queue.splice(0, 4).filter(p => !visited.has(p.url));
  await Promise.all(batch.map(scan));
  if (visited.size % 40 < 4) process.stdout.write(`${visited.size} pages, ${boards.size} boards\n`);
}
const result = {generatedAt: new Date().toISOString(), discoveryPages: visited.size,
  boards: [...boards.values()], unsupportedSites: [...sites.values()].filter(s => !s.boards), failures};
await writeFile(configPath, JSON.stringify(result, null, 2) + '\n');
await mkdir(new URL('Temp', root), {recursive: true});
await writeFile(new URL('Temp/university-discovery-report.json', root), JSON.stringify(result, null, 2));
console.log(`${result.boards.length} boards saved to ${fileURLToPath(configPath)}; ${failures.length} pages failed.`);
