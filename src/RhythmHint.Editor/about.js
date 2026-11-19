/* 编辑器「关于」里的作者、贡献者、仓库和交流群。和 Mod 设置窗口「状态」页的「关于」是同一份信息：
 * 作者和仓库地址以 src/RhythmHint.Mod/RhythmHint.Mod.csproj（Info.json 的 Author、RepoUrl）为准，群号以 SettingsWindow.cs 的 QqGroup 为准，
 * tests/about.test.cjs 核对两边一致，改一处漏一处会报错。 */
(function (root) {
  'use strict';

  const about = {
    author: 'XIAOMING6680',
    authorUrl: 'https://github.com/xiaoming6680',
    // 2026-11-18 用户：编辑器里写上 PR 的作者（不写 PR 编号）
    contributors: [{ name: 'Linxi-1105', url: 'https://github.com/Linxi-1105' }],
    repo: 'https://github.com/xiaoming6680/ADOFAI-RhythmHint',
    qq: '1136407588',
  };

  if (typeof module === 'object' && module.exports) module.exports = about;
  root.EditorAbout = about;
})(typeof globalThis !== 'undefined' ? globalThis : this);
