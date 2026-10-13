(() => {
  let selector='', timer=null, inFlight=false, stopped=false, seen=new Set();
  function collect(){
    const root=selector && document.querySelector(selector);
    if(!root)throw new Error('목록 영역이 없습니다. 알림 목록을 열고 영역을 다시 선택하세요.');
    const unique=new Map();
    for(const a of root.querySelectorAll('a[href]')){
      const title=(a.innerText||'').replace(/\s+/g,' ').trim();
      if(!title || !a.getClientRects().length)continue;
      const url=new URL(a.href,location.href);
      if(url.origin!==location.origin || !/^\/mod\//.test(url.pathname) || /[?&](sesskey|token|password)=/i.test(url.search))continue;
      url.hash='';url.searchParams.sort();
      unique.set(url.href,{title:title.slice(0,500),url:url.href});
    }
    return [...unique.values()];
  }
  async function send(manual=false){
    if(inFlight)return {ok:false,message:'전송 중입니다.'};
    inFlight=true;
    try{
      const all=collect(), items=all.filter(x=>!seen.has(x.url));
      if(!items.length)return {ok:true,message:all.length?'이미 전달한 링크입니다.':'선택한 영역에 공지·학습활동 링크가 없습니다.'};
      let count=0;
      for(let i=0;i<items.length;i+=100){
        const batch=items.slice(i,i+100);
        const r=await chrome.runtime.sendMessage({type:'deliver',items:batch});
        if(!r.ok)return r;
        batch.forEach(x=>seen.add(x.url));count+=r.added;
      }
      return {ok:true,message:`실제 목록 링크 ${count}개 전달 완료. Unity에서 새로고침하세요.`};
    }catch(e){return {ok:false,message:e.message};}finally{inFlight=false;}
  }
  function path(element){
    const parts=[];
    while(element && element!==document.body){
      if(element.id){parts.unshift('#'+CSS.escape(element.id));break;}
      const siblings=[...element.parentElement.children].filter(x=>x.tagName===element.tagName);
      parts.unshift(element.tagName.toLowerCase()+':nth-of-type('+(siblings.indexOf(element)+1)+')');
      element=element.parentElement;
    }
    return parts.join(' > ');
  }
  function choose(){
    let highlighted=null,oldOutline='';
    const container=e=>e.closest('[data-region="notification-list"], [role="list"], ul, ol, tbody, table')||e.parentElement;
    function cleanup(){if(highlighted)highlighted.style.outline=oldOutline;document.removeEventListener('mouseover',hover,true);document.removeEventListener('click',click,true);document.removeEventListener('keydown',key,true);}
    function hover(e){if(highlighted)highlighted.style.outline=oldOutline;highlighted=container(e.target);oldOutline=highlighted.style.outline;highlighted.style.outline='3px solid #00bfff';}
    function key(e){if(e.key==='Escape')cleanup();}
    async function click(e){
      e.preventDefault();e.stopPropagation();const chosen=container(e.target);cleanup();
      selector=path(chosen);stopped=false;seen.clear();
      await chrome.runtime.sendMessage({type:'watch',selector});
      const result=await send(true);await chrome.runtime.sendMessage({type:'watch',selector});
      // Only chosen list anchors are read; no account fields, cookies or page scripts are inspected.
      alert(result.message);
    }
    document.addEventListener('mouseover',hover,true);document.addEventListener('click',click,true);document.addEventListener('keydown',key,true);
  }
  chrome.runtime.onMessage.addListener((m,s,reply)=>{
    if(m.type==='pick'){choose();reply({ok:true,message:'ポップアップを閉じて通知一覧の項目にマウスを置き、青枠を確認してクリックしてください。Escで中止できます。'.replace(/.*/,'팝업을 닫고 알림 목록에 마우스를 올린 뒤 파란 영역을 클릭하세요. Esc로 취소합니다.')});}
    else if(m.type==='send'){send(true).then(reply);return true;}
    else if(m.type==='stop'){stopped=true;selector='';seen.clear();reply({ok:true});}
  });
  // DOM observation only: this does not call hidden school APIs or refresh pages in the background.
  new MutationObserver(()=>{if(!selector||stopped)return;clearTimeout(timer);timer=setTimeout(()=>send(),1200);}).observe(document.documentElement,{childList:true,subtree:true,characterData:true});
  chrome.runtime.sendMessage({type:'settings'}).then(r=>{if(r?.watch){selector=r.watch.selector;send();}});
})();
