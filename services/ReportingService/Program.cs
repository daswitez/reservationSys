using System.Text;
using Cassandra;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Cassandra — config is read lazily inside the lambda so test overrides (UseSetting) apply
builder.Services.AddSingleton<ICluster>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var contactPoints = cfg.GetSection("Cassandra:ContactPoints").Get<string[]>() ?? ["localhost"];
    var port = cfg.GetValue<int>("Cassandra:Port", 9042);
    var localDc = cfg["Cassandra:LocalDatacenter"] ?? "datacenter1";
    return Cluster.Builder()
        .AddContactPoints(contactPoints)
        .WithPort(port)
        .WithLoadBalancingPolicy(new DCAwareRoundRobinPolicy(localDc))
        .Build();
});

builder.Services.AddSingleton<Cassandra.ISession>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var keyspace = cfg["Cassandra:Keyspace"] ?? "reservas_reports";
    return sp.GetRequiredService<ICluster>().Connect(keyspace);
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient("IdentityValidation", client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:IdentityBaseUrl"]
        ?? throw new InvalidOperationException("Services:IdentityBaseUrl is required.")));

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is required.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType = "roles",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context => ValidateWithIdentityAsync(context)
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AuthenticatedUser", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("user_id"))
    .AddPolicy("TenantUser", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("user_id")
        .RequireClaim("tenant_id"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "reporting-service",
    status = "ok"
}));
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

static async Task ValidateWithIdentityAsync(TokenValidatedContext context)
{
    var authorization = context.HttpContext.Request.Headers.Authorization.ToString();

    if (string.IsNullOrWhiteSpace(authorization))
    {
        context.Fail("The authorization header is missing.");
        return;
    }

    var client = context.HttpContext.RequestServices
        .GetRequiredService<IHttpClientFactory>()
        .CreateClient("IdentityValidation");
    using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
    request.Headers.TryAddWithoutValidation("Authorization", authorization);
    try
    {
        using var response = await client.SendAsync(request, context.HttpContext.RequestAborted);

        if (!response.IsSuccessStatusCode)
        {
            context.Fail("Identity rejected the token.");
        }
    }
    catch (HttpRequestException)
    {
        context.Fail("Identity is unavailable for token validation.");
    }
}

public partial class Program;
