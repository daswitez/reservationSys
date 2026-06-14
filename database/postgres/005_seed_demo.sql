INSERT INTO identity.tenants (
  tenant_id, name, slug, main_category, timezone, status
) VALUES (
  '11111111-1111-1111-1111-111111111111',
  'Peluqueria Demo',
  'peluqueria-demo',
  'Belleza',
  'America/La_Paz',
  'active'
) ON CONFLICT (tenant_id) DO NOTHING;

INSERT INTO identity.users (
  user_id, tenant_id, email, password_hash, first_name, last_name, phone, status
) VALUES
  (
    '00000000-0000-0000-0000-000000000001',
    NULL,
    'superadmin@demo.local',
    crypt('Password123', gen_salt('bf')),
    'Super',
    'Admin',
    '+59170000000',
    'active'
  ),
  (
    '22222222-2222-2222-2222-222222222222',
    '11111111-1111-1111-1111-111111111111',
    'admin@demo.local',
    crypt('Password123', gen_salt('bf')),
    'Admin',
    'Demo',
    '+59170000001',
    'active'
  ),
  (
    '33333333-3333-3333-3333-333333333333',
    NULL,
    'cliente@demo.local',
    crypt('Password123', gen_salt('bf')),
    'Cliente',
    'Demo',
    '+59170000002',
    'active'
  )
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO identity.user_roles (user_id, role_id)
SELECT '00000000-0000-0000-0000-000000000001', role_id
FROM identity.roles WHERE code = 'super_admin'
ON CONFLICT DO NOTHING;

INSERT INTO identity.user_roles (user_id, role_id)
SELECT '22222222-2222-2222-2222-222222222222', role_id
FROM identity.roles WHERE code = 'tenant_admin'
ON CONFLICT DO NOTHING;

INSERT INTO identity.user_roles (user_id, role_id)
SELECT '33333333-3333-3333-3333-333333333333', role_id
FROM identity.roles WHERE code = 'client'
ON CONFLICT DO NOTHING;

INSERT INTO catalog.branches (
  branch_id, tenant_id, name, address, phone, email_contact, timezone, status
) VALUES (
  '44444444-4444-4444-4444-444444444444',
  '11111111-1111-1111-1111-111111111111',
  'Sucursal Centro',
  'Av. Principal #123',
  '+59170000000',
  'centro@demo.local',
  'America/La_Paz',
  'active'
) ON CONFLICT (branch_id) DO NOTHING;

INSERT INTO catalog.services (
  service_id, tenant_id, name, description, duration_minutes,
  reference_price, modality, status
) VALUES (
  '55555555-5555-5555-5555-555555555555',
  '11111111-1111-1111-1111-111111111111',
  'Corte de cabello',
  'Corte clasico o moderno',
  30,
  50.00,
  'presencial',
  'active'
) ON CONFLICT (service_id) DO NOTHING;

INSERT INTO catalog.resources (
  resource_id, tenant_id, branch_id, name, resource_type, description, capacity, status
) VALUES (
  '66666666-6666-6666-6666-666666666666',
  '11111111-1111-1111-1111-111111111111',
  '44444444-4444-4444-4444-444444444444',
  'Silla 1',
  'chair',
  'Silla principal',
  1,
  'active'
) ON CONFLICT (resource_id) DO NOTHING;

INSERT INTO catalog.branch_services (tenant_id, branch_id, service_id)
VALUES (
  '11111111-1111-1111-1111-111111111111',
  '44444444-4444-4444-4444-444444444444',
  '55555555-5555-5555-5555-555555555555'
) ON CONFLICT DO NOTHING;

INSERT INTO catalog.service_resources (tenant_id, service_id, resource_id)
VALUES (
  '11111111-1111-1111-1111-111111111111',
  '55555555-5555-5555-5555-555555555555',
  '66666666-6666-6666-6666-666666666666'
) ON CONFLICT DO NOTHING;

INSERT INTO catalog.resource_schedules (
  tenant_id, branch_id, resource_id, day_of_week, start_time, end_time, valid_from
)
SELECT
  '11111111-1111-1111-1111-111111111111',
  '44444444-4444-4444-4444-444444444444',
  '66666666-6666-6666-6666-666666666666',
  day_of_week,
  '09:00'::time,
  '18:00'::time,
  DATE '2026-01-01'
FROM generate_series(1, 5) AS day_of_week;
