(() => {
  const badge=document.querySelector('[data-unread-badge]'); if(!badge)return;
  let pending=null,timer=null,generation=0;
  const refresh=async()=>{
    if(document.hidden||pending)return;
    const current=++generation;pending=new AbortController();
    try{
      const r=await fetch('/Notifications/Feed',{credentials:'same-origin',cache:'no-store',signal:pending.signal,headers:{'X-CropCare-Partial':'true'}});
      if(r.status===401||r.redirected){badge.textContent='Sign in';return;}
      if(!r.ok)throw Error('Notification refresh unavailable');
      const data=await r.json();if(current!==generation||!Array.isArray(data.items))return;
      const unread=data.items.filter(n=>!n.isRead).length;badge.textContent=String(unread);badge.title='Unread among latest 100 notifications';
      document.dispatchEvent(new CustomEvent('cropcare:notifications',{detail:data.items}));
      const status=document.querySelector('[data-notification-refresh-status]');if(status)status.textContent='Updated just now · latest 100 notifications';
    }catch(e){if(e.name!=='AbortError'){badge.title='Notification count may be out of date';const status=document.querySelector('[data-notification-refresh-status]');if(status)status.textContent='Refresh unavailable. Previously loaded history is retained.';}}
    finally{pending=null;}
  };
  document.addEventListener('visibilitychange',()=>{if(document.hidden){generation++;pending?.abort();}else refresh();});
  window.addEventListener('pagehide',()=>{clearInterval(timer);pending?.abort();});
  refresh();timer=setInterval(refresh,30000);
  document.addEventListener('cropcare:notifications',event=>{
    const host=document.querySelector('[data-notification-list]');if(!host)return;
    const token=document.querySelector('input[name="__RequestVerificationToken"]')?.value;if(!token)return;
    host.replaceChildren();
    event.detail.forEach(n=>{
      const card=document.createElement('article');card.className='cc-card';
      const title=document.createElement('h3');title.textContent=(n.isRead?'':'● ')+n.title;
      const body=document.createElement('p');body.textContent=n.message;
      const time=document.createElement('small');time.textContent=n.timestampMs>0?new Intl.DateTimeFormat('en-PH',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Manila'}).format(n.timestampMs):'Time unavailable';card.append(title,body,time);
      [['MarkRead','Mark read'],['Remove','Delete']].forEach(([action,label])=>{
        if(action==='MarkRead'&&n.isRead)return;const form=document.createElement('form');form.method='post';form.action='/Notifications/'+action;form.className='d-inline-block m-2';
        for(const [name,value]of [['__RequestVerificationToken',token],['id',n.id]]){const field=document.createElement('input');field.type='hidden';field.name=name;field.value=value;form.append(field);}
        const button=document.createElement('button');button.className='btn btn-light btn-sm';button.textContent=label;form.append(button);
        if(action==='Remove')form.addEventListener('submit',e=>{if(!confirm('Delete this notification?'))e.preventDefault();});card.append(form);
      });
      if(n.zoneId){const link=document.createElement('a');link.href='/Zones/Details/'+encodeURIComponent(n.zoneId);link.textContent='Open zone';link.className='btn btn-light btn-sm';card.append(link);}host.append(card);
    });
    if(!event.detail.length){const empty=document.createElement('p');empty.textContent='No notifications yet.';host.append(empty);}
  });
})();
