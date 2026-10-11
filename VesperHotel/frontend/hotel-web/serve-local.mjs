import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const port = Number(process.env.PORT || 5173);
const apiTarget = new URL(process.env.API_URL || 'http://127.0.0.1:5080');
const dist = path.resolve('dist');
const types = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg', '.png': 'image/png', '.webp': 'image/webp', '.ico': 'image/x-icon'
};

function proxy(request, response) {
  const upstream = http.request({
    hostname: apiTarget.hostname,
    port: apiTarget.port,
    path: request.url,
    method: request.method,
    headers: { ...request.headers, host: apiTarget.host }
  }, result => {
    response.writeHead(result.statusCode || 502, result.headers);
    result.pipe(response);
  });
  upstream.on('error', () => {
    response.writeHead(502, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ code: 'api_unavailable', message: 'The local API is unavailable.' }));
  });
  request.pipe(upstream);
}

const server = http.createServer((request, response) => {
  if (request.url?.startsWith('/api/')) {
    proxy(request, response);
    return;
  }

  const pathname = decodeURIComponent(new URL(request.url || '/', 'http://localhost').pathname);
  const requested = path.resolve(dist, '.' + pathname);
  const safePath = requested.startsWith(dist + path.sep) ? requested : path.join(dist, 'index.html');
  const file = fs.existsSync(safePath) && fs.statSync(safePath).isFile()
    ? safePath
    : path.join(dist, 'index.html');
  response.writeHead(200, { 'content-type': types[path.extname(file)] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(response);
});

server.listen(port, '127.0.0.1', () => console.log(`VESPER web is ready at http://127.0.0.1:${port}`));
