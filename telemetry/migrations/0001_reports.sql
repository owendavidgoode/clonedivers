CREATE TABLE devices (
  id TEXT PRIMARY KEY,
  token_hash TEXT NOT NULL,
  nickname TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  revoked INTEGER NOT NULL DEFAULT 0,
  quota_day TEXT NOT NULL DEFAULT '',
  quota_bytes INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE chunks (
  id TEXT PRIMARY KEY,
  device_id TEXT NOT NULL REFERENCES devices(id),
  session_id TEXT NOT NULL,
  received_utc TEXT NOT NULL,
  first_utc TEXT NOT NULL,
  last_utc TEXT NOT NULL,
  records INTEGER NOT NULL,
  bytes INTEGER NOT NULL,
  sha256 TEXT NOT NULL,
  object_key TEXT NOT NULL
);
CREATE INDEX chunks_device_time ON chunks(device_id, received_utc);
CREATE INDEX chunks_session ON chunks(session_id, first_utc);
CREATE INDEX chunks_received ON chunks(received_utc);
