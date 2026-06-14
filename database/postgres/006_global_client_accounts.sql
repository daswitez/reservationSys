UPDATE identity.users AS users
SET tenant_id = NULL,
    updated_at = now()
FROM identity.user_roles AS user_roles
JOIN identity.roles AS roles ON roles.role_id = user_roles.role_id
WHERE users.user_id = user_roles.user_id
  AND roles.code = 'client'
  AND users.tenant_id IS NOT NULL;

ALTER TABLE identity.users
  DROP CONSTRAINT IF EXISTS users_tenant_id_email_key;

DROP INDEX IF EXISTS identity.ux_users_tenant_email_normalized;

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email_normalized
  ON identity.users (lower(email));

DROP INDEX IF EXISTS booking.idx_reservations_client;

CREATE INDEX idx_reservations_client
  ON booking.reservations (client_user_id, start_at DESC);
