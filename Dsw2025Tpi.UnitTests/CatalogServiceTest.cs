using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Linq.Expressions;

namespace Dsw2025Tpi.UnitTests;

public class CatalogServiceTests
{
    [Fact]
    public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
    {
        // 1. ARRANGE: Configuramos el mock con una lista mixta (activos e inactivos)
        var repositoryMock = new Mock<IRepository>();
        var bdSimulada = new List<Product>
        {
            CreateProduct("SKU-001", "Activo 1", true),
            CreateProduct("SKU-002", "Inactivo 1", false),
            CreateProduct("SKU-003", "Activo 2", true)
        };
        SetupFilteredProducts(repositoryMock, bdSimulada);
        var service = CreateService(repositoryMock);

        // 2. ACT: Solicitamos los productos pidiendo excluir los inactivos
        var result = await service.GetProducts(
            new ProductModel.FilterProduct(null, null, 1, 10),
            includeInactive: false);

        // 3. ASSERT: Verificamos que el resultado contenga únicamente los 2 activos
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.ProductItems.Count);
        Assert.All(result.ProductItems, product => Assert.True(product.IsActive));
    }

    [Fact]
    public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
    {
        var repositoryMock = new Mock<IRepository>();
        var listaDe15 = Enumerable.Range(1, 15)
                                  .Select(i => CreateProduct($"SKU-{i:000}", $"Producto {i:00}", true))
                                  .ToList();
        SetupFilteredProducts(repositoryMock, listaDe15);
        var service = CreateService(repositoryMock);

        // Solicitamos específicamente la página 2, con un tamaño de 5 elementos
        var result = await service.GetProducts(
            new ProductModel.FilterProduct(null, null, 2, 5));

        Assert.Equal(5, result.ProductItems.Count);
        Assert.Equal(15, result.Total);
        Assert.Equal("Producto 06", result.ProductItems.First().Name);
        Assert.Equal("Producto 10", result.ProductItems.Last().Name);
    }

    [Fact]
    public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode()
    {
        var repositoryMock = new Mock<IRepository>();
        var products = new List<Product>
        {
            CreateProduct("MG-01", "Mouse Gamer", true, "A1"),
            CreateProduct("TM-02", "Teclado Mecánico", true, "A2"),
            CreateProduct("MU-Mouse-03", "Monitor UltraWide", true, "A3")
        };
        SetupFilteredProducts(repositoryMock, products);
        var service = CreateService(repositoryMock);

        var result = await service.GetProducts(
            new ProductModel.FilterProduct(null, "Mouse", 1, 10));

        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.ProductItems.Count);
        Assert.Contains(result.ProductItems, product => product.Name == "Mouse Gamer");
        Assert.Contains(result.ProductItems, product => product.Sku == "MU-Mouse-03");
    }

    private static void SetupFilteredProducts(Mock<IRepository> repositoryMock, List<Product> products)
    {
        // Permite que el Mock intercepte y ejecute las expresiones LINQ (Where) en memoria
        repositoryMock
            .Setup(r => r.GetFiltered<Product>(
                It.IsAny<Expression<Func<Product, bool>>>(),
                It.IsAny<string[]>()))
            .Returns((Expression<Func<Product, bool>> predicate, string[] _) =>
                Task.FromResult<IEnumerable<Product>?>(products.Where(predicate.Compile()).ToList()));
    }

    private static Product CreateProduct(
        string sku,
        string name,
        bool isActive,
        string internalCode = "INT-001") =>
        new Product(sku, name, 100m, 10, internalCode: internalCode)
        {
            IsActive = isActive
        };

    private static ProductsManagementService CreateService(Mock<IRepository> repositoryMock)
    {
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();
        return new ProductsManagementService(repositoryMock.Object, loggerMock.Object);
    }
}
