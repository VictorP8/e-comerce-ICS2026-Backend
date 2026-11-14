using System.Data.Common;
using Dsw2025Tpi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly DbConnection _businessConnection;
    private readonly DbConnection _authConnection;

    public CustomWebApplicationFactory()
    {
        // Conexiones SQLite en memoria compartida separadas para cada DbContext
        _businessConnection = new SqliteConnection("DataSource=Dsw2025TpiTestDb;Mode=Memory;Cache=Shared");
        _businessConnection.Open();

        _authConnection = new SqliteConnection("DataSource=AuthTestDb;Mode=Memory;Cache=Shared");
        _authConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // 1. Limpiamos la configuración de SQL Server de ambos contextos (necesario en EF Core 8)
            RemoveDbContext<Dsw2025TpiContext>(services);
            RemoveDbContext<AuthenticateContext>(services);

            // 2. Registramos ambos contextos apuntando a SQLite en memoria
            services.AddDbContext<Dsw2025TpiContext>(options =>
                options.UseSqlite(_businessConnection));

            services.AddDbContext<AuthenticateContext>(options =>
                options.UseSqlite(_authConnection));

            // 3. Creamos las tablas de negocio e Identity en SQLite
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var businessDb = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            businessDb.Database.EnsureCreated();

            var authDb = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
            authDb.Database.EnsureCreated();
        });
    }

    private static void RemoveDbContext<TContext>(IServiceCollection services) where TContext : DbContext
    {
        services.RemoveAll(typeof(DbContextOptions<TContext>));
        services.RemoveAll(typeof(IDbContextOptionsConfiguration<TContext>));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _businessConnection.Close();
            _businessConnection.Dispose();
            _authConnection.Close();
            _authConnection.Dispose();
        }
    }
}
