CREATE KEYSPACE IF NOT EXISTS reservas_reports
WITH replication = {
  'class': 'SimpleStrategy',
  'replication_factor': 1
};
