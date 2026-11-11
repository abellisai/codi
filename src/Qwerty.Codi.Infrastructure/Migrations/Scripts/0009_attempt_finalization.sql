-- T5: permessi conclusivi limitati al tentativo e alla credenziale (§12.7).
CREATE TABLE attempt_finalization_grant (
    id TEXT NOT NULL PRIMARY KEY,
    attempt_id TEXT NOT NULL REFERENCES exercise_attempt(id) ON DELETE RESTRICT,
    credential_session_id TEXT NOT NULL REFERENCES credential_session(id) ON DELETE RESTRICT,
    connection_epoch INTEGER NOT NULL CHECK (connection_epoch >= 0),
    execution_generation INTEGER NOT NULL CHECK (execution_generation >= 0),
    base_row_version INTEGER NOT NULL CHECK (base_row_version >= 1),
    expires_at_utc_ms INTEGER NOT NULL,
    used_at_utc_ms INTEGER NULL,
    save_id TEXT NULL,
    row_version INTEGER NOT NULL DEFAULT 1 CHECK (row_version >= 1),
    CHECK ((used_at_utc_ms IS NULL) = (save_id IS NULL)),
    UNIQUE (attempt_id, credential_session_id, connection_epoch)
);
CREATE INDEX ix_finalization_credential ON attempt_finalization_grant(credential_session_id, expires_at_utc_ms);
