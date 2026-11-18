// Package a validated review and the local editor as one portable HTML file.
// Personal charts should be written under the ignored Charts/ directory.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const C = require('./review-editor/core.js');

function build(input, output) {
  const chart = C.validate(JSON.parse(fs.readFileSync(input, 'utf8').replace(/^\uFEFF/, '')));
  const directory = path.join(__dirname, 'review-editor');
  const read = name => fs.readFileSync(path.join(directory, name), 'utf8');
  const script = source => '<script>\n' + source.replace(/<\/script/gi, '<\\/script') + '\n</script>';
  let html = read('index.html');
  const cssReference = '<link rel="stylesheet" href="style.css">';
  if (!html.includes(cssReference)) throw new Error('Editor stylesheet reference changed; update the packager.');
  html = html.replace(cssReference, () => '<style>\n' + read('style.css') + '\n</style>');
  for (const name of ['core.js', 'studio-model.js', 'studio-audio.js', 'note-planner.js', 'adofai-import.js', 'demo.js', 'app.js']) {
    const reference = '<script src="' + name + '"></script>';
    if (!html.includes(reference)) throw new Error('Editor script reference changed: ' + name);
    const source = name === 'demo.js'
      ? 'window.RHYTHMHINT_DEMO = ' + JSON.stringify(chart).replace(/</g, '\\u003c') + ';'
      : read(name);
    html = html.replace(reference, () => script(source));
  }
  // A personal package may contain an in-progress review as well as a baseline.
  html = html.replace(/(<button\b[^>]*\bid="demo"[^>]*>)打开演示/, (_, openingTag) => openingTag + '打开内置谱面');
  const resolved = path.resolve(output);
  if (resolved === path.resolve(input)) throw new Error('Output must differ from the review JSON input.');
  fs.mkdirSync(path.dirname(resolved), { recursive: true });
  fs.writeFileSync(resolved, html, 'utf8');
  return resolved;
}

if (require.main === module) {
  if (process.argv.length !== 4) {
    console.error('Usage: node tools/build-review-editor.cjs <review.json> <output.html>');
    process.exitCode = 2;
  } else {
    try { console.log(build(process.argv[2], process.argv[3])); }
    catch (error) { console.error(error.message); process.exitCode = 1; }
  }
}
module.exports = { build };
