-- Uscita volontaria distinta dall'esclusione del docente. I lavori restano nello storico.
ALTER TABLE session_participant
ADD COLUMN left_at_utc_ms INTEGER NULL CHECK (left_at_utc_ms IS NULL OR left_at_utc_ms >= joined_at_utc_ms);
