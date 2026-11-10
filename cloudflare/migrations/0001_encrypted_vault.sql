CREATE TABLE vault_entries (id TEXT PRIMARY KEY NOT NULL, revision INTEGER NOT NULL CHECK (revision > 0), ciphertext TEXT NOT NULL);
