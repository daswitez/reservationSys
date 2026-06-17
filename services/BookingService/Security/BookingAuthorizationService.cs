using System.Security.Claims;
using BookingService.Common;
using BookingService.Domain;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Security;

public sealed class BookingAuthorizationService
{
    public bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
    {
        var value = user.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    public bool EnsureInternalUser(ClaimsPrincipal user, out IActionResult? failure)
    {
        failure = null;

        if (user.IsInRole("super_admin") ||
            user.IsInRole("tenant_admin") ||
            user.IsInRole("branch_admin"))
        {
            return true;
        }

        failure = new ForbidResult();
        return false;
    }

    public bool CanAccessReservation(
        ClaimsPrincipal user,
        Reservation reservation,
        bool allowClient,
        out IActionResult? failure)
    {
        failure = null;

        if (user.IsInRole("super_admin"))
        {
            return true;
        }

        if (user.IsInRole("client"))
        {
            if (!allowClient)
            {
                failure = new ForbidResult();
                return false;
            }

            if (!TryGetUserId(user, out var userId))
            {
                failure = Unauthorized("El JWT no contiene user_id valido.");
                return false;
            }

            if (reservation.ClientUserId == userId)
            {
                return true;
            }

            failure = new ForbidResult();
            return false;
        }

        return CanAccessBranch(user, reservation.TenantId, reservation.BranchId, out failure);
    }

    public bool CanAccessResource(
        ClaimsPrincipal user,
        Resource resource,
        out IActionResult? failure) =>
        CanAccessBranch(user, resource.TenantId, resource.BranchId, out failure);

    public bool CanAccessBlock(
        ClaimsPrincipal user,
        ResourceBlock block,
        out IActionResult? failure) =>
        CanAccessBranch(user, block.TenantId, block.BranchId, out failure);

    public bool CanAccessBranch(
        ClaimsPrincipal user,
        Guid tenantId,
        Guid branchId,
        out IActionResult? failure)
    {
        failure = null;

        if (user.IsInRole("super_admin"))
        {
            return true;
        }

        if (user.IsInRole("client"))
        {
            failure = new ForbidResult();
            return false;
        }

        if (!TryGetTenantId(user, out var claimTenantId, out failure))
        {
            return false;
        }

        if (tenantId != claimTenantId)
        {
            failure = new ForbidResult();
            return false;
        }

        if (user.IsInRole("branch_admin"))
        {
            if (!TryGetBranchId(user, out var claimBranchId, out failure))
            {
                return false;
            }

            if (branchId != claimBranchId)
            {
                failure = new ForbidResult();
                return false;
            }
        }

        if (user.IsInRole("tenant_admin") || user.IsInRole("branch_admin"))
        {
            return true;
        }

        failure = new ForbidResult();
        return false;
    }

    public bool CanAccessTenant(
        ClaimsPrincipal user,
        Guid tenantId,
        out IActionResult? failure)
    {
        failure = null;

        if (user.IsInRole("super_admin"))
        {
            return true;
        }

        if (user.IsInRole("client"))
        {
            failure = new ForbidResult();
            return false;
        }

        if (!TryGetTenantId(user, out var claimTenantId, out failure))
        {
            return false;
        }

        if (tenantId == claimTenantId && (user.IsInRole("tenant_admin") || user.IsInRole("branch_admin")))
        {
            return true;
        }

        failure = new ForbidResult();
        return false;
    }

    public bool TryGetTenantId(
        ClaimsPrincipal user,
        out Guid tenantId,
        out IActionResult? failure)
    {
        failure = null;
        var tenantIdClaim = user.FindFirstValue("tenant_id");
        if (Guid.TryParse(tenantIdClaim, out tenantId))
        {
            return true;
        }

        failure = Unauthorized("El JWT no contiene tenant_id valido.");
        return false;
    }

    public bool TryGetBranchId(
        ClaimsPrincipal user,
        out Guid branchId,
        out IActionResult? failure)
    {
        failure = null;
        var branchIdClaim = user.FindFirstValue("branch_id");
        if (Guid.TryParse(branchIdClaim, out branchId))
        {
            return true;
        }

        failure = Unauthorized("El JWT no contiene branch_id valido.");
        return false;
    }

    private static UnauthorizedObjectResult Unauthorized(string message) =>
        new(ApiResponse<object>.Failure("UNAUTHORIZED", message));
}
