CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE IF NOT EXISTS identity.tenants (
  tenant_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  name varchar(150) NOT NULL,
  slug varchar(120) NOT NULL UNIQUE,
  main_category varchar(120),
  timezone varchar(80) NOT NULL DEFAULT 'America/La_Paz',
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive')),
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS identity.roles (
  role_id smallserial PRIMARY KEY,
  code varchar(50) NOT NULL UNIQUE,
  name varchar(80) NOT NULL,
  description text
);

CREATE TABLE IF NOT EXISTS identity.users (
  user_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid REFERENCES identity.tenants(tenant_id),
  email varchar(180) NOT NULL,
  password_hash text NOT NULL,
  first_name varchar(120) NOT NULL,
  last_name varchar(120),
  phone varchar(40),
  status varchar(30) NOT NULL DEFAULT 'active'
    CHECK (status IN ('active', 'inactive', 'blocked')),
  auth_version integer NOT NULL DEFAULT 1 CHECK (auth_version > 0),
  last_login_at timestamptz,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email_normalized
  ON identity.users (lower(email));

CREATE TABLE IF NOT EXISTS identity.user_roles (
  user_id uuid NOT NULL REFERENCES identity.users(user_id),
  role_id smallint NOT NULL REFERENCES identity.roles(role_id),
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, role_id)
);

CREATE TABLE IF NOT EXISTS identity.user_branch_access (
  user_id uuid NOT NULL REFERENCES identity.users(user_id),
  tenant_id uuid NOT NULL REFERENCES identity.tenants(tenant_id),
  branch_id uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (user_id, branch_id)
);

INSERT INTO identity.roles (code, name) VALUES
  ('super_admin', 'Super administrador'),
  ('tenant_admin', 'Administrador de empresa'),
  ('branch_admin', 'Administrador de sucursal'),
  ('receptionist', 'Recepcionista'),
  ('professional', 'Profesional'),
  ('client', 'Cliente')
ON CONFLICT (code) DO NOTHING;
