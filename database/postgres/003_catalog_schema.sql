CREATE SCHEMA IF NOT EXISTS catalog;

CREATE TABLE IF NOT EXISTS catalog.branches (
  branch_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  name varchar(150) NOT NULL,
  address text,
  phone varchar(40),
  email_contact varchar(180),
  timezone varchar(80) NOT NULL DEFAULT 'America/La_Paz',
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_branches_tenant_status
  ON catalog.branches (tenant_id, status);

CREATE TABLE IF NOT EXISTS catalog.services (
  service_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  name varchar(150) NOT NULL,
  description text,
  duration_minutes int NOT NULL CHECK (duration_minutes > 0),
  reference_price numeric(10,2) CHECK (reference_price IS NULL OR reference_price >= 0),
  modality varchar(30) NOT NULL DEFAULT 'presencial',
  requires_confirmation boolean NOT NULL DEFAULT false,
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_services_tenant_status
  ON catalog.services (tenant_id, status);

CREATE TABLE IF NOT EXISTS catalog.branch_services (
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES catalog.branches(branch_id),
  service_id uuid NOT NULL REFERENCES catalog.services(service_id),
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (branch_id, service_id)
);

CREATE INDEX IF NOT EXISTS idx_branch_services_tenant
  ON catalog.branch_services (tenant_id, service_id);

CREATE TABLE IF NOT EXISTS catalog.resources (
  resource_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES catalog.branches(branch_id),
  name varchar(150) NOT NULL,
  resource_type varchar(80) NOT NULL,
  description text,
  capacity int NOT NULL DEFAULT 1 CHECK (capacity > 0),
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'blocked', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_resources_branch_status
  ON catalog.resources (tenant_id, branch_id, status);

CREATE TABLE IF NOT EXISTS catalog.service_resources (
  tenant_id uuid NOT NULL,
  service_id uuid NOT NULL REFERENCES catalog.services(service_id),
  resource_id uuid NOT NULL REFERENCES catalog.resources(resource_id),
  required boolean NOT NULL DEFAULT true,
  priority int NOT NULL DEFAULT 1 CHECK (priority > 0),
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (service_id, resource_id)
);

CREATE INDEX IF NOT EXISTS idx_service_resources_resource
  ON catalog.service_resources (tenant_id, resource_id);

CREATE TABLE IF NOT EXISTS catalog.resource_schedules (
  schedule_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES catalog.branches(branch_id),
  resource_id uuid NOT NULL REFERENCES catalog.resources(resource_id),
  day_of_week smallint NOT NULL CHECK (day_of_week BETWEEN 1 AND 7),
  start_time time NOT NULL,
  end_time time NOT NULL,
  valid_from date,
  valid_to date,
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  CHECK (end_time > start_time),
  CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)
);

CREATE INDEX IF NOT EXISTS idx_resource_schedules_lookup
  ON catalog.resource_schedules (tenant_id, branch_id, resource_id, day_of_week, status);
