namespace BookingService.Domain;

public sealed class Branch
{
    public Guid BranchId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? EmailContact { get; set; }
    public string Timezone { get; set; } = "America/La_Paz";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
