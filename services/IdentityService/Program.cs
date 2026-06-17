using System.Text;
using System.Security.Claims;
using IdentityService.Authorization;
using IdentityService.Common;
using IdentityService.Data;
using IdentityService.Features.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var details = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value!.Errors
                        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "El valor proporcionado no es valido."
                            : error.ErrorMessage)
                        .ToArray());

            return new BadRequestObjectResult(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "La solicitud contiene datos invalidos.",
                details));
        };
    });
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Reservas - Identity & Tenancy API",
        Version = "v1",
        Description = "Gestion de identidad, roles y empresas del sistema de reservas."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Introduce un JWT valido. Swagger agrega automaticamente el prefijo Bearer."
    });
});
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IAuthorizationHandler, BranchAccessAuthorizationHandler>();

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
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;

                if (!Guid.TryParse(principal?.FindFirstValue("user_id"), out var userId))
                {
                    context.Fail("The token does not contain a valid user_id.");
                    return;
                }

                var dbContext = context.HttpContext.RequestServices
                    .GetRequiredService<IdentityDbContext>();
                var user = await dbContext.Users
                    .AsNoTracking()
                    .Include(candidate => candidate.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                    .SingleOrDefaultAsync(candidate => candidate.UserId == userId);

                if (user is null || user.Status != "active")
                {
                    context.Fail("The user is not active.");
                    return;
                }

                if (!int.TryParse(principal.FindFirstValue("auth_version"), out var authVersion) ||
                    authVersion != user.AuthVersion)
                {
                    context.Fail("The token authentication version is no longer valid.");
                    return;
                }

                var tenantClaim = principal.FindFirstValue("tenant_id");
                var tenantMatches = user.TenantId.HasValue
                    ? Guid.TryParse(tenantClaim, out var tenantId) && tenantId == user.TenantId.Value
                    : string.IsNullOrWhiteSpace(tenantClaim);

                if (!tenantMatches)
                {
                    context.Fail("The token tenant does not match the user.");
                    return;
                }

                var tokenRoles = principal.FindAll("roles")
                    .Select(claim => claim.Value)
                    .ToHashSet(StringComparer.Ordinal);
                var persistedRoles = user.UserRoles
                    .Select(userRole => userRole.Role.Code)
                    .ToHashSet(StringComparer.Ordinal);

                if (!tokenRoles.SetEquals(persistedRoles))
                {
                    context.Fail("The token roles are no longer valid.");
                }
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SuperAdminOnly", policy => policy.RequireRole("super_admin"))
    .AddPolicy("AuthenticatedUser", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("user_id"))
    .AddPolicy("UserAdministration", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("user_id")
        .RequireRole("super_admin", "tenant_admin"))
    .AddPolicy("AdministrativePanel", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("user_id")
        .RequireClaim("tenant_id")
        .RequireRole("tenant_admin", "branch_admin"));

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000", "null")
         .AllowAnyHeader()
         .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity & Tenancy API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Reservas - Identity API";
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "identity-service",
    status = "ok"
}));
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
