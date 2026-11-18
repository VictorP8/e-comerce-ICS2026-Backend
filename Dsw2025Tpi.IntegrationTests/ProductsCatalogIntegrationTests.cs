using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class ProductsCatalogIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProductsCatalogIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // =========================================================================
    // ESCENARIO 1: Endpoint público GET /api/products devuelve 200 OK y JSON paginado
    // =========================================================================
    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        // 1. ARRANGE: Sembramos al menos 1 producto activo para que no devuelva 204 NoContent
        var sku = $"SKU-PUB-{Guid.NewGuid():N}".Substring(0, 15);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var producto = new Product(sku, "Auriculares Bluetooth", 35000m, 12, internalCode: "INT-PUB-01");
            db.Set<Product>().Add(producto);
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Authorization = null;

        // 2. ACT
        var response = await _client.GetAsync("/api/products");

        // 3. ASSERT: 200 OK y estructura JSON paginada (productItems + total)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("productItems", out var itemsElement),
            "La respuesta JSON debe contener la propiedad 'productItems'.");
        Assert.Equal(JsonValueKind.Array, itemsElement.ValueKind);
        Assert.True(itemsElement.GetArrayLength() >= 1);

        Assert.True(root.TryGetProperty("total", out var totalElement),
            "La respuesta JSON debe contener la propiedad 'total'.");
        Assert.True(totalElement.GetInt32() >= 1);
    }

    // =========================================================================
    // ESCENARIO 2: Filtros por Query String devuelven resultados filtrados desde SQLite
    // =========================================================================
    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        // 1. ARRANGE: Sembramos 3 productos con un prefijo único en minúsculas para SQLite
        var tagUnico = Guid.NewGuid().ToString("N").Substring(0, 8);
        var skuMatch1 = $"SKU-F1-{tagUnico}";
        var skuMatch2 = $"SKU-F2-{tagUnico}";
        var skuOtro = $"SKU-OT-{tagUnico}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            db.Set<Product>().AddRange(
                new Product(skuMatch1, $"monitor {tagUnico} curvo ", 210000m, 5, internalCode: "INT-F1"),
                new Product(skuMatch2, $"monitor {tagUnico} plano ", 180000m, 8, internalCode: "INT-F2"),
                new Product(skuOtro, $"teclado {tagUnico} slim ", 40000m, 10, internalCode: "INT-F3")
            );
            await db.SaveChangesAsync();
        }

        _client.DefaultRequestHeaders.Authorization = null;
        var terminoBusqueda = $"monitor {tagUnico}";
        var url = $"/api/products?Search={Uri.EscapeDataString(terminoBusqueda)}&PageNumber=1&PageSize=10";

        // 2. ACT
        var response = await _client.GetAsync(url);

        // 3. ASSERT: Solo deben volver los 2 monitores que coinciden con el filtro
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("productItems");

        Assert.Equal(2, items.GetArrayLength());

        var skusDevueltos = items.EnumerateArray()
            .Select(item => item.GetProperty("sku").GetString())
            .ToList();

        Assert.Contains(skuMatch1, skusDevueltos);
        Assert.Contains(skuMatch2, skusDevueltos);
        Assert.DoesNotContain(skuOtro, skusDevueltos);
    }

    // =========================================================================
    // ESCENARIO 3: Endpoint Admin GET /api/products/admin incluye productos inactivos
    // =========================================================================
    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        // 1. ARRANGE: Sembramos un producto activo y uno inactivo (IsActive = false) en SQLite
        var tag = Guid.NewGuid().ToString("N").Substring(0, 8);
        var skuActivo = $"SKU-ACT-{tag}";
        var skuInactivo = $"SKU-INA-{tag}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

            var prodActivo = new Product(skuActivo, $"Producto Activo {tag}", 10000m, 5, internalCode: "INT-ACT");
            var prodInactivo = new Product(skuInactivo, $"Producto Inactivo {tag}", 12000m, 0, internalCode: "INT-INA");

            db.Set<Product>().AddRange(prodActivo, prodInactivo);
            await db.SaveChangesAsync();

            db.Entry(prodInactivo).Property("IsActive").CurrentValue = false;
            await db.SaveChangesAsync();
        }

        var tokenAdmin = await ObtenerTokenAdminAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);

        // 2. ACT
        var response = await _client.GetAsync("/api/products/admin?PageNumber=1&PageSize=100");

        // 3. ASSERT: 200 OK y el listado incluye tanto el activo como el inactivo
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("productItems");

        var skusDevueltos = items.EnumerateArray()
            .Select(item => item.GetProperty("sku").GetString())
            .ToList();

        Assert.Contains(skuActivo, skusDevueltos);
        Assert.Contains(skuInactivo, skusDevueltos);
    }

    // =========================================================================
    // HELPER DE AUTENTICACIÓN ADMIN
    // =========================================================================
    private async Task<string> ObtenerTokenAdminAsync()
    {
        const string adminUser = "admin_catalog_test";
        const string adminPass = "Admin123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

            var roles = await roleManager.Roles.Select(r => r.Name).ToListAsync();
            var existingRole = roles.FirstOrDefault(n => n != null && n.Equals("Admin", StringComparison.OrdinalIgnoreCase));

            var adminRoleName = existingRole ?? "Admin";
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
                    Email = "admin_catalog@test.com",
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

    private sealed class AuthResponseDto
    {
        public string Token { get; set; } = string.Empty;
    }
}

