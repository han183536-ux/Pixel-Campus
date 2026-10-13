const status=document.querySelector('#status');
async function tabMessage(type){
  const [tab]=await chrome.tabs.query({active:true,currentWindow:true});
  if(!tab?.url?.startsWith('https://smartlead.hallym.ac.kr/'))throw new Error('SmartLEAD 탭을 먼저 열어 주세요.');
  return chrome.tabs.sendMessage(tab.id,{type});
}
for(const [id,action] of Object.entries({
  pair:async()=>{const r=await chrome.runtime.sendMessage({type:'pair',token:document.querySelector('#token').value.trim()});document.querySelector('#token').value='';return r;},
  pick:async()=>tabMessage('pick'),
  send:async()=>tabMessage('send'),
  stop:async()=>{await chrome.runtime.sendMessage({type:'stop'});try{await tabMessage('stop');}catch{}return {ok:true,message:'자동 전달을 중지했습니다.'};}
}))document.querySelector('#'+id).onclick=async()=>{try{const r=await action();status.textContent=r?.message||'완료';}catch(e){status.textContent=e.message.includes('Receiving end')?'SmartLEAD 페이지를 새로고침한 뒤 다시 시도하세요.':e.message;}};
chrome.storage.session.get('lastStatus').then(r=>status.textContent=r.lastStatus||'Unity에서 Play → 연결 코드 복사');
