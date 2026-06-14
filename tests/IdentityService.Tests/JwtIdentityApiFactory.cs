using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IdentityService.Tests;

public sealed class JwtIdentityApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            "ConnectionStrings:Postgres",
            "Host=localhost;Port=55432;Database=reservas_mvp;Username=reservas;Password=reservas_dev;Search Path=identity");
    }
}
