using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<BranchService> BranchServices => Set<BranchService>();
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ServiceResource> ServiceResources => Set<ServiceResource>();
    public DbSet<ResourceSchedule> ResourceSchedules => Set<ResourceSchedule>();
    public DbSet<CatalogTenant> Tenants => Set<CatalogTenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        branch.HasIndex(entity => new { entity.TenantId, entity.Status })
            .HasDatabaseName("idx_branches_tenant_status");

        var service = modelBuilder.Entity<Service>();
        service.ToTable("services", "catalog");
        service.HasKey(entity => entity.ServiceId);
        service.Property(entity => entity.ServiceId).HasColumnName("service_id");
        service.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        service.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(150);
        service.Property(entity => entity.Description).HasColumnName("description");
        service.Property(entity => entity.DurationMinutes).HasColumnName("duration_minutes");
        service.Property(entity => entity.ReferencePrice)
            .HasColumnName("reference_price")
            .HasPrecision(10, 2);
        service.Property(entity => entity.Modality).HasColumnName("modality").HasMaxLength(30);
        service.Property(entity => entity.RequiresConfirmation).HasColumnName("requires_confirmation");
        service.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        service.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        service.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        service.HasIndex(entity => new { entity.TenantId, entity.Status })
            .HasDatabaseName("idx_services_tenant_status");

        var branchService = modelBuilder.Entity<BranchService>();
        branchService.ToTable("branch_services", "catalog");
        branchService.HasKey(entity => new { entity.BranchId, entity.ServiceId });
        branchService.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        branchService.Property(entity => entity.BranchId).HasColumnName("branch_id");
        branchService.Property(entity => entity.ServiceId).HasColumnName("service_id");
        branchService.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        branchService.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        branchService.HasIndex(entity => new { entity.TenantId, entity.ServiceId })
            .HasDatabaseName("idx_branch_services_tenant");

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
        resource.HasIndex(entity => new { entity.TenantId, entity.BranchId, entity.Status })
            .HasDatabaseName("idx_resources_branch_status");

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
        serviceResource.HasIndex(entity => new { entity.TenantId, entity.ResourceId })
            .HasDatabaseName("idx_service_resources_resource");

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
        resourceSchedule.HasIndex(entity => new
            {
                entity.TenantId,
                entity.BranchId,
                entity.ResourceId,
                entity.DayOfWeek,
                entity.Status
            })
            .HasDatabaseName("idx_resource_schedules_lookup");

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
    }
}
