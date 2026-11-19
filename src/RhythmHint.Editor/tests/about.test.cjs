// 编辑器「关于」和 Mod 设置窗口的「关于」是同一份信息：作者、仓库地址看 Mod 的 csproj，群号看 SettingsWindow.cs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const about = require('../about.js');

const mod = path.join(__dirname, '..', '..', 'RhythmHint.Mod');

test('作者和仓库地址和 Mod 的 Info.json 一致', () => {
  const csproj = fs.readFileSync(path.join(mod, 'RhythmHint.Mod.csproj'), 'utf8');
  assert.ok(csproj.includes('"Author": "' + about.author + '"'), 'csproj 里 Info.json 的 Author');
  assert.ok(csproj.includes('<RepoUrl>' + about.repo + '</RepoUrl>'), 'csproj 里的 RepoUrl');
});

test('群号和 Mod 设置窗口一致', () => {
  const window = fs.readFileSync(path.join(mod, 'SettingsWindow.cs'), 'utf8');
  assert.ok(window.includes('QqGroup = "' + about.qq + '"'));
});

test('作者和贡献者都有名字和主页', () => {
  assert.ok(about.repo.startsWith(about.authorUrl + '/'), '仓库在作者名下');
  assert.ok(about.contributors.length > 0);
  for (const c of about.contributors) {
    assert.deepEqual(Object.keys(c).sort(), ['name', 'url']);
    assert.ok(c.name && /^https:\/\/github\.com\//.test(c.url));
  }
});
