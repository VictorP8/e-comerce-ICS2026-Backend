using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class ProductsControllerIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProductsControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // =========================================================================
    // ESCENARIO 1: Sin token (401 Unauthorized) y con rol USER (403 Forbidden)
    // =========================================================================
    [Fact]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden()
    {
        var nuevoProducto = new
        {
            sku = "SKU-UNAUTH-01",
            internalCode = "INT-UNAUTH-01",
            name = "Teclado Mecánico",
            description = "Teclado RGB",
            currentUnitPrice = 45000m,
            stockQuantity = 10
        };

        // 1. Petición anónima, devolver 401 Unauthorized
        var unauthResponse = await _client.PostAsJsonAsync("/api/products", nuevoProducto);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        // 2. Petición con token de cliente común ("USER"),devolver 403 Forbidden
        var userToken = await ObtenerTokenClienteAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        var forbiddenResponse = await _client.PostAsJsonAsync("/api/products", nuevoProducto);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    // =========================================================================
    // ESCENARIO 2: Alta exitosa como Admin y persistencia real en SQLite
    // =========================================================================
    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        // 1. ARRANGE
        var token = await ObtenerTokenAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var skuUnico = $"SKU-INT-{Guid.NewGuid():N}".Substring(0, 15);
        var nuevoProducto = new
        {
            sku = skuUnico,
            internalCode = "INT-100",
            name = "Mouse Inalámbrico",
            description = "Mouse óptico 2.4GHz",
            currentUnitPrice = 25000m,
            stockQuantity = 15
        };

        // 2. ACT
        var response = await _client.PostAsJsonAsync("/api/products", nuevoProducto);

        // 3. ASSERT HTTP
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // 4. ASSERT SQLITE: Verificamos la fila persistida en la base de datos en memoria
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var productoGuardado = await db.Set<Product>().FirstOrDefaultAsync(p => p.Sku == skuUnico);

        Assert.NotNull(productoGuardado);
        Assert.Equal("Mouse Inalámbrico", productoGuardado.Name);
        Assert.Equal(25000m, productoGuardado.CurrentUnitPrice);
        Assert.Equal(15, productoGuardado.StockQuantity);
    }

    // =========================================================================
    // ESCENARIO 3: SKU duplicado en base de datos (409 Conflict / 400)
    // =========================================================================
    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
    {
        // 1. ARRANGE: Pre-insertamos el producto directamente en SQLite
        var skuDuplicado = $"SKU-DUP-{Guid.NewGuid():N}".Substring(0, 15);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var existente = new Product(skuDuplicado, "Monitor Original", 150000m, 5, internalCode: "INT-DUP");
            db.Set<Product>().Add(existente);
            await db.SaveChangesAsync();
        }

        var token = await ObtenerTokenAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var payloadDuplicado = new
        {
            sku = skuDuplicado,
            internalCode = "INT-DUP-2",
            name = "Monitor Duplicado",
            description = "Intento con mismo SKU",
            currentUnitPrice = 160000m,
            stockQuantity = 3
        };

        // 2. ACT
        var response = await _client.PostAsJsonAsync("/api/products", payloadDuplicado);

        // 3. ASSERT
        Assert.True(
            response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest,
            $"Se esperaba 409 Conflict o 400 BadRequest, pero devolvió {(int)response.StatusCode} ({response.StatusCode}).");
    }

    // =========================================================================
    // HELPERS DE AUTENTICACIÓN (Adaptados a AuthenticateController)
    // =========================================================================
    private async Task<string> ObtenerTokenAdminAsync()
    {
        const string adminUser = "admin_integration";
        const string adminPass = "Admin123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // Traemos los nombres de roles a memoria con ToListAsync() antes de comparar
            var roles = await roleManager.Roles.Select(r => r.Name).ToListAsync();
            var existingRole = roles.FirstOrDefault(n => n != null && n.Equals("ADMIN", StringComparison.OrdinalIgnoreCase));

            var adminRoleName = existingRole ?? "ADMIN";
            if (!await roleManager.RoleExistsAsync(adminRoleName))
            {
                await roleManager.CreateAsync(new IdentityRole(adminRoleName));
            }

            var user = await userManager.FindByNameAsync(adminUser);
            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = adminUser,
                    Email = "admin_integration@test.com",
                    EmailConfirmed = true
                };
                await userManager.CreateAsync(user, adminPass);
                await userManager.AddToRoleAsync(user, adminRoleName);
            }
        }

        _client.DefaultRequestHeaders.Authorization = null;
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = adminUser,
            Password = adminPass
        });
        loginResponse.EnsureSuccessStatusCode();

        var body = await loginResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        return body!.Token;
    }

    private async Task<string> ObtenerTokenClienteAsync()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        _client.DefaultRequestHeaders.Authorization = null;

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = $"user_{suffix}",
            Email = $"user_{suffix}@test.com",
            Password = "User123!"
        });
        registerResponse.EnsureSuccessStatusCode();

        var body = await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        return body!.Token;
    }

    private sealed class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;
    }
}
