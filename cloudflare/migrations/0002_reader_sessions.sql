-- Browser credentials are read-only and expire independently of native devices.
-- Neither the raw browser token nor the catalog decryption key is stored here.
CREATE TABLE reader_sessions (
  token_hash TEXT PRIMARY KEY NOT NULL,
  device_hash TEXT NOT NULL,
  vault_id TEXT NOT NULL,
  expires_at INTEGER NOT NULL
);
CREATE INDEX reader_sessions_expiry ON reader_sessions(expires_at);
