const encoder = new TextEncoder();
const tokenPattern = /^Bearer ([A-Za-z0-9+/]{43}=)$/;
const hashPattern = /^[a-f0-9]{64}$/;
const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
export const digest = async value => Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(value))), b => b.toString(16).padStart(2, '0')).join('');
export const tokenHashes = env => (env.DEVICE_TOKEN_HASHES ?? '').split(',').map(x => x.trim()).filter(x => hashPattern.test(x));
export const configuredVault = env => uuidPattern.test(env.VAULT_ID ?? '');
export const bearer = request => request.headers.get('Authorization')?.match(tokenPattern)?.[1];

export const securityHeaders = {
  'Cache-Control': 'no-store',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'no-referrer',
  'X-Frame-Options': 'DENY',
  'Cross-Origin-Resource-Policy': 'same-origin',
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
  'Content-Security-Policy': "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'; object-src 'none'",
};
export const json = (body, status = 200) => Response.json(body, {status, headers: securityHeaders});

export function sameOrigin(request, requireOrigin = false) {
  const origin = request.headers.get('Origin');
  const site = request.headers.get('Sec-Fetch-Site');
  return (!requireOrigin || origin !== null) &&
    (origin === null || origin === new URL(request.url).origin) &&
    (site === null || site === 'same-origin' || site === 'none');
}

export async function readerSession(request, env) {
  const token = bearer(request);
  if (!token || !configuredVault(env) || request.headers.get('X-Starfleet-Vault') !== env.VAULT_ID) return null;
  const tokenHash = await digest(token);
  const session = await env.DB.prepare('SELECT device_hash, vault_id, expires_at FROM reader_sessions WHERE token_hash=?').bind(tokenHash).first();
  if (!session || session.expires_at <= Math.floor(Date.now() / 1000) || session.vault_id !== env.VAULT_ID || !tokenHashes(env).includes(session.device_hash)) return null;
  return tokenHash;
}

export async function createSession(env, deviceHash) {
  const token = btoa(String.fromCharCode(...crypto.getRandomValues(new Uint8Array(32))));
  const now = Math.floor(Date.now() / 1000);
  const expiresAt = now + 30 * 24 * 60 * 60;
  await env.DB.prepare('DELETE FROM reader_sessions WHERE expires_at<=?').bind(now).run();
  await env.DB.prepare('INSERT INTO reader_sessions(token_hash,device_hash,vault_id,expires_at) VALUES(?,?,?,?)')
    .bind(await digest(token), deviceHash, env.VAULT_ID, expiresAt).run();
  return json({token, expiresAt});
}

const publicPaths = new Set(['/', '/index.html', '/app.js', '/crypto.js', '/catalog.js', '/styles.css', '/logo.svg']);
export async function publicAsset(request, env) {
  if (!publicPaths.has(new URL(request.url).pathname)) return null;
  if (request.method !== 'GET' && request.method !== 'HEAD') return json({error: 'Method not allowed'}, 405);
  if (!env.ASSETS) return json({error: 'Reader assets unavailable'}, 503);
  const response = await env.ASSETS.fetch(request);
  const headers = new Headers(response.headers);
  for (const [name, value] of Object.entries(securityHeaders)) headers.set(name, value);
  return new Response(response.body, {status: response.status, headers});
}
