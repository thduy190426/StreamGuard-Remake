// node tests/clipboard-browser-tests.cjs <C# test artifact folder> [path to playwright]
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const { chromium } = require(process.argv[3] || 'playwright');
const scripts = process.argv[2];
let checks = 0;
function check(value, message) { checks++; assert.ok(value, message); }

const fixture = `<!doctype html><html><head><meta charset="utf-8">
<style>.blocked { user-select: none !important; } input { display:block; margin:12px; }</style>
</head><body class="blocked">
<p><span id="source" style="user-select:none!important">StreamGuard</span></p>
<p id="unicode">Tiếng Việt — kiểm tra sao chép</p>
<input id="target"><textarea id="textarea"></textarea><div id="editor" contenteditable="true">Editable</div>
<input id="readonly" readonly value="unchanged"><input id="disabled" disabled value="unchanged">
<iframe id="frame" srcdoc="<input id='frameInput'><script>document.addEventListener('paste', e => e.preventDefault(), true)<\/script>"></iframe>
<script>
window.savedHandlers = {};
for (const type of ['copy','cut','paste','selectstart','contextmenu','beforeinput','keydown','keyup']) {
  const blocker = event => {
    const shortcut = (event.ctrlKey || event.metaKey) && ['a','c','v','x','s','insert'].includes((event.key || '').toLowerCase());
    if (!['keydown','keyup'].includes(type) || shortcut) {
      event.preventDefault(); event.stopPropagation(); event.stopImmediatePropagation(); event.returnValue = false;
    }
  };
  window.addEventListener(type, blocker, true);
  document.addEventListener(type, blocker, true);
}
window.oncopy = window.savedHandlers.copy = () => false;
document.onpaste = () => false;
window.originalMethods = ['preventDefault','stopPropagation','stopImmediatePropagation'].map(k => Event.prototype[k]);
window.originalReturnValue = Object.getOwnPropertyDescriptor(Event.prototype, 'returnValue');
</script></body></html>`;

