(() => {
    'use strict';
    document.querySelectorAll('[data-password-toggle]').forEach(button => {
        button.addEventListener('click', () => {
            const input = document.getElementById(button.dataset.passwordToggle);
            if (!input) return;
            const start = input.selectionStart, end = input.selectionEnd;
            const show = input.type === 'password';
            input.type = show ? 'text' : 'password'; button.textContent = show ? 'Hide' : 'Show';
            button.setAttribute('aria-pressed', String(show)); button.setAttribute('aria-label', `${show ? 'Hide' : 'Show'} password`);
            input.focus(); if (start !== null) input.setSelectionRange(start, end);
        });
    });
    const toggle = document.querySelector('.cc-nav-toggle'), navigation = document.getElementById('website-navigation');
    const closeNav = () => { navigation?.classList.remove('open'); toggle?.setAttribute('aria-expanded', 'false'); };
    toggle?.addEventListener('click', () => { const open = navigation.classList.toggle('open'); toggle.setAttribute('aria-expanded', String(open)); });
    document.addEventListener('keydown', event => { if (event.key === 'Escape') closeNav(); });
    document.addEventListener('click', event => { if (navigation?.classList.contains('open') && !navigation.contains(event.target) && !toggle.contains(event.target)) closeNav(); });
    document.querySelectorAll('[data-zone-filter]').forEach(button => button.addEventListener('click', () => {
        document.querySelectorAll('[data-zone-filter]').forEach(tab => { tab.classList.toggle('active', tab === button); tab.setAttribute('aria-pressed', String(tab === button)); });
        document.querySelectorAll('[data-zone-card]').forEach(card => card.hidden = button.dataset.zoneFilter !== '' && card.dataset.zoneCard !== button.dataset.zoneFilter);
    }));
    document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => { if (!window.confirm(form.dataset.confirm)) event.preventDefault(); }));
})();
// Interactive planting calendar mirrors the native date input and works without any backend write.
document.querySelectorAll('[data-calendar-for]').forEach(calendar => {
    const input = document.getElementById(calendar.dataset.calendarFor);
    const initial = input.value ? new Date(input.value+'T12:00:00') : new Date();
    let year=initial.getFullYear(),month=initial.getMonth();
    const draw=()=>{
        calendar.querySelector('[data-cal-title]').textContent=new Date(year,month,1).toLocaleDateString('en-PH',{year:'numeric',month:'long'});
        const grid=calendar.querySelector('[data-cal-days]'); grid.replaceChildren();
        ['Su','Mo','Tu','We','Th','Fr','Sa'].forEach(label=>{const span=document.createElement('span');span.textContent=label;grid.appendChild(span);});
        for(let i=0;i<new Date(year,month,1).getDay();i++) grid.appendChild(document.createElement('span'));
        for(let day=1;day<=new Date(year,month+1,0).getDate();day++){
            const button=document.createElement('button'); button.type='button'; button.textContent=String(day);
            const date=`${year}-${String(month+1).padStart(2,'0')}-${String(day).padStart(2,'0')}`;
            button.disabled=date<input.min || date>input.max;
            button.classList.toggle('selected',input.value===date); button.setAttribute('aria-label',date);
            button.setAttribute('aria-pressed',String(input.value===date));
            button.addEventListener('click',()=>{input.value=date;input.dispatchEvent(new Event('change',{bubbles:true}));draw();});grid.appendChild(button);
        }
    };
    calendar.querySelectorAll('[data-cal-step]').forEach(button=>button.addEventListener('click',()=>{
        const next=new Date(year,month+Number(button.dataset.calStep),1);year=next.getFullYear();month=next.getMonth();draw();
    }));
    input.addEventListener('change',()=>{if(input.value){const date=new Date(input.value+'T12:00:00');year=date.getFullYear();month=date.getMonth();draw();}});draw();
});
