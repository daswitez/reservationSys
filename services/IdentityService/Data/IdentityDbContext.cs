using IdentityService.Domain;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserBranchAccess> UserBranchAccess => Set<UserBranchAccess>();
    public DbSet<CatalogBranch> CatalogBranches => Set<CatalogBranch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<Tenant>();

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
        tenant.HasIndex(entity => entity.Slug).IsUnique();

        var user = modelBuilder.Entity<User>();

        user.ToTable("users", "identity");
        user.HasKey(entity => entity.UserId);
        user.Property(entity => entity.UserId).HasColumnName("user_id");
        user.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        user.Property(entity => entity.Email).HasColumnName("email").HasMaxLength(180);
        user.Property(entity => entity.PasswordHash).HasColumnName("password_hash");
        user.Property(entity => entity.FirstName).HasColumnName("first_name").HasMaxLength(120);
        user.Property(entity => entity.LastName).HasColumnName("last_name").HasMaxLength(120);
        user.Property(entity => entity.Phone).HasColumnName("phone").HasMaxLength(40);
        user.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        user.Property(entity => entity.AuthVersion).HasColumnName("auth_version");
        user.Property(entity => entity.LastLoginAt).HasColumnName("last_login_at");
        user.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        user.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        user.HasIndex(entity => entity.Email).IsUnique();
        user.HasOne(entity => entity.Tenant)
            .WithMany()
            .HasForeignKey(entity => entity.TenantId);

        var role = modelBuilder.Entity<Role>();

        role.ToTable("roles", "identity");
        role.HasKey(entity => entity.RoleId);
        role.Property(entity => entity.RoleId).HasColumnName("role_id");
        role.Property(entity => entity.Code).HasColumnName("code").HasMaxLength(50);
        role.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(80);
        role.Property(entity => entity.Description).HasColumnName("description");
        role.HasIndex(entity => entity.Code).IsUnique();

        var userRole = modelBuilder.Entity<UserRole>();

        userRole.ToTable("user_roles", "identity");
        userRole.HasKey(entity => new { entity.UserId, entity.RoleId });
        userRole.Property(entity => entity.UserId).HasColumnName("user_id");
        userRole.Property(entity => entity.RoleId).HasColumnName("role_id");
        userRole.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        userRole.HasOne(entity => entity.User)
            .WithMany(entity => entity.UserRoles)
            .HasForeignKey(entity => entity.UserId);
        userRole.HasOne(entity => entity.Role)
            .WithMany(entity => entity.UserRoles)
            .HasForeignKey(entity => entity.RoleId);

        var userBranchAccess = modelBuilder.Entity<UserBranchAccess>();

        userBranchAccess.ToTable("user_branch_access", "identity");
        userBranchAccess.HasKey(entity => new { entity.UserId, entity.BranchId });
        userBranchAccess.Property(entity => entity.UserId).HasColumnName("user_id");
        userBranchAccess.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        userBranchAccess.Property(entity => entity.BranchId).HasColumnName("branch_id");
        userBranchAccess.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        userBranchAccess.HasOne(entity => entity.User)
            .WithMany(entity => entity.BranchAccess)
            .HasForeignKey(entity => entity.UserId);

        var catalogBranch = modelBuilder.Entity<CatalogBranch>();

        catalogBranch.ToTable("branches", "catalog");
        catalogBranch.HasKey(entity => entity.BranchId);
        catalogBranch.Property(entity => entity.BranchId).HasColumnName("branch_id");
        catalogBranch.Property(entity => entity.TenantId).HasColumnName("tenant_id");
        catalogBranch.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(150);
        catalogBranch.Property(entity => entity.Address).HasColumnName("address");
        catalogBranch.Property(entity => entity.Phone).HasColumnName("phone").HasMaxLength(40);
        catalogBranch.Property(entity => entity.EmailContact).HasColumnName("email_contact").HasMaxLength(180);
        catalogBranch.Property(entity => entity.Timezone).HasColumnName("timezone").HasMaxLength(80);
        catalogBranch.Property(entity => entity.Status).HasColumnName("status").HasMaxLength(30);
        catalogBranch.Property(entity => entity.CreatedAt).HasColumnName("created_at");
        catalogBranch.Property(entity => entity.UpdatedAt).HasColumnName("updated_at");
        catalogBranch.HasIndex(entity => new { entity.TenantId, entity.Status });
    }
}
