// public/js/timeline.js

export function renderPartnerList(containerId, partners, npcMap, onSelect) {
  const ul = document.getElementById(containerId);
  if (!ul) return;
  while (ul.firstChild) ul.removeChild(ul.firstChild);
  if (!partners.length) {
    const li = document.createElement('li');
    li.className = 'list-group-item text-muted small';
    li.textContent = 'No relationship data';
    ul.appendChild(li);
    return;
  }
  for (const pid of partners) {
    const npc  = npcMap?.get(pid.toLowerCase());
    const name = npc?.displayName ?? (pid.slice(0, 8) + '…');
    const li   = document.createElement('li');
    li.className = 'list-group-item list-group-item-action py-1 partner-item';
    li.dataset.pid = pid;

    const nameEl = document.createElement('div');
    nameEl.className = 'small fw-semibold';
    nameEl.textContent = name;
    li.appendChild(nameEl);

    if (npc?.biology) {
      const bioEl = document.createElement('div');
      bioEl.className = 'partner-bio';
      bioEl.textContent = npc.biology + (npc.birthDateText ? '  ·  ' + npc.birthDateText : '');
      li.appendChild(bioEl);
    }

    li.addEventListener('click', () => onSelect(pid, name));
    ul.appendChild(li);
  }
}

export function setActivePartner(pid) {
  document.querySelectorAll('.partner-item').forEach(el => {
    el.classList.toggle('active', el.dataset.pid === pid);
  });
}
