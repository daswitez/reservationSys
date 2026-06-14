ALTER TABLE identity.users
  ADD COLUMN IF NOT EXISTS auth_version integer NOT NULL DEFAULT 1;

ALTER TABLE identity.users
  DROP CONSTRAINT IF EXISTS ck_users_auth_version_positive;

ALTER TABLE identity.users
  ADD CONSTRAINT ck_users_auth_version_positive CHECK (auth_version > 0);