(async () => {
  const server = http.createServer((req, res) => { res.writeHead(200, {'Content-Type':'text/html; charset=utf-8'}); res.end(fixture); });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  let browser;
  try {
    browser = await chromium.launch({ channel: 'chrome', headless: true });
    const context = await browser.newContext();
    const page = await context.newPage();
    page.on('pageerror', error => console.error('Fixture page error: ' + error.message));
    const second = await context.newPage();
    const url = `http://127.0.0.1:${server.address().port}/`;
    await second.goto(url);
    for (const language of ['en', 'vi']) {
      await page.goto(url);
      const script = fs.readFileSync(path.join(scripts, `copy-paste-${language}.js`), 'utf8');
      const custom = await context.newPage();
      await custom.setContent('<input id="input">');
      await custom.evaluate(() => {
        window.calls = {key:0, input:0};
        window.savedKey = window.onkeydown = () => { window.calls.key++; return false; };
        window.savedInput = window.onbeforeinput = () => { window.calls.input++; return false; };
      });
      await custom.evaluate(script);
      check(!(await custom.evaluate(() => document.querySelector('input').dispatchEvent(new KeyboardEvent('keydown', {key:'k',bubbles:true,cancelable:true})))), 'Unrelated window.onkeydown keeps its original default cancellation');
      check(await custom.evaluate(() => window.calls.key === 1), 'Unrelated window keyboard handler is still called');
      check(await custom.evaluate(() => document.querySelector('input').dispatchEvent(new KeyboardEvent('keydown', {key:'v',ctrlKey:true,bubbles:true,cancelable:true}))), 'Protected paste shortcut bypasses a window property handler');
      check(!(await custom.evaluate(() => document.querySelector('input').dispatchEvent(new InputEvent('beforeinput', {inputType:'insertText',bubbles:true,cancelable:true})))), 'Normal input handler is preserved');
      check(await custom.evaluate(() => window.calls.input === 1), 'Normal beforeinput still reaches its original handler');
      check(await custom.evaluate(() => document.querySelector('input').dispatchEvent(new InputEvent('beforeinput', {inputType:'insertFromPaste',bubbles:true,cancelable:true}))), 'Paste input is unlocked');
      await custom.evaluate(() => window.__streamGuardClipboardUnlock.disable());
      check(await custom.evaluate(() => window.onkeydown === window.savedKey && window.onbeforeinput === window.savedInput), 'Disable restores the exact original window property handlers');
      await custom.close();
      const dispatch = (type, props = {}) => page.evaluate(({type, props}) => {
        const event = type.startsWith('key') ? new KeyboardEvent(type, {bubbles:true,cancelable:true,...props})
          : new Event(type, {bubbles:true,cancelable:true});
        return document.querySelector('#target').dispatchEvent(event);
      }, {type, props});
      check(!(await dispatch('paste')), `${language}: fixture initially blocks paste`);
      check(await page.locator('#source').evaluate(e => getComputedStyle(e).userSelect) === 'none', 'Fixture initially blocks selection');
      await page.evaluate(script);
      for (const type of ['copy','cut','paste','selectstart','contextmenu']) {
        check(await dispatch(type), `${language}: ${type} native action is not cancelled`);
      }
      check(await dispatch('keydown', {key:'v',ctrlKey:true}), 'Ctrl+V default action survives an earlier window blocker');
      check(await dispatch('keyup', {key:'c',metaKey:true}), 'Cmd+C default action survives');
      check(!(await dispatch('keydown', {key:'s',ctrlKey:true})), 'Unrelated keyboard shortcuts keep their original behavior');
      check(await page.locator('#source').evaluate(e => getComputedStyle(e).userSelect) === 'text', 'Inline CSS selection lock is removed');
      check(await page.locator('#readonly').evaluate(e => e.readOnly) && await page.locator('#disabled').isDisabled(), 'Read-only and disabled fields remain unchanged');

      await page.locator('#source').dblclick();
      check(await page.evaluate(() => window.getSelection().toString()) === 'StreamGuard', 'Mouse selection works');
      await page.keyboard.press('Control+c');
      await page.locator('#target').click();
      await page.keyboard.press('Control+v');
      check(await page.locator('#target').inputValue() === 'StreamGuard', 'Real native Ctrl+C / Ctrl+V inserts selected text');
      await page.keyboard.press('Control+a'); await page.keyboard.press('Control+x');
      check(await page.locator('#target').inputValue() === '', 'Real native Cut works');
      await page.locator('#textarea').click(); await page.keyboard.press('Control+v');
      check(await page.locator('#textarea').inputValue() === 'StreamGuard', 'Textarea accepts native paste');
      await page.locator('#editor').click(); await page.keyboard.press('Control+a'); await page.keyboard.press('Control+v');
      check(await page.locator('#editor').innerText() === 'StreamGuard', 'Contenteditable accepts native paste');
      await page.locator('#unicode').evaluate(e => { const range=document.createRange();range.selectNodeContents(e);const selection=getSelection();selection.removeAllRanges();selection.addRange(range); });
      await page.keyboard.press('Control+c'); await page.locator('#target').click(); await page.keyboard.press('Control+a'); await page.keyboard.press('Control+v');
      check(await page.locator('#target').inputValue() === 'Tiếng Việt — kiểm tra sao chép', 'Unicode survives real copy and paste');

      check(await page.evaluate(() => {
        const state = window.__streamGuardClipboardUnlock; window.testState = state; return state.enabled;
      }), 'Current-page state is available');
      await page.evaluate(script);
      check(await page.evaluate(() => window.testState === window.__streamGuardClipboardUnlock), 'Re-running is idempotent');
      await page.evaluate(() => { const node=document.createElement('p');node.id='dynamic';node.style.setProperty('user-select','none','important');node.textContent='dynamic';document.body.appendChild(node); });
      await page.waitForFunction(() => getComputedStyle(document.querySelector('#dynamic')).userSelect === 'text');
      check(true, 'Dynamically added content becomes selectable');
      await page.locator('#source').evaluate(e => e.style.setProperty('user-select','none','important'));
      await page.waitForFunction(() => getComputedStyle(document.querySelector('#source')).userSelect === 'text');
      check(true, 'A page reapplying its inline selection lock is handled');
      check(await second.evaluate(() => !window.__streamGuardClipboardUnlock), 'Other tabs stay untouched');
      const frame = page.frames().find(f => f.parentFrame());
      check(!(await frame.evaluate(() => document.querySelector('#frameInput').dispatchEvent(new Event('paste',{bubbles:true,cancelable:true})))), 'Embedded frames are not silently changed');
      await frame.evaluate(script);
      check(await frame.evaluate(() => document.querySelector('#frameInput').dispatchEvent(new Event('paste',{bubbles:true,cancelable:true}))), 'Script works when explicitly run inside the frame');

      await page.evaluate(fs.readFileSync(path.join(scripts, 'copy-paste-disable.js'), 'utf8'));
      check(!(await dispatch('paste')), 'Disable restores page clipboard blocking');
      check(await page.locator('#source').evaluate(e => e.style.getPropertyValue('user-select') === 'none' && e.style.getPropertyPriority('user-select') === 'important'), 'Disable restores original inline CSS');
      check(await page.evaluate(() => ['preventDefault','stopPropagation','stopImmediatePropagation'].every((key,i)=>Event.prototype[key]===window.originalMethods[i])
        && Object.getOwnPropertyDescriptor(Event.prototype,'returnValue').set===window.originalReturnValue.set
        && window.oncopy===window.savedHandlers.copy && !window.__streamGuardClipboardUnlock), 'Disable restores original prototypes and window handlers');
      check(await frame.evaluate(() => window.__streamGuardClipboardUnlock.enabled), 'Disabling the top page does not change a frame');
      const bookmarklet = fs.readFileSync(path.join(scripts, `copy-paste-${language}-bookmarklet.txt`), 'utf8');
      await page.evaluate(url => { const link=document.createElement('a');link.id='bookmarklet';link.href=url;link.textContent='Unlock';document.body.appendChild(link); }, bookmarklet);
      await page.locator('#bookmarklet').click();
      await page.waitForFunction(() => window.__streamGuardClipboardUnlock?.enabled, null, {timeout:5000});
      check(await dispatch('paste'), 'Encoded bookmarklet enables the same behavior');
      check(page.url() === url && await page.locator('#source').count() === 1, 'Bookmarklet does not navigate away or replace page content');
      await page.reload();
      check(!(await dispatch('paste')) && await page.evaluate(() => !window.__streamGuardClipboardUnlock), 'Reload removes the unlock');
    }
    console.log(`PASS: ${checks} browser checks (Chrome, native clipboard shortcuts, English + Vietnamese).`);
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
