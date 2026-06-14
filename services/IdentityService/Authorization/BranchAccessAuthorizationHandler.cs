using System.Security.Claims;
using IdentityService.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Authorization;

public sealed class BranchAccessAuthorizationHandler(IdentityDbContext dbContext)
    : AuthorizationHandler<BranchAccessRequirement, Guid>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchAccessRequirement requirement,
        Guid branchId)
    {
        if (!TryGetGuidClaim(context.User, "tenant_id", out var tenantId) ||
            !TryGetGuidClaim(context.User, "user_id", out var userId))
        {
            return;
        }

        var user = await dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate =>
                candidate.UserId == userId &&
                candidate.TenantId == tenantId &&
                candidate.Status == "active");

        if (user is null)
        {
            return;
        }

        var persistedRoles = user.UserRoles
            .Select(userRole => userRole.Role.Code)
            .ToHashSet(StringComparer.Ordinal);

        var branchBelongsToTenant = await dbContext.CatalogBranches.AnyAsync(
            branch => branch.BranchId == branchId && branch.TenantId == tenantId);

        if (!branchBelongsToTenant)
        {
            return;
        }

        if (context.User.IsInRole("tenant_admin") && persistedRoles.Contains("tenant_admin"))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole("branch_admin") || !persistedRoles.Contains("branch_admin"))
        {
            return;
        }

        var hasBranchAccess = await dbContext.UserBranchAccess.AnyAsync(
            access => access.UserId == userId &&
                      access.TenantId == tenantId &&
                      access.BranchId == branchId);

        if (hasBranchAccess)
        {
            context.Succeed(requirement);
        }
    }

    private static bool TryGetGuidClaim(
        ClaimsPrincipal principal,
        string claimType,
        out Guid value) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out value);
}
