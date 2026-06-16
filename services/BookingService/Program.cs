using System.Text;
using BookingService.Common;
using BookingService.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        Title = "Reservas - Booking API",
        Version = "v1",
        Description = "Consulta de disponibilidad, reservas, bloqueos e historial del sistema de reservas."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Introduce un JWT valido. Swagger agrega automaticamente el prefijo Bearer."
    });
});
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
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
        .RequireClaim("tenant_id"))
    .AddPolicy("ClientOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("user_id")
        .RequireRole("client"));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Booking API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Reservas - Booking API";
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "booking-service",
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
