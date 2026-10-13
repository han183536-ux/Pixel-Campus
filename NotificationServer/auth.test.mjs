import test from 'node:test';
import assert from 'node:assert/strict';
import {createNotificationServer} from './server.mjs';
import {loginSmartlead, ConnectionError, getSmartleadLoginOptions} from './smartlead.mjs';
const enabledConfig = [{error:false,data:{enablewebservices:1,enablemobilewebservice:1,typeoflogin:1}}];

test('school login sends credentials only to official HTTPS POST and sanitizes failures', async()=>{
  let captured;
  const provider=await loginSmartlead('student','private-pass',async(url,options)=>{
    if (url.includes('service-nologin.php')) return {ok:true,json:async()=>enabledConfig};
    captured={url,options}; return {ok:true,json:async()=>({token:'private-token'})};
  });
  assert.equal(captured.url,'https://smartlead.hallym.ac.kr/login/token.php');
  assert.equal(captured.options.body.get('service'),'moodle_mobile_app');
  assert.equal(captured.options.body.get('password'),'private-pass');
  assert.equal(captured.options.redirect,'error');
  assert.equal(provider.configured,true);
  await assert.rejects(loginSmartlead('u','p',async(url)=>({ok:true,json:async()=>url.includes('service-nologin.php') ? enabledConfig : {errorcode:'invalidlogin',error:'secret'}})), {code:'invalid_login'});
});

test('disabled mobile login blocks credential submission before authentication', async()=>{
  let requests=0;
  const disabled=async(url,options)=>{
    requests++;
    assert.ok(url.endsWith('/lib/ajax/service-nologin.php'));
    assert.ok(!options.body.includes('private-pass'));
    return {ok:true,json:async()=>[{error:false,data:{enablewebservices:1,enablemobilewebservice:0,typeoflogin:1}}]};
  };
  await assert.rejects(loginSmartlead('student','private-pass',disabled),{code:'mobile_service_disabled'});
  assert.equal(requests,1);
  const browser=await getSmartleadLoginOptions(async()=>({ok:true,json:async()=>[{error:false,data:{enablewebservices:1,enablemobilewebservice:1,typeoflogin:2}}]}));
  assert.equal(browser.code,'browser_login_required');
  for(const errorcode of ['servicenotavailable','cannotcreatetoken','missingrequiredcapability'])
    await assert.rejects(loginSmartlead('u','p',async(url)=>({ok:true,json:async()=>url.includes('service-nologin.php')?enabledConfig:{errorcode}})),{code:errorcode});
});

test('Unity login/logout isolates accounts, validates requests and never returns secrets', async()=>{
  let clock=10000, received;
  const server=createNotificationServer({mode:'smartlead',now:()=>clock,authenticate:async(u,p)=>{
    received=[u,p];
    if(p==='wrong') throw new ConnectionError('invalid_login','아이디 또는 비밀번호를 확인하세요.');
    return {configured:true,load:async()=>({notifications:[{id:u+':1',source_id:'smartlead',source_name:'SmartLEAD',title:'Test',published_at:new Date().toISOString()}]})};
  }});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base=`http://127.0.0.1:${server.address().port}`;
  const headers={'content-type':'application/json','x-notification-client':'unity'};
  const post=(path,body,extra={})=>fetch(base+path,{method:'POST',headers:{...headers,...extra},body:JSON.stringify(body)});
  try {
    assert.equal((await post('/api/auth/login',{username:'a',password:'p'},{Origin:'https://attacker.test'})).status,403);
    assert.equal((await post('/api/auth/login',{username:'a',password:'p'},{'x-notification-client':''})).status,403);
    const r=await post('/api/auth/login',{username:'student',password:'private-pass'});
    assert.equal(r.status,200); assert.deepEqual(received,['student','private-pass']);
    assert.ok(!(await r.text()).includes('private-pass'));
    let data=await (await fetch(base+'/api/notifications')).json();
    assert.equal(data.notifications[0].id,'student:1');
    clock+=4000;
    assert.equal((await post('/api/auth/login',{username:'other',password:'wrong'})).status,401);
    assert.equal((await fetch(base+'/api/notifications')).status,503); // Prior account data was cleared.
    clock+=4000;
    assert.equal((await post('/api/auth/login',{username:'other',password:'pass'})).status,200);
    data=await (await fetch(base+'/api/notifications')).json();
    assert.equal(data.notifications[0].id,'other:1');
    assert.equal((await post('/api/auth/logout',{})).status,200);
    assert.equal((await fetch(base+'/api/notifications')).status,503);
    const state=await (await fetch(base+'/api/connection')).json();
    assert.equal(state.configured,false);
  } finally {await new Promise(resolve=>server.close(resolve));}
});

test('logout during pending school authentication cannot resurrect a session',async()=>{
  let release, entered;
  const started=new Promise(resolve=>entered=resolve);
  const server=createNotificationServer({mode:'smartlead',authenticate:async()=>{
    entered(); await new Promise(resolve=>release=resolve);
    return {configured:true,load:async()=>({notifications:[]})};
  }});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base=`http://127.0.0.1:${server.address().port}`;
  const post=(path,body)=>fetch(base+path,{method:'POST',headers:{'content-type':'application/json','x-notification-client':'unity'},body:JSON.stringify(body)});
  try {
    const login=post('/api/auth/login',{username:'u',password:'p'});
    await started;
    assert.equal((await post('/api/auth/logout',{})).status,200);
    release();
    assert.equal((await login).status,409);
    assert.equal((await fetch(base+'/api/notifications')).status,503);
  } finally {await new Promise(resolve=>server.close(resolve));}
});
