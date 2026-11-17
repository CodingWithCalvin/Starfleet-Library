-- A limited public projection; no notes, reading history, ratings or credentials.
CREATE TABLE public_editions (
  id TEXT PRIMARY KEY NOT NULL,
  revision INTEGER NOT NULL CHECK (revision > 0),
  book_json TEXT,
  cover TEXT
);
