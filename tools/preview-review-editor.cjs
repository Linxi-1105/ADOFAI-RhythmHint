// Local preview exposes only the editor's fixed public assets and synthetic demo.
// Personal charts, music, and all other workspace paths are not served.
'use strict';
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const directory = path.join(__dirname, 'review-editor');
const names = new Set(['index.html', 'style.css', 'core.js', 'studio-model.js', 'studio-audio.js', 'note-planner.js', 'adofai-import.js', 'demo.js', 'app.js']);
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
http.createServer((req, res) => {
  const name = (req.url || '/').split('?')[0].slice(1) || 'index.html';
  if (req.method !== 'GET' || !names.has(name)) { res.writeHead(404); res.end('Not found'); return; }
  res.writeHead(200, { 'Content-Type': types[path.extname(name)] + '; charset=utf-8', 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
  fs.createReadStream(path.join(directory, name)).pipe(res);
}).listen(18742, '127.0.0.1', () => console.log('Review editor preview: http://127.0.0.1:18742'));
