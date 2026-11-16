const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', {fatal: true});
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
export const fromBase64 = value => Uint8Array.from(atob(value), c => c.charCodeAt(0));
export const toBase64 = bytes => btoa(String.fromCharCode(...bytes));
const hex = bytes => Array.from(bytes, x => x.toString(16).padStart(2, '0')).join('');

export function parseConnection(code, origin) {
  try {
    const value = code.trim();
    if (!value.startsWith('starfleet-v1:') || value.length > 8192) throw new Error();
    const settings = JSON.parse(decoder.decode(fromBase64(value.slice(13))));
    const url = new URL(settings.serverUrl);
    if (url.origin !== origin || url.pathname !== '/' || url.search || url.hash || url.username || url.password) {
      throw new Error('This code belongs to a different library address. Open the address configured in your desktop app.');
    }
    if (!uuid.test(settings.vaultId) || settings.vaultId === '00000000-0000-0000-0000-000000000000' ||
        !/^[A-Za-z0-9+/]{43}=$/.test(settings.deviceToken) || fromBase64(settings.encryptionKey).length !== 32) throw new Error();
    return settings;
  } catch (error) {
    if (error.message.startsWith('This code')) throw error;
    throw new Error('Paste the complete connection code from Settings in the Windows app.');
  }
}

export async function vaultCrypto(settings) {
  if (!uuid.test(settings.vaultId)) throw new Error('Invalid vault.');
  const raw = fromBase64(settings.encryptionKey);
  if (raw.length !== 32) throw new Error('Invalid library key.');
  const aes = await crypto.subtle.importKey('raw', raw, 'AES-GCM', false, ['decrypt']);
  const hmac = await crypto.subtle.importKey('raw', raw, {name: 'HMAC', hash: 'SHA-256'}, false, ['sign']);
  raw.fill(0);
  const aad = purpose => encoder.encode(`starfleet-v1/${settings.vaultId}/${purpose}`);
  async function decrypt(envelope, purpose) {
    if (envelope.length < 29 || envelope[0] !== 1) throw new Error('Invalid encrypted envelope.');
    // .NET stores version | nonce | tag | ciphertext; Web Crypto expects ciphertext | tag.
    const body = new Uint8Array(envelope.length - 13);
    body.set(envelope.subarray(29));
    body.set(envelope.subarray(13, 29), envelope.length - 29);
    return new Uint8Array(await crypto.subtle.decrypt({name: 'AES-GCM', iv: envelope.subarray(1, 13), additionalData: aad(purpose), tagLength: 128}, aes, body));
  }
  return {
    async entry(entry) {
      if (!uuid.test(entry.id) || !Number.isSafeInteger(entry.revision) || entry.revision < 1) throw new Error('Invalid edition revision.');
      const payload = JSON.parse(decoder.decode(await decrypt(fromBase64(entry.ciphertext), `entry/${entry.id}/${entry.revision}`)));
      if (payload.book != null && (payload.book.id !== entry.id || typeof payload.book.title !== 'string' || !payload.book.title.trim())) throw new Error('Invalid edition.');
      if (payload.cover != null && !/^[a-f0-9]{64}$/.test(payload.cover)) throw new Error('Invalid cover reference.');
      return {...entry, book: payload.book, cover: payload.cover};
    },
    async coverId(hash) {
      if (!/^[a-f0-9]{64}$/.test(hash)) throw new Error('Invalid cover reference.');
      return hex(new Uint8Array(await crypto.subtle.sign('HMAC', hmac, aad('cover-id/' + hash))));
    },
    async cover(bytes, id, hash) {
      const plain = await decrypt(bytes, 'cover/' + id);
      if (hex(new Uint8Array(await crypto.subtle.digest('SHA-256', plain))) !== hash) throw new Error('Cover checksum mismatch.');
      return plain;
    },
  };
}

const iterations = 600000;
async function passwordKey(password, salt) {
  const material = await crypto.subtle.importKey('raw', encoder.encode(password), 'PBKDF2', false, ['deriveKey']);
  return crypto.subtle.deriveKey({name: 'PBKDF2', salt, iterations, hash: 'SHA-256'}, material, {name: 'AES-GCM', length: 256}, false, ['encrypt', 'decrypt']);
}
export async function sealConnection(settings, password, origin) {
  if (password.length < 10) throw new Error('Use a browser password of at least 10 characters.');
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await passwordKey(password, salt);
  const encrypted = await crypto.subtle.encrypt({name: 'AES-GCM', iv, additionalData: encoder.encode('starfleet-reader-v1/' + origin)}, key, encoder.encode(JSON.stringify(settings)));
  return JSON.stringify({version: 1, salt: toBase64(salt), iv: toBase64(iv), ciphertext: toBase64(new Uint8Array(encrypted))});
}
export async function openConnection(value, password, origin) {
  try {
    const saved = JSON.parse(value);
    if (saved.version !== 1 || fromBase64(saved.salt).length !== 16 || fromBase64(saved.iv).length !== 12 || value.length > 16384) throw new Error();
    const key = await passwordKey(password, fromBase64(saved.salt));
    const bytes = await crypto.subtle.decrypt({name: 'AES-GCM', iv: fromBase64(saved.iv), additionalData: encoder.encode('starfleet-reader-v1/' + origin)}, key, fromBase64(saved.ciphertext));
    const settings = JSON.parse(decoder.decode(bytes));
    if (!uuid.test(settings.vaultId) || !/^[A-Za-z0-9+/]{43}=$/.test(settings.token) || fromBase64(settings.encryptionKey).length !== 32) throw new Error();
    return settings;
  } catch {
    throw new Error('Could not unlock this browser. Check your password, or forget this browser and pair again.');
  }
}
