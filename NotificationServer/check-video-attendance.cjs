// Exercise the real DOM collector using synthetic school pages, without network I/O.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require(process.env.PLAYWRIGHT_MODULE_PATH || 'playwright');
const source = fs.readFileSync(path.join(__dirname,'../Assets/Notifications/Resources/SmartleadVideoAttendance.txt'),'utf8');
const course = {courseId:7,courseName:'온라인 강의 (123456-01)',courseCode:'123456-01'};
const main = body => '<html><body class="loggedin"><main id="region-main">'+body+'</main></body></html>';
const activity = (id,title,metadata='',week=1) => '<li class="section"><h3 class="sectionname">'+week+'주차</h3><ul><li class="activity vod"><a href="/mod/vod/view.php?id='+id+'"><span>'+title+'</span><span class="accesshide">동영상</span></a>'+metadata+'</li></ul></li>';
const start = '2026-10-20 00:00:00', end = '2026-10-27 23:59:59';

async function mainTest() {
  const browser=await chromium.launch({headless:true,channel:'msedge'});
  try{
    const page=await browser.newPage();
    async function scan(pages,bridge='both'){
      return await page.evaluate(async({source,pages,course,bridge})=>{
        const origin='https://smartlead.hallym.ac.kr',events=[],requests=[];
        const context={};
        if(bridge!=='windows')context.Unity={call:value=>events.push(JSON.parse(value))};
        if(bridge!=='android')context.chrome={webview:{postMessage:value=>events.push(JSON.parse(value))}};
        const fetchPage=async(url,options)=>{
          const parsed=new URL(url);const key=parsed.pathname+parsed.search;
          requests.push({key,method:options.method||'GET',credentials:options.credentials});
          return{ok:key in pages,url:parsed.href,text:async()=>pages[key]||''};
        };
        new Function('window','location','fetch',source.replace('__UNITY_NONCE__','video-test-nonce'))(context,{origin,href:origin+'/local/ubion/user/index.php'},fetchPage);
        await context.PixelCampusScanVideoAttendance([course]);
        return{events,requests};
      },{source,pages,course,bridge});
    }
    const coursePage=main('<div class="course-content"><ul>'+activity(101,'영상 A','<span class="displayoptions">'+start+' ~ '+end+', 30:00</span>')+activity(102,'영상 B','<input type="checkbox" checked><span class="displayoptions">'+start+' ~ '+end+'</span>')+'</ul></div>');
    const indexPage=main('<table><tr><th>제목</th><th>출석인정기간</th><th>지각인정기간</th><th>출석 상태</th><th>진도율</th></tr><tr><td><a href="/mod/vod/viewer.php?id=101">영상 A</a></td><td>'+start+' ~ '+end+'</td><td>2026-10-28 00:00 ~ 2026-10-30 23:59</td><td>미출석</td><td>100%</td></tr><tr><td><a href="/mod/vod/view.php?id=102">영상 B</a></td><td>'+start+' ~ '+end+'</td><td>-</td><td>출석</td><td>100%</td></tr></table>');
    const pages={'/course/view.php?id=7':coursePage,'/mod/vod/index.php?id=7':indexPage};
    const result=await scan(pages), lectures=result.events.filter(event=>event.kind==='lecture').map(event=>event.items[0]);
    assert.equal(lectures.length,2);assert.equal(lectures[0].title,'영상 A');
    assert.equal(lectures[0].startAt,Date.parse('2026-10-20T00:00:00+09:00')/1000);
    assert.equal(lectures[0].dueAt,Date.parse('2026-10-27T23:59:59+09:00')/1000);
    assert.equal(lectures[0].lateDueAt,Date.parse('2026-10-30T23:59:00+09:00')/1000);
    assert.equal(lectures[0].attended,false);assert.equal(lectures[0].attendanceKnown,true);assert.equal(lectures[1].attended,true);
    assert.ok(lectures[0].progressText.includes('100%'));
    assert.equal(lectures[0].url,'https://smartlead.hallym.ac.kr/mod/vod/view.php?id=101');
    assert.ok(result.requests.every(request=>request.method==='GET'&&request.credentials==='same-origin'&&!/view(?:er)?\.php\?id=10/.test(request.key)));
    assert.ok(result.events.every(event=>event.nonce==='video-test-nonce'&&event.page.endsWith('/user/index.php')));
    assert.deepEqual((await scan(pages,'android')).events,result.events);
    assert.deepEqual((await scan(pages,'windows')).events,result.events);
    console.log('PASS: Dates use Korea time; late periods and attendance remain distinct from 100% progress; one packet per bridge');
    console.log('PASS: Read-only collection never opens a video player or posts a viewing/attendance record');

    const noStatus=await scan({'/course/view.php?id=7':coursePage});
    assert.ok(noStatus.events.filter(event=>event.kind==='lecture').every(event=>!event.items[0].attendanceKnown&&!event.items[0].attended));
    console.log('PASS: A completion checkbox and course video period never imply confirmed attendance');

    const reportUrl='/report/ubcompletion/user_progress.php?id=7';
    const weeklyHome=main('<a href="'+reportUrl+'">온라인출석부</a><a href="https://other.example/report/ubcompletion/user_progress.php?id=7">온라인출석부</a><div class="course-content"><ul>'+activity(101,'1주 영상 A')+activity(102,'1주 영상 B')+activity(103,'2주 영상', '',2)+'</ul></div>');
    const weekly=main('<table><tr><th>주차</th><th>출석인정기간</th><th>출석</th></tr><tr><td rowspan="2">1주차</td><td rowspan="2">'+start+' ~ '+end+'</td><td rowspan="2">O</td></tr><tr></tr><tr><td>2주차</td><td>2026-10-28 00:00 ~ 2026-11-03 23:59</td><td>X</td></tr></table>');
    const weekResult=await scan({'/course/view.php?id=7':weeklyHome,[reportUrl]:weekly});
    const byId=new Map(weekResult.events.filter(event=>event.kind==='lecture').map(event=>[event.items[0].id,event.items[0]]));
    assert.equal(byId.get(101).attended,true);assert.equal(byId.get(102).attended,true);assert.equal(byId.get(103).attended,false);
    assert.equal(byId.get(103).dueAt,Date.parse('2026-11-03T23:59:00+09:00')/1000);
    assert.ok(weekResult.requests.every(request=>!request.key.includes('other.example')));
    console.log('PASS: Weekly attendance with row spans maps to the correct videos and cannot borrow the next week deadline');

    const split=main('<table><tr><th rowspan="2">주차</th><th colspan="2">출석인정기간</th><th rowspan="2">출석</th></tr><tr><th>시작</th><th>종료</th></tr><tr><td>1주차</td><td>'+start+'</td><td>'+end+'</td><td>O</td></tr></table>');
    const splitResult=await scan({'/course/view.php?id=7':weeklyHome,[reportUrl]:split});
    const splitItem=splitResult.events.find(event=>event.kind==='lecture').items[0];
    assert.equal(splitItem.dueAt,Date.parse('2026-10-27T23:59:59+09:00')/1000);assert.equal(splitItem.attended,true);
    console.log('PASS: Grouped headers with separate start/end cells preserve the attendance window');

    const availability=await scan({'/course/view.php?id=7':main('<div class="course-content">'+activity(101,'열람 가능한 영상','<span class="period">시청 가능 기간: '+start+' ~ '+end+'</span>')+'</div>')});
    const unknown=availability.events.find(event=>event.kind==='lecture').items[0];
    assert.equal(unknown.attendancePeriodKnown,false);assert.equal(unknown.dueAt,undefined);assert.ok(unknown.availabilityText.includes('시청 가능'));
    assert.equal(availability.events.find(event=>event.kind==='lectures_complete').historyLimited,true);
    console.log('PASS: Playback availability is never substituted for an attendance deadline');

    const invalid=await scan({'/course/view.php?id=7':main('<div class="course-content">'+activity(101,'잘못된 날짜','<span class="displayoptions">2026-02-30 00:00 ~ 2026-03-04 23:59</span>')+'</div>')});
    assert.equal(invalid.events.find(event=>event.kind==='lecture').items[0].attendancePeriodKnown,false);
    const failed=await scan({});assert.equal(failed.events.find(event=>event.kind==='lectures_complete').found,false);
    const unrecognized=await scan({'/course/view.php?id=7':main('알 수 없는 강의실 화면')});assert.equal(unrecognized.events.find(event=>event.kind==='lectures_complete').historyLimited,true);
    const expired=await scan({'/course/view.php?id=7':'<html><body class="notloggedin"><form action="/login/index.php"></form></body></html>'});
    assert.ok(expired.events.some(event=>event.kind==='authentication'));assert.ok(!expired.events.some(event=>event.kind==='lecture'));
    console.log('PASS: Invalid dates, unavailable pages, unknown layouts and expired logins remain unconfirmed');

    const paged=main('<div class="course-content">'+activity(101,'영상')+'</div><nav class="section-navigation">'+Array.from({length:14},(_,index)=>'<a href="/course/view.php?id=7&section='+index+'">'+index+'</a>').join('')+'</nav>');
    const sectionPages={'/course/view.php?id=7':paged};for(let i=0;i<14;i++)sectionPages['/course/view.php?id=7&section='+i]=coursePage;
    const limited=await scan(sectionPages);
    assert.equal(limited.requests.filter(request=>request.key.includes('&section=')).length,12);
    assert.equal(limited.events.find(event=>event.kind==='lectures_complete').historyLimited,true);
    console.log('PASS: Section pagination is bounded and partial coverage is explicitly reported');
  }finally{await browser.close();}
}
mainTest().catch(error=>{console.error(error);process.exitCode=1;});
