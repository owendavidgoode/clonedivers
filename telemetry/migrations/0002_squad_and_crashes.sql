CREATE TABLE squad_sessions (
  id TEXT PRIMARY KEY,
  received_utc TEXT NOT NULL,
  player TEXT NOT NULL,
  host_label TEXT NOT NULL DEFAULT '',
  launcher_version TEXT NOT NULL,
  match_hash TEXT NOT NULL DEFAULT '',
  mode TEXT NOT NULL DEFAULT '',
  perf_tier TEXT NOT NULL DEFAULT '',
  gpu TEXT NOT NULL DEFAULT '',
  ram_gb REAL,
  duration_sec INTEGER,
  exit_code INTEGER NOT NULL DEFAULT 0,
  avg_fps REAL,
  low1_fps REAL,
  status_summary TEXT NOT NULL DEFAULT ''
);
CREATE INDEX squad_sessions_player_time ON squad_sessions(player, received_utc DESC);
CREATE INDEX squad_sessions_received ON squad_sessions(received_utc DESC);

CREATE TABLE crash_reports (
  id TEXT PRIMARY KEY,
  received_utc TEXT NOT NULL,
  player TEXT NOT NULL,
  launcher_version TEXT NOT NULL,
  match_hash TEXT NOT NULL DEFAULT '',
  mode TEXT NOT NULL DEFAULT '',
  perf_tier TEXT NOT NULL DEFAULT '',
  gpu TEXT NOT NULL DEFAULT '',
  ram_gb REAL,
  exit_code INTEGER NOT NULL DEFAULT 1,
  crash_class TEXT NOT NULL,
  summary TEXT NOT NULL,
  log_tail TEXT NOT NULL DEFAULT '',
  github_issue_url TEXT
);
CREATE INDEX crash_reports_received ON crash_reports(received_utc DESC);
CREATE INDEX crash_reports_dedup ON crash_reports(crash_class, launcher_version, received_utc DESC);
