using BookingService.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Data;

public sealed class BookingDbContext(DbContextOptions<BookingDbContext> options)
    : DbContext(options)
{
    public DbSet<CatalogTenant> Tenants => Set<CatalogTenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<BranchService> BranchServices => Set<BranchService>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ServiceResource> ServiceResources => Set<ServiceResource>();
    public DbSet<ResourceSchedule> ResourceSchedules => Set<ResourceSchedule>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ResourceBlock> ResourceBlocks => Set<ResourceBlock>();
    public DbSet<ReservationHistory> ReservationHistory => Set<ReservationHistory>();
    public DbSet<ReservationEventOutbox> ReservationEventOutbox => Set<ReservationEventOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<CatalogTenant>();
        tenant.ToTable("tenants", "identity");
        tenant.HasKey(entity => entity.TenantId);
        tenant.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        tenant.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(150);
        tenant.Property(entity => entity.Slug).HasColumnName("slug").HasMaxLength(120);
        tenant.Property(entity => entity.MainCategory).HasColumnName("main_category").HasMaxLength(120);
        tenant.Property(entity => entity.Timezone).HasColumnName("timezone").HasMaxLength(80);
        tenant.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        tenant.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        tenant.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

        var branch = modelBuilder.Entity<Branch>();
        branch.ToTable("branches", "catalog");
        branch.HasKey(entity => entity.BranchId);
        branch.Property(entity => entity.BranchId).HasColumnName("branch_id");
        branch.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        branch.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(150);
        branch.Property(entity => entity.Address).HasColumnName("address");
        branch.Property(entity => entity.Phone).HasColumnName("phone").HasMaxLength(40);
        branch.Property(entity => entity.EmailContact).HasColumnName("email_contact").HasMaxLength(180);
        branch.Property(entity => entity.Timezone).HasColumnName("timezone").HasMaxLength(80);
        branch.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        branch.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        branch.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

        var service = modelBuilder.Entity<Service>();
        service.ToTable("services", "catalog");
        service.HasKey(entity => entity.ServiceId);
        service.Property(entity => entity.ServiceId).HasColumnName("service_id");
        service.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        service.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(150);
        service.Property(entity => entity.Description).HasColumnName("description");
        service.Property(entity => entity.DurationMinutes).HasColumnName("duration_minutes");
        service.Property(entity => entity.ReferencePrice).HasColumnName("reference_price").HasPrecision(10, 2);
        service.Property(entity => entity.Modality).HasColumnName("modality").HasMaxLength(30);
        service.Property(entity => entity.RequiresConfirmation).HasColumnName("requires_confirmation");
        service.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        service.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        service.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

        var branchService = modelBuilder.Entity<BranchService>();
        branchService.ToTable("branch_services", "catalog");
        branchService.HasKey(entity => new { entity.BranchId, entity.ServiceId });
        branchService.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        branchService.Property(entity => entity.BranchId).HasColumnName("branch_id");
        branchService.Property(entity => entity.ServiceId).HasColumnName("service_id");
        branchService.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        branchService.Property(entity => entity.CreatedAt).HasColumnName("created_at");

        var resource = modelBuilder.Entity<Resource>();
        resource.ToTable("resources", "catalog");
        resource.HasKey(entity => entity.ResourceId);
        resource.Property(entity => entity.ResourceId).HasColumnName("resource_id");
        resource.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        resource.Property(entity => entity.BranchId).HasColumnName("branch_id");
        resource.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(150);
        resource.Property(entity => entity.ResourceType).HasColumnName("resource_type").HasMaxLength(80);
        resource.Property(entity => entity.Description).HasColumnName("description");
        resource.Property(entity => entity.Capacity).HasColumnName("capacity");
        resource.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        resource.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        resource.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

        var serviceResource = modelBuilder.Entity<ServiceResource>();
        serviceResource.ToTable("service_resources", "catalog");
        serviceResource.HasKey(entity => new { entity.ServiceId, entity.ResourceId });
        serviceResource.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        serviceResource.Property(entity => entity.ServiceId).HasColumnName("service_id");
        serviceResource.Property(entity => entity.ResourceId).HasColumnName("resource_id");
        serviceResource.Property(entity => entity.Required).HasColumnName("required");
        serviceResource.Property(entity => entity.Priority).HasColumnName("priority");
        serviceResource.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        serviceResource.Property(entity => entity.CreatedAt).HasColumnName("created_at");

        var resourceSchedule = modelBuilder.Entity<ResourceSchedule>();
        resourceSchedule.ToTable("resource_schedules", "catalog");
        resourceSchedule.HasKey(entity => entity.ScheduleId);
        resourceSchedule.Property(entity => entity.ScheduleId).HasColumnName("schedule_id");
        resourceSchedule.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        resourceSchedule.Property(entity => entity.BranchId).HasColumnName("branch_id");
        resourceSchedule.Property(entity => entity.ResourceId).HasColumnName("resource_id");
        resourceSchedule.Property(entity => entity.DayOfWeek).HasColumnName("day_of_week");
        resourceSchedule.Property(entity => entity.StartTime).HasColumnName("start_time");
        resourceSchedule.Property(entity => entity.EndTime).HasColumnName("end_time");
        resourceSchedule.Property(entity => entity.ValidFrom).HasColumnName("valid_from");
        resourceSchedule.Property(entity => entity.ValidTo).HasColumnName("valid_to");
        resourceSchedule.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        resourceSchedule.Property(entity => entity.CreatedAt).HasColumnName("created_at");

        var reservation = modelBuilder.Entity<Reservation>();
        reservation.ToTable("reservations", "booking");
        reservation.HasKey(entity => entity.ReservationId);
        reservation.Property(entity => entity.ReservationId).HasColumnName("reservation_id");
        reservation.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        reservation.Property(entity => entity.BranchId).HasColumnName("branch_id");
        reservation.Property(entity => entity.ClientUserId).HasColumnName("client_user_id");
        reservation.Property(entity => entity.ServiceId).HasColumnName("service_id");
        reservation.Property(entity => entity.ResourceId).HasColumnName("resource_id");
        reservation.Property(entity => entity.CreatedByUserId).HasColumnName("created_by_user_id");
        reservation.Property(entity => entity.StartAt).HasColumnName("start_at");
        reservation.Property(entity => entity.EndAt).HasColumnName("end_at");
        reservation.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        reservation.Property(entity => entity.ChannelOrigin).HasColumnName("channel_origin").HasMaxLength(40);
        reservation.Property(entity => entity.Notes).HasColumnName("notes");
        reservation.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        reservation.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

        var resourceBlock = modelBuilder.Entity<ResourceBlock>();
        resourceBlock.ToTable("resource_blocks", "booking");
        resourceBlock.HasKey(entity => entity.BlockId);
        resourceBlock.Property(entity => entity.BlockId).HasColumnName("block_id");
        resourceBlock.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        resourceBlock.Property(entity => entity.BranchId).HasColumnName("branch_id");
        resourceBlock.Property(entity => entity.ResourceId).HasColumnName("resource_id");
        resourceBlock.Property(entity => entity.StartAt).HasColumnName("start_at");
        resourceBlock.Property(entity => entity.EndAt).HasColumnName("end_at");
        resourceBlock.Property(entity => entity.Reason).HasColumnName("reason");
        resourceBlock.Property(entity => entity.BlockType).HasColumnName("block_type").HasMaxLength(50);
        resourceBlock.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        resourceBlock.Property(entity => entity.CreatedByUserId).HasColumnName("created_by_user_id");
        resourceBlock.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        resourceBlock.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");

        var reservationHistory = modelBuilder.Entity<ReservationHistory>();
        reservationHistory.ToTable("reservation_history", "booking");
        reservationHistory.HasKey(entity => entity.HistoryId);
        reservationHistory.Property(entity => entity.HistoryId).HasColumnName("history_id");
        reservationHistory.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        reservationHistory.Property(entity => entity.ReservationId).HasColumnName("reservation_id");
        reservationHistory.Property(entity => entity.UserId).HasColumnName("user_id");
        reservationHistory.Property(entity => entity.PreviousStatus).HasColumnName("previous_status").HasMaxLength(30);
        reservationHistory.Property(entity => entity.NewStatus).HasColumnName("new_status").HasMaxLength(30);
        reservationHistory.Property(entity => entity.Action).HasColumnName("action").HasMaxLength(80);
        reservationHistory.Property(entity => entity.Reason).HasColumnName("reason");
        reservationHistory.Property(entity => entity.CreatedAt).HasColumnName("created_at");

        var eventOutbox = modelBuilder.Entity<ReservationEventOutbox>();
        eventOutbox.ToTable("reservation_event_outbox", "booking");
        eventOutbox.HasKey(entity => entity.EventId);
        eventOutbox.Property(entity => entity.EventId).HasColumnName("event_id");
        eventOutbox.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        eventOutbox.Property(entity => entity.EventType).HasColumnName("event_type").HasMaxLength(80);
        eventOutbox.Property(entity => entity.AggregateId).HasColumnName("aggregate_id");
        eventOutbox.Property(entity => entity.Payload).HasColumnName("payload").HasColumnType("jsonb");
        eventOutbox.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        eventOutbox.Property(entity => entity.Attempts).HasColumnName("attempts");
        eventOutbox.Property(entity => entity.LastError).HasColumnName("last_error");
        eventOutbox.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        eventOutbox.Property(entity => entity.ProcessedAt).HasColumnName("processed_at");
    }
}
