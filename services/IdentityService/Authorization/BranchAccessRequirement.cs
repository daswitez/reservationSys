using Microsoft.AspNetCore.Authorization;

namespace IdentityService.Authorization;

public sealed class BranchAccessRequirement : IAuthorizationRequirement
{
    public static BranchAccessRequirement Instance { get; } = new();

    private BranchAccessRequirement()
    {
    }
}
