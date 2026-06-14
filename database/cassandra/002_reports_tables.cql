CREATE TABLE IF NOT EXISTS report_daily_summary_by_tenant (
  tenant_id uuid,
  report_date date,
  total_created int,
  total_confirmed int,
  total_cancelled int,
  total_attended int,
  total_no_show int,
  total_blocked_minutes int,
  total_reserved_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id), report_date)
) WITH CLUSTERING ORDER BY (report_date DESC);

CREATE TABLE IF NOT EXISTS report_daily_summary_by_branch (
  tenant_id uuid,
  branch_id uuid,
  report_date date,
  branch_name text,
  total_created int,
  total_confirmed int,
  total_cancelled int,
  total_attended int,
  total_no_show int,
  total_reserved_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, branch_id), report_date)
) WITH CLUSTERING ORDER BY (report_date DESC);

CREATE TABLE IF NOT EXISTS report_service_summary_by_month (
  tenant_id uuid,
  year_month text,
  service_id uuid,
  service_name text,
  total_created int,
  total_cancelled int,
  total_attended int,
  total_no_show int,
  total_reserved_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, year_month), service_id)
);

CREATE TABLE IF NOT EXISTS report_resource_occupancy_by_day (
  tenant_id uuid,
  branch_id uuid,
  resource_id uuid,
  report_date date,
  resource_name text,
  resource_type text,
  total_reservations int,
  total_attended int,
  total_cancelled int,
  total_no_show int,
  reserved_minutes int,
  blocked_minutes int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, branch_id, report_date), resource_id)
);

CREATE TABLE IF NOT EXISTS report_peak_hours_by_branch_day (
  tenant_id uuid,
  branch_id uuid,
  report_date date,
  hour_of_day int,
  branch_name text,
  total_created int,
  total_attended int,
  total_cancelled int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, branch_id, report_date), hour_of_day)
);

CREATE TABLE IF NOT EXISTS report_reservation_status_by_month (
  tenant_id uuid,
  year_month text,
  status text,
  total int,
  updated_at timestamp,
  PRIMARY KEY ((tenant_id, year_month), status)
);

CREATE TABLE IF NOT EXISTS report_processed_events (
  event_id uuid PRIMARY KEY,
  tenant_id uuid,
  event_type text,
  processed_at timestamp
);
