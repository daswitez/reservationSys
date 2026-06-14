# Diagrama entidad-relación Mermaid

Este diagrama representa el modelo conceptual/lógico del MVP actualizado.

Importante:

- La operación principal vive en PostgreSQL.
- Reporting vive en Cassandra.
- Cassandra no tiene relaciones reales ni foreign keys; se muestra aparte como modelo de lectura para reportes.

```mermaid
erDiagram

    TENANT {
        uuid tenant_id PK
        string name
        string slug UK
        string main_category
        string timezone
        string status
        timestamptz created_at
    }

    USER {
        uuid user_id PK
        uuid tenant_id FK
        string email
        string password_hash
        string first_name
        string last_name
        string phone
        string status
        timestamptz created_at
    }

    ROLE {
        int role_id PK
        string code UK
        string name
    }

    USER_ROLE {
        uuid user_id PK, FK
        int role_id PK, FK
    }

    USER_BRANCH_ACCESS {
        uuid user_id PK, FK
        uuid tenant_id FK
        uuid branch_id PK, FK
    }

    BRANCH {
        uuid branch_id PK
        uuid tenant_id FK
        string name
        string address
        string phone
        string email_contact
        string timezone
        string status
    }

    SERVICE {
        uuid service_id PK
        uuid tenant_id FK
        string name
        text description
        int duration_minutes
        decimal reference_price
        string modality
        boolean requires_confirmation
        string status
    }

    BRANCH_SERVICE {
        uuid branch_id PK, FK
        uuid service_id PK, FK
        uuid tenant_id FK
        string status
    }

    RESOURCE {
        uuid resource_id PK
        uuid tenant_id FK
        uuid branch_id FK
        string name
        string resource_type
        text description
        int capacity
        string status
    }

    SERVICE_RESOURCE {
        uuid service_id PK, FK
        uuid resource_id PK, FK
        uuid tenant_id FK
        boolean required
        int priority
        string status
    }

    RESOURCE_SCHEDULE {
        uuid schedule_id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid resource_id FK
        int day_of_week
        time start_time
        time end_time
        date valid_from
        date valid_to
        string status
    }

    RESERVATION {
        uuid reservation_id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid client_user_id FK
        uuid service_id FK
        uuid resource_id FK
        uuid created_by_user_id FK
        timestamptz start_at
        timestamptz end_at
        string status
        string channel_origin
        text notes
        timestamptz created_at
    }

    RESOURCE_BLOCK {
        uuid block_id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid resource_id FK
        timestamptz start_at
        timestamptz end_at
        text reason
        string block_type
        string status
        uuid created_by_user_id FK
    }

    RESERVATION_HISTORY {
        uuid history_id PK
        uuid tenant_id FK
        uuid reservation_id FK
        uuid user_id FK
        string previous_status
        string new_status
        string action
        text reason
        timestamptz created_at
    }

    RESERVATION_EVENT_OUTBOX {
        uuid event_id PK
        uuid tenant_id FK
        string event_type
        uuid aggregate_id
        jsonb payload
        string status
        int attempts
        timestamptz created_at
        timestamptz processed_at
    }

    REPORT_DAILY_SUMMARY_BY_TENANT {
        uuid tenant_id PK
        date report_date PK
        int total_created
        int total_confirmed
        int total_cancelled
        int total_attended
        int total_no_show
        int total_reserved_minutes
    }

    REPORT_RESOURCE_OCCUPANCY_BY_DAY {
        uuid tenant_id PK
        uuid branch_id PK
        date report_date PK
        uuid resource_id PK
        string resource_name
        int total_reservations
        int reserved_minutes
        int blocked_minutes
    }

    TENANT ||--o{ USER : has
    TENANT ||--o{ BRANCH : owns
    TENANT ||--o{ SERVICE : offers
    TENANT ||--o{ RESOURCE : manages
    TENANT ||--o{ RESERVATION : contains

    USER ||--o{ USER_ROLE : has
    ROLE ||--o{ USER_ROLE : assigned
    USER ||--o{ USER_BRANCH_ACCESS : can_access
    BRANCH ||--o{ USER_BRANCH_ACCESS : assigned_to

    BRANCH ||--o{ RESOURCE : contains
    BRANCH ||--o{ BRANCH_SERVICE : enables
    SERVICE ||--o{ BRANCH_SERVICE : available_in

    SERVICE ||--o{ SERVICE_RESOURCE : requires
    RESOURCE ||--o{ SERVICE_RESOURCE : supports

    RESOURCE ||--o{ RESOURCE_SCHEDULE : has
    RESOURCE ||--o{ RESOURCE_BLOCK : blocked_by

    USER ||--o{ RESERVATION : creates_or_owns
    BRANCH ||--o{ RESERVATION : receives
    SERVICE ||--o{ RESERVATION : booked_as
    RESOURCE ||--o{ RESERVATION : occupied_by

    RESERVATION ||--o{ RESERVATION_HISTORY : changes
    USER ||--o{ RESERVATION_HISTORY : registers

    RESERVATION ||--o{ RESERVATION_EVENT_OUTBOX : emits

    TENANT ||--o{ REPORT_DAILY_SUMMARY_BY_TENANT : reports
    RESOURCE ||--o{ REPORT_RESOURCE_OCCUPANCY_BY_DAY : summarized_in
```
