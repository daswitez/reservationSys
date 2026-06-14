using CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Service> Services => Set<Service>();
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
