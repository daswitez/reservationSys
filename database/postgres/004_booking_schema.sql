CREATE SCHEMA IF NOT EXISTS booking;

CREATE TABLE IF NOT EXISTS booking.reservations (
  reservation_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  client_user_id uuid NOT NULL,
  service_id uuid NOT NULL,
  resource_id uuid NOT NULL,
  created_by_user_id uuid,
  start_at timestamptz NOT NULL,
  end_at timestamptz NOT NULL,
  status varchar(30) NOT NULL DEFAULT 'CONFIRMED'
    CHECK (status IN ('CONFIRMED', 'CANCELLED', 'ATTENDED', 'NO_SHOW')),
  channel_origin varchar(40) NOT NULL DEFAULT 'web',
  notes text,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK (end_at > start_at)
);

CREATE INDEX IF NOT EXISTS idx_reservations_agenda
  ON booking.reservations (tenant_id, branch_id, start_at, status);
CREATE INDEX IF NOT EXISTS idx_reservations_client
  ON booking.reservations (client_user_id, start_at DESC);
CREATE INDEX IF NOT EXISTS idx_reservations_resource_day
  ON booking.reservations (tenant_id, resource_id, start_at);

ALTER TABLE booking.reservations
  ADD CONSTRAINT no_overlapping_confirmed_reservations
  EXCLUDE USING gist (
    tenant_id WITH =,
    resource_id WITH =,
    tstzrange(start_at, end_at, '[)') WITH &&
  ) WHERE (status = 'CONFIRMED');

CREATE TABLE IF NOT EXISTS booking.resource_blocks (
  block_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  resource_id uuid NOT NULL,
  start_at timestamptz NOT NULL,
  end_at timestamptz NOT NULL,
  reason text,
  block_type varchar(50) NOT NULL DEFAULT 'manual',
  status varchar(30) NOT NULL DEFAULT 'ACTIVE'
    CHECK (status IN ('ACTIVE', 'CANCELLED')),
  created_by_user_id uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  CHECK (end_at > start_at)
);

CREATE INDEX IF NOT EXISTS idx_blocks_resource_day
  ON booking.resource_blocks (tenant_id, resource_id, start_at, status);

ALTER TABLE booking.resource_blocks
  ADD CONSTRAINT no_overlapping_active_blocks
  EXCLUDE USING gist (
    tenant_id WITH =,
    resource_id WITH =,
    tstzrange(start_at, end_at, '[)') WITH &&
  ) WHERE (status = 'ACTIVE');

CREATE TABLE IF NOT EXISTS booking.reservation_history (
  history_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  reservation_id uuid NOT NULL REFERENCES booking.reservations(reservation_id),
  user_id uuid,
  previous_status varchar(30),
  new_status varchar(30) NOT NULL,
  action varchar(80) NOT NULL,
  reason text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_history_reservation
  ON booking.reservation_history (tenant_id, reservation_id, created_at);

CREATE TABLE IF NOT EXISTS booking.reservation_event_outbox (
  event_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  event_type varchar(80) NOT NULL,
  aggregate_id uuid NOT NULL,
  payload jsonb NOT NULL,
  status varchar(30) NOT NULL DEFAULT 'PENDING'
    CHECK (status IN ('PENDING', 'PROCESSING', 'PROCESSED', 'FAILED')),
  attempts int NOT NULL DEFAULT 0 CHECK (attempts >= 0),
  last_error text,
  created_at timestamptz NOT NULL DEFAULT now(),
  processed_at timestamptz
);

CREATE INDEX IF NOT EXISTS idx_outbox_pending
  ON booking.reservation_event_outbox (status, created_at);
