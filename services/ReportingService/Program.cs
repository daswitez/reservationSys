using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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
