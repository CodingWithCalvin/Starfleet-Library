import {json, digest, tokenHashes, configuredVault, createSession} from './reader.js';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const equal = (a, b) => {
  if (a.length !== b.length) return false;
  let difference = 0;
  for (let i = 0; i < a.length; i++) difference |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return difference === 0;
};
const configuredLogin = (login, env) => login && configuredVault(env) && login.vault_id === env.VAULT_ID && tokenHashes(env).includes(login.device_hash);
const loadLogin = env => env.DB.prepare('SELECT * FROM reader_login WHERE singleton=1').first();

export async function loginInfo(env) {
  const login = await loadLogin(env);
  if (!configuredLogin(login, env)) return json({enabled: false, id: login?.id ?? null});
  // Salt and identifiers are public; the encrypted key and verifier are not.
  return json({enabled: true, version: 1, id: login.id, vaultId: login.vault_id, salt: login.salt});
}

export async function configureLogin(env, deviceHash, data) {
  if (Object.keys(data).sort().join(',') !== 'authHash,expectedId,id,salt,version,wrappedKey' || data.version !== 1 ||
      !uuid.test(data.id ?? '') || (data.expectedId !== null && !uuid.test(data.expectedId ?? '')) || data.id === data.expectedId ||
      !/^[A-Za-z0-9+/]{22}==$/.test(data.salt ?? '') || !/^[a-f0-9]{64}$/.test(data.authHash ?? '') ||
      typeof data.wrappedKey !== 'string' || data.wrappedKey.length > 1024 || !/^[A-Za-z0-9+/]+={0,2}$/.test(data.wrappedKey)) return json({error: 'Invalid password setup'}, 400);
  const wrapped = atob(data.wrappedKey);
  if (wrapped.length < 29 + 32 || wrapped.charCodeAt(0) !== 1) return json({error: 'Invalid encrypted key'}, 400);
  // Compare-and-set prevents two setup pages from silently replacing each other.
  const updated = data.expectedId === null
    ? await env.DB.prepare('INSERT INTO reader_login(singleton,id,vault_id,device_hash,salt,auth_hash,wrapped_key) VALUES(1,?,?,?,?,?,?) ON CONFLICT(singleton) DO NOTHING RETURNING id')
        .bind(data.id, env.VAULT_ID, deviceHash, data.salt, data.authHash, data.wrappedKey).first()
    : await env.DB.prepare('UPDATE reader_login SET id=?,vault_id=?,device_hash=?,salt=?,auth_hash=?,wrapped_key=? WHERE singleton=1 AND id=? RETURNING id')
        .bind(data.id, env.VAULT_ID, deviceHash, data.salt, data.authHash, data.wrappedKey, data.expectedId).first();
  if (!updated) return json({error: 'Password setup changed in another browser. Reload and try again.'}, 409);
  return json({configured: true});
}

export async function passwordLogin(request, env, data) {
  if (!env.LOGIN_RATE_LIMITER) return json({error: 'Sign-in is not configured'}, 503);
  const {success} = await env.LOGIN_RATE_LIMITER.limit({key: `${env.VAULT_ID ?? 'unconfigured'}:${request.headers.get('CF-Connecting-IP') ?? 'local'}`});
  if (!success) return new Response(JSON.stringify({error: 'Too many attempts. Wait a minute and try again.'}), {
    status: 429, headers: {...Object.fromEntries(json({}).headers), 'Retry-After': '60'},
  });
  if (Object.keys(data).sort().join(',') !== 'auth,id' || !uuid.test(data.id ?? '') || !/^[A-Za-z0-9+/]{43}=$/.test(data.auth ?? '')) return json({error: 'Incorrect library password'}, 401);
  const login = await loadLogin(env);
  if (!configuredLogin(login, env) || login.id !== data.id || !equal(await digest(data.auth), login.auth_hash)) return json({error: 'Incorrect library password'}, 401);
  const session = await (await createSession(env, login.device_hash, login.id)).json();
  return json({...session, wrappedKey: login.wrapped_key});
}
