using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ReportingService.Tests;

public sealed class ReportingApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Keep Cassandra settings pointing to local dev instance
        builder.UseSetting("Cassandra:ContactPoints:0", "localhost");
        builder.UseSetting("Cassandra:Port", "9142");
        builder.UseSetting("Cassandra:Keyspace", "reservas_reports");
        builder.UseSetting("Cassandra:LocalDatacenter", "datacenter1");

        // Jwt:Secret is required by Program.cs at build time
        builder.UseSetting("Jwt:Secret", "test_secret_at_least_32_chars_long!!");
        builder.UseSetting("Jwt:Issuer", "reservas-mvp");
        builder.UseSetting("Jwt:Audience", "reservas-mvp-web");
        builder.UseSetting("Services:IdentityBaseUrl", "http://localhost:5201");

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }
}
