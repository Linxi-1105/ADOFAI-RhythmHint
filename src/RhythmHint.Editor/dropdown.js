/* 下拉菜单：代替浏览器自带的 <select> 弹出列表（系统样式的白色列表和编辑器不搭，2026-11-18 用户）。
 * 原来的 <select> 留着当数据（藏起来）：选中时改它的 value 并发 change 事件，别处的代码照旧监听；
 * 程序里改了 value 或选项文字后调 syncAll()，按钮上的字跟着变。
 * 键盘：按钮上 ↑↓ / Enter / 空格打开；菜单里 ↑↓ Home End 移动，Enter / 空格选中，Esc 关闭。菜单里的按键不传给编辑器的快捷键。 */
(function (root) {
  'use strict';

  const all = [];
  let current = null; // 开着的菜单：{ dd, menu }
  const reduceMotion = root.matchMedia && root.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const CHECK = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>';
  const CHEVRON = '<svg class="dd-chev" viewBox="0 0 24 24" aria-hidden="true"><path d="m7 10 5 5 5-5"/></svg>';

  /** 把一个 <select> 换成编辑器样式的下拉按钮；className 加在按钮上。 */
  function enhance(select, className) {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'dd' + (className ? ' ' + className : '');
    button.setAttribute('aria-haspopup', 'listbox');
    button.setAttribute('aria-expanded', 'false');
    button.innerHTML = '<span class="dd-label"></span>' + CHEVRON;
    select.classList.add('dd-native');
    select.tabIndex = -1;
    select.setAttribute('aria-hidden', 'true');
    select.after(button);

    const dd = {
      select, button,
      sync() {
        const option = select.options[select.selectedIndex];
        button.firstChild.textContent = option ? option.textContent : '';
        if (select.title) {
          button.title = select.title;
          button.setAttribute('aria-label', select.title);
        }
      },
    };
    // 鼠标点开 / 收起；键盘在 keydown 里处理（detail 为 0 的是键盘触发的点击，不再处理一遍）
    button.addEventListener('click', e => {
      if (e.detail === 0) return;
      if (current && current.dd === dd) close(false); else open(dd);
    });
    button.addEventListener('keydown', e => {
      if (!['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(e.key)) return;
      e.preventDefault();
      e.stopPropagation(); // 空格不要变成播放 / 暂停
      open(dd);
    });
    dd.sync();
    all.push(dd);
    return dd;
  }

  function open(dd) {
    close(false);
    const { select, button } = dd;
    const menu = document.createElement('div');
    menu.className = 'dd-menu';
    menu.setAttribute('role', 'listbox');
    Array.from(select.options).forEach((option, i) => {
      const item = document.createElement('button');
      item.type = 'button';
      item.className = 'dd-item';
      item.setAttribute('role', 'option');
      item.setAttribute('aria-selected', String(i === select.selectedIndex));
      item.dataset.index = String(i);
      item.innerHTML = '<span></span>' + CHECK;
      item.firstChild.textContent = option.textContent;
      menu.appendChild(item);
    });
    document.body.appendChild(menu);
    place(menu, button);
    button.setAttribute('aria-expanded', 'true');
    button.classList.add('open');
    current = { dd, menu };
    menu.addEventListener('click', e => {
      const item = e.target.closest('.dd-item');
      if (item) choose(dd, Number(item.dataset.index), e.detail === 0);
    });
    menu.addEventListener('keydown', e => onKey(e, dd, menu));
    const selected = menu.querySelector('[aria-selected="true"]') || menu.firstElementChild;
    if (selected) {
      selected.focus({ preventScroll: true });
      selected.scrollIntoView({ block: 'nearest' });
    }
  }

  /** 贴着按钮放：下面放不下、上面更宽敞时往上弹（播放条在屏幕底部）。 */
  function place(menu, button) {
    const r = button.getBoundingClientRect(), gap = 6, pad = 8;
    menu.style.minWidth = r.width + 'px';
    const below = window.innerHeight - r.bottom - gap - pad, above = r.top - gap - pad;
    const up = menu.offsetHeight > below && above > below;
    menu.style.maxHeight = Math.max(120, up ? above : below) + 'px';
    const h = menu.offsetHeight, w = menu.offsetWidth;
    menu.style.top = (up ? r.top - gap - h : r.bottom + gap) + 'px';
    menu.style.left = Math.max(pad, Math.min(r.left, window.innerWidth - w - pad)) + 'px';
    menu.classList.toggle('up', up);
  }

  function onKey(e, dd, menu) {
    e.stopPropagation(); // 菜单里的按键不当编辑器快捷键
    const items = Array.from(menu.children);
    const i = items.indexOf(document.activeElement);
    const go = j => {
      const item = items[(j + items.length) % items.length];
      item.focus({ preventScroll: true });
      item.scrollIntoView({ block: 'nearest' });
    };
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); go(i + 1); break;
      case 'ArrowUp': e.preventDefault(); go(i - 1); break;
      case 'Home': e.preventDefault(); go(0); break;
      case 'End': e.preventDefault(); go(items.length - 1); break;
      case 'Escape': e.preventDefault(); close(true); break;
      case 'Tab': close(false); break;
      case 'Enter':
      case ' ':
        e.preventDefault();
        if (i >= 0) choose(dd, i, true);
        break;
      default: break;
    }
  }

  function choose(dd, index, keyboard) {
    close(keyboard); // 用键盘选的把焦点还给按钮；用鼠标选的不留焦点，免得之后按空格又打开菜单
    if (dd.select.selectedIndex !== index) {
      dd.select.selectedIndex = index;
      dd.select.dispatchEvent(new Event('change', { bubbles: true }));
    }
    dd.sync();
  }

  function close(refocus) {
    if (!current) return;
    const { dd, menu } = current;
    current = null;
    dd.button.setAttribute('aria-expanded', 'false');
    dd.button.classList.remove('open');
    if (refocus) dd.button.focus({ preventScroll: true });
    menu.classList.add('leaving');
    if (reduceMotion || getComputedStyle(menu).animationName === 'none') menu.remove();
    else menu.addEventListener('animationend', () => menu.remove(), { once: true });
  }

  // 点到别处、窗口变化、外面滚动时收起
  document.addEventListener('pointerdown', e => {
    if (current && !current.menu.contains(e.target) && !current.dd.button.contains(e.target)) close(false);
  }, true);
  document.addEventListener('scroll', e => { if (current && !current.menu.contains(e.target)) close(false); }, true);
  window.addEventListener('resize', () => close(false));
  window.addEventListener('blur', () => close(false));

  root.EditorDropdown = { enhance, syncAll: () => all.forEach(dd => dd.sync()), close };
})(typeof globalThis !== 'undefined' ? globalThis : this);
