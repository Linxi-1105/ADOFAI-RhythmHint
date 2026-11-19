// 编辑器五种语言：每句都有五种译文、占位符一致；界面代码和页面里用到的键都存在；界面代码里没有写死的中文。
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { STRINGS, LANGUAGES, create, fromBrowser } = require('../i18n.js');

const dir = path.join(__dirname, '..');
const read = name => fs.readFileSync(path.join(dir, name), 'utf8');
const placeholders = s => (s.match(/\{\d+\}/g) || []).sort().join();

test('every sentence has all five languages with the same placeholders', () => {
  for (const [key, row] of Object.entries(STRINGS)) {
    assert.equal(row.length, LANGUAGES.length, key);
    row.forEach((text, i) => {
      assert.ok(typeof text === 'string' && text.trim().length > 0, key + ' #' + i);
      assert.equal(placeholders(text), placeholders(row[0]), key + ' #' + i);
    });
  }
});

test('keys used by the page and the code exist', () => {
  const used = new Set();
  const html = read('index.html');
  for (const m of html.matchAll(/data-i18n(?:-title|-placeholder)?="([^"]+)"/g)) used.add(m[1]);
  for (const file of ['app.js', 'view.js', 'track.js']) {
    for (const m of read(file).matchAll(/\bt\('([a-z]+\.[A-Za-z0-9]+)'/g)) used.add(m[1]);
  }
  // 拼出来的键
  ['rail.file', 'rail.refs', 'rail.audio', 'rail.view', 'rail.keys', 'kind.1', 'kind.2', 'kind.3', 'kind.4', 'col.standard', 'col.alternate',
    'err.notFingering', 'err.tooNew', 'err.badFile', 'err.notJson', 'err.notSession', 'about.contributor', 'track.shrink',
    'draft.restored', 'draft.restoredSaved', 'history.saved', 'history.unsaved'].forEach(k => used.add(k));
  const missing = [...used].filter(k => !STRINGS[k]);
  assert.deepEqual(missing, []);
});

test('no hard-coded Chinese in the interface code', () => {
  for (const file of ['app.js', 'view.js', 'track.js', 'audio.js']) {
    const code = read(file).split('\n').filter(line => !/^\s*(\/\/|\*|\/\*)/.test(line)).map(line => line.replace(/\/\/.*$/, '').replace(/\/\*.*?\*\//g, ''));
    const hits = code.filter(line => /['"`][^'"`]*[一-鿿][^'"`]*['"`]/.test(line));
    assert.deepEqual(hits, [], file);
  }
});

test('language choice and formatting', () => {
  assert.equal(fromBrowser('zh-CN'), 'zh-Hans');
  assert.equal(fromBrowser('zh-TW'), 'zh-Hant');
  assert.equal(fromBrowser('ko-KR'), 'ko');
  assert.equal(fromBrowser('ja'), 'ja');
  assert.equal(fromBrowser('fr-FR'), 'en');
  assert.equal(create('en')('insp.count', 3), '3 notes selected');
  assert.equal(create('xx')('insp.count', 3), '3 notes selected');
});
