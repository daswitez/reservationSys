using System.Security.Claims;
using BookingService.Domain;
using BookingService.Security;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Tests;

public sealed class BookingAuthorizationServiceTests
{
    private readonly BookingAuthorizationService _authorization = new();

    [Fact]
    public void CanAccessReservation_ClientOwner_ReturnsTrue()
    {
        var clientId = Guid.NewGuid();
        var reservation = Reservation(clientUserId: clientId);
        var user = User("client", clientId);

        var allowed = _authorization.CanAccessReservation(
            user,
            reservation,
            allowClient: true,
            out var failure);

        Assert.True(allowed);
        Assert.Null(failure);
    }

    [Fact]
    public void CanAccessReservation_OtherClient_ReturnsForbidden()
    {
        var reservation = Reservation(clientUserId: Guid.NewGuid());
        var user = User("client", Guid.NewGuid());

        var allowed = _authorization.CanAccessReservation(
            user,
            reservation,
            allowClient: true,
            out var failure);

        Assert.False(allowed);
        Assert.IsType<ForbidResult>(failure);
    }

    [Fact]
    public void CanAccessReservation_TenantAdminSameTenant_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var reservation = Reservation(tenantId: tenantId);
        var user = User("tenant_admin", Guid.NewGuid(), tenantId);

        var allowed = _authorization.CanAccessReservation(
            user,
            reservation,
            allowClient: false,
            out var failure);

        Assert.True(allowed);
        Assert.Null(failure);
    }

    [Fact]
    public void CanAccessReservation_TenantAdminOtherTenant_ReturnsForbidden()
    {
        var reservation = Reservation(tenantId: Guid.NewGuid());
        var user = User("tenant_admin", Guid.NewGuid(), Guid.NewGuid());

        var allowed = _authorization.CanAccessReservation(
            user,
            reservation,
            allowClient: false,
            out var failure);

        Assert.False(allowed);
        Assert.IsType<ForbidResult>(failure);
    }

    [Fact]
    public void CanAccessReservation_BranchAdminSameBranch_ReturnsTrue()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var reservation = Reservation(tenantId: tenantId, branchId: branchId);
        var user = User("branch_admin", Guid.NewGuid(), tenantId, branchId);

        var allowed = _authorization.CanAccessReservation(
            user,
            reservation,
            allowClient: false,
            out var failure);

        Assert.True(allowed);
        Assert.Null(failure);
    }

    [Fact]
    public void CanAccessReservation_BranchAdminOtherBranch_ReturnsForbidden()
    {
        var tenantId = Guid.NewGuid();
        var reservation = Reservation(tenantId: tenantId, branchId: Guid.NewGuid());
        var user = User("branch_admin", Guid.NewGuid(), tenantId, Guid.NewGuid());

        var allowed = _authorization.CanAccessReservation(
            user,
            reservation,
            allowClient: false,
            out var failure);

        Assert.False(allowed);
        Assert.IsType<ForbidResult>(failure);
    }

    [Fact]
    public void CanAccessBlock_Client_ReturnsForbidden()
    {
        var block = Block();
        var user = User("client", Guid.NewGuid());

        var allowed = _authorization.CanAccessBlock(user, block, out var failure);

        Assert.False(allowed);
        Assert.IsType<ForbidResult>(failure);
    }

    [Fact]
    public void CanAccessResource_BranchAdminMissingBranchClaim_ReturnsUnauthorized()
    {
        var tenantId = Guid.NewGuid();
        var resource = Resource(tenantId: tenantId, branchId: Guid.NewGuid());
        var user = User("branch_admin", Guid.NewGuid(), tenantId);

        var allowed = _authorization.CanAccessResource(user, resource, out var failure);

        Assert.False(allowed);
        Assert.IsType<UnauthorizedObjectResult>(failure);
    }

    [Fact]
    public void CanAccessBranch_SuperAdmin_ReturnsTrue()
    {
        var user = User("super_admin", Guid.NewGuid());

        var allowed = _authorization.CanAccessBranch(
            user,
            Guid.NewGuid(),
            Guid.NewGuid(),
            out var failure);

        Assert.True(allowed);
        Assert.Null(failure);
    }

    [Fact]
    public void EnsureInternalUser_Client_ReturnsForbidden()
    {
        var user = User("client", Guid.NewGuid());

        var allowed = _authorization.EnsureInternalUser(user, out var failure);

        Assert.False(allowed);
        Assert.IsType<ForbidResult>(failure);
    }

    [Fact]
    public void EnsureInternalUser_TenantAdmin_ReturnsTrue()
    {
        var user = User("tenant_admin", Guid.NewGuid(), Guid.NewGuid());

        var allowed = _authorization.EnsureInternalUser(user, out var failure);

        Assert.True(allowed);
        Assert.Null(failure);
    }

    private static ClaimsPrincipal User(
        string role,
        Guid userId,
        Guid? tenantId = null,
        Guid? branchId = null)
    {
        var claims = new List<Claim>
        {
            new("user_id", userId.ToString()),
            new(ClaimTypes.Role, role)
        };

        if (tenantId.HasValue)
            claims.Add(new Claim("tenant_id", tenantId.Value.ToString()));
        if (branchId.HasValue)
            claims.Add(new Claim("branch_id", branchId.Value.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static Reservation Reservation(
        Guid? tenantId = null,
        Guid? branchId = null,
        Guid? clientUserId = null) =>
        new()
        {
            ReservationId = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            BranchId = branchId ?? Guid.NewGuid(),
            ClientUserId = clientUserId ?? Guid.NewGuid()
        };

    private static ResourceBlock Block(
        Guid? tenantId = null,
        Guid? branchId = null) =>
        new()
        {
            BlockId = Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            BranchId = branchId ?? Guid.NewGuid(),
            ResourceId = Guid.NewGuid()
        };

    private static Resource Resource(Guid tenantId, Guid branchId) =>
        new()
        {
            ResourceId = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId
        };
}
