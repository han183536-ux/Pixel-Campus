const endpoint='http://127.0.0.1:8787/api/browser/ingest';
chrome.runtime.onMessage.addListener((message,sender,reply)=>{
  (async()=>{
    if(sender.id!==chrome.runtime.id)throw new Error('확장 프로그램 요청만 허용합니다.');
    const internal=sender.url===chrome.runtime.getURL('popup.html');
    if(message.type==='pair' && internal){
      if(!/^[a-f0-9]{48}$/.test(message.token))throw new Error('Unity 연결 코드를 확인하세요.');
      await chrome.storage.session.set({pairToken:message.token});
      return {ok:true,message:'연결 코드 저장 완료. SmartLEAD 알림 목록을 선택하세요.'};
    }
    if(message.type==='settings' && sender.tab){
      const {watch}=await chrome.storage.session.get('watch');
      return {ok:true,watch:watch?.url===sender.url?watch:null};
    }
    if(message.type==='watch' && sender.tab && sender.url?.startsWith('https://smartlead.hallym.ac.kr/')){
      await chrome.storage.session.set({watch:{url:sender.url,selector:message.selector}});
      return {ok:true};
    }
    if(message.type==='stop' && internal){await chrome.storage.session.remove('watch');return {ok:true};}
    if(message.type!=='deliver' || !sender.tab || !sender.url?.startsWith('https://smartlead.hallym.ac.kr/'))throw new Error('허용되지 않은 요청');
    const {pairToken}=await chrome.storage.session.get('pairToken');
    if(!pairToken)throw new Error('Unity 연결 코드를 먼저 저장하세요.');
    const response=await fetch(endpoint,{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+pairToken},body:JSON.stringify({items:message.items}),signal:AbortSignal.timeout(5000)});
    const result=await response.json();
    if(!response.ok)throw new Error(result.message||'Unity 서버 연결 실패');
    await chrome.storage.session.set({lastStatus:result.message+' · '+new Date().toLocaleTimeString()});
    return {ok:true,...result};
  })().then(reply).catch(async error=>{
    const message=error.message==='Failed to fetch'?'Unity에서 Play를 누르고 연결 코드를 다시 확인하세요.':error.message;
    await chrome.storage.session.set({lastStatus:message}); reply({ok:false,message});
  });
  return true;
});
