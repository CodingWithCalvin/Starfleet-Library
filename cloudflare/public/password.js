import {fromBase64, toBase64} from './crypto.js';

const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', {fatal: true});
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const context = (info, origin) => encoder.encode(`starfleet-password-v1/${origin}/${info.vaultId}/${info.id}`);

export async function deriveLogin(password, info, origin) {
  if (info.version !== 1 || !uuid.test(info.id) || !uuid.test(info.vaultId) || fromBase64(info.salt).length !== 16) throw new Error('Invalid password sign-in configuration.');
  const material = await crypto.subtle.importKey('raw', encoder.encode(password), 'PBKDF2', false, ['deriveBits']);
  const seed = new Uint8Array(await crypto.subtle.deriveBits({name: 'PBKDF2', salt: fromBase64(info.salt), iterations: 600000, hash: 'SHA-256'}, material, 256));
  const root = await crypto.subtle.importKey('raw', seed, 'HKDF', false, ['deriveBits', 'deriveKey']);
  seed.fill(0);
  const parameters = {name: 'HKDF', hash: 'SHA-256', salt: context(info, origin)};
  // Independent keys: the sign-in proof sent to the server cannot unwrap the library key.
  const authBytes = new Uint8Array(await crypto.subtle.deriveBits({...parameters, info: encoder.encode('authentication')}, root, 256));
  const auth = toBase64(authBytes);
  authBytes.fill(0);
  const wrappingKey = await crypto.subtle.deriveKey({...parameters, info: encoder.encode('key-wrapping')}, root, {name: 'AES-GCM', length: 256}, false, ['encrypt', 'decrypt']);
  return {auth, wrappingKey};
}

export async function preparePasswordLogin(settings, password, origin, expectedId) {
  if (password.length < 14 || password.length > 1024) throw new Error('Use a unique library password of at least 14 characters.');
  const info = {version: 1, id: crypto.randomUUID(), vaultId: settings.vaultId, salt: toBase64(crypto.getRandomValues(new Uint8Array(16)))};
  const keys = await deriveLogin(password, info, origin);
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const plain = encoder.encode(JSON.stringify({vaultId: settings.vaultId, encryptionKey: settings.encryptionKey}));
  const encrypted = new Uint8Array(await crypto.subtle.encrypt({name: 'AES-GCM', iv: nonce, additionalData: context(info, origin)}, keys.wrappingKey, plain));
  plain.fill(0);
  const envelope = new Uint8Array(13 + encrypted.length);
  envelope[0] = 1;
  envelope.set(nonce, 1);
  envelope.set(encrypted, 13);
  const authHash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', encoder.encode(keys.auth))), b => b.toString(16).padStart(2, '0')).join('');
  return {info, keys, setup: {version: 1, id: info.id, salt: info.salt, authHash, wrappedKey: toBase64(envelope), expectedId}};
}

export async function unlockPasswordKey(wrappedKey, keys, info, origin) {
  try {
    const envelope = fromBase64(wrappedKey);
    if (envelope.length < 61 || envelope[0] !== 1 || envelope.length > 768) throw new Error();
    const plain = new Uint8Array(await crypto.subtle.decrypt({name: 'AES-GCM', iv: envelope.subarray(1, 13), additionalData: context(info, origin)}, keys.wrappingKey, envelope.subarray(13)));
    const settings = JSON.parse(decoder.decode(plain));
    plain.fill(0);
    if (settings.vaultId !== info.vaultId || fromBase64(settings.encryptionKey).length !== 32 || Object.keys(settings).sort().join(',') !== 'encryptionKey,vaultId') throw new Error();
    return settings;
  } catch {
    throw new Error('Could not unlock the library key. Check your password or reset password sign-in using your recovery connection code.');
  }
}
