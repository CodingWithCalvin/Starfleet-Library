-- A password-wrapped library key enables sign-in from a new browser.
-- The password, unwrap key, native token, and plaintext library key are never stored.
CREATE TABLE reader_login (
  singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
  id TEXT NOT NULL UNIQUE,
  vault_id TEXT NOT NULL,
  device_hash TEXT NOT NULL,
  salt TEXT NOT NULL,
  auth_hash TEXT NOT NULL,
  wrapped_key TEXT NOT NULL
);
ALTER TABLE reader_sessions ADD COLUMN login_id TEXT;
