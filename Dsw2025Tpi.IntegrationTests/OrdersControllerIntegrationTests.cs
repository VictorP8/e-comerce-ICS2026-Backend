using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class OrdersControllerIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrdersControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // =========================================================================
    // ESCENARIO 1: Seguridad sin token (401 Unauthorized)
    // =========================================================================
    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // 1. ARRANGE: Sin header Authorization
        _client.DefaultRequestHeaders.Authorization = null;

        var items = new[]
        {
            new { productId = Guid.NewGuid(), quantity = 1 }
        };

        var nuevaOrden = new
        {
            customerId = Guid.NewGuid(),
            shippingAddress = "Calle Falsa 123",
            billingAddress = "Calle Falsa 123",
            notes = "Entrega por la tarde",
            orderItems = items,
            items = items
        };

        // 2. ACT
        var response = await _client.PostAsJsonAsync("/api/orders", nuevaOrden);

        // 3. ASSERT
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // ESCENARIO 2: Creación exitosa, persistencia de Orden y descuento de Stock
    // =========================================================================
    [Fact]
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        // 1. ARRANGE: Registramos cliente (crea Customer y devuelve Token + CustomerId)
        var auth = await RegistrarClienteAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        // Sembramos un producto con stock inicial = 10 en SQLite
        var sku = $"SKU-ORD-{Guid.NewGuid():N}".Substring(0, 15);
        Guid productId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var producto = new Product(sku, "Notebook Gamer", 500000m, 10, internalCode: "INT-ORD-01");
            db.Set<Product>().Add(producto);
            await db.SaveChangesAsync();
            productId = producto.Id;
        }

        var direccionUnica = $"Av. Independencia {Guid.NewGuid():N}".Substring(0, 35);
        var items = new[]
        {
            new { productId = productId, quantity = 3 }
        };

        var nuevaOrden = new
        {
            customerId = auth.CustomerId,
            shippingAddress = direccionUnica,
            billingAddress = direccionUnica,
            notes = "Prueba de integración",
            orderItems = items,
            items = items
        };

        // 2. ACT
        var response = await _client.PostAsJsonAsync("/api/orders", nuevaOrden);

        // 3. ASSERT HTTP: 201 Created y header Location
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        // 4. ASSERT SQLITE: Verificamos la orden creada y que el stock bajó de 10 a 7
        using (var assertScope = _factory.Services.CreateScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

            var ordenGuardada = await db.Set<Order>()
                .FirstOrDefaultAsync(o => o.ShippingAddress == direccionUnica);
            Assert.NotNull(ordenGuardada);

            var productoActualizado = await db.Set<Product>()
                .FirstOrDefaultAsync(p => p.Id == productId);
            Assert.NotNull(productoActualizado);
            Assert.Equal(7, productoActualizado.StockQuantity);
        }
    }

    // =========================================================================
    // ESCENARIO 3: Stock insuficiente devuelve 400 BadRequest con mensaje
    // =========================================================================
    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        // 1. ARRANGE: Registramos cliente y sembramos producto con solo 2 unidades
        var auth = await RegistrarClienteAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var sku = $"SKU-LOW-{Guid.NewGuid():N}".Substring(0, 15);
        Guid productId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var producto = new Product(sku, "Placa de Video", 300000m, 2, internalCode: "INT-LOW-01");
            db.Set<Product>().Add(producto);
            await db.SaveChangesAsync();
            productId = producto.Id;
        }

        // Pedimos 5 unidades cuando el stock disponible es 2
        var items = new[]
        {
            new { productId = productId, quantity = 5 }
        };

        var ordenSinStock = new
        {
            customerId = auth.CustomerId,
            shippingAddress = "San Martín 450",
            billingAddress = "San Martín 450",
            notes = "Intento con stock insuficiente",
            orderItems = items,
            items = items
        };

        // 2. ACT
        var response = await _client.PostAsJsonAsync("/api/orders", ordenSinStock);

        // 3. ASSERT
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorContent = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(errorContent));
    }

    // =========================================================================
    // HELPER DE REGISTRO / AUTENTICACIÓN DE CLIENTE
    // =========================================================================
    private async Task<RegisterResponseDto> RegistrarClienteAsync()
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        _client.DefaultRequestHeaders.Authorization = null;

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = $"client_{suffix}",
            Email = $"client_{suffix}@test.com",
            Password = "User123!"
        });
        registerResponse.EnsureSuccessStatusCode();

        var body = await registerResponse.Content.ReadFromJsonAsync<RegisterResponseDto>();
        return body!;
    }

    private sealed class RegisterResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public Guid? CustomerId { get; set; }
    }
}