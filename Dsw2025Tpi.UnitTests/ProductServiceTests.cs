using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests;

public class ProductoServiceTests
{
    [Fact]
    public async Task AddProduct_DatosValidos_DevuelveProductoCreado()
    {
        // ==========================================
        // 1. ARRANGE (Preparación)
        // ==========================================
        var repositoryMock = CreateRepositoryMock();
        // Simulamos que no existe ningún producto con ese SKU
        repositoryMock
            .Setup(r => r.First<Product>(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync((Product?)null);
        // Simulamos el guardado exitoso
        repositoryMock
            .Setup(r => r.Add(It.IsAny<Product>()))
            .ReturnsAsync((Product product) => product);

        var service = CreateService(repositoryMock);
        // ==========================================
        // 2. ACT (Ejecución)
        // ==========================================
        var result = await service.AddProduct(new ProductModel.Request(
            "SKU-001",
            "INT-001",
            "Auriculares",
            "Auriculares inalámbricos",
            50000m,
            10));
        // ==========================================
        // 3. ASSERT (Verificación)
        // ==========================================
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("SKU-001", result.Sku);
        Assert.Equal("Auriculares", result.Name);
        Assert.True(result.IsActive);
        // Verificamos que se llamó al repositorio para guardar una sola vez
        repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Once);
    }

    [Fact]
    public async Task AddProduct_SkuDuplicado_LanzaDuplicatedEntityException()
    {

        var repositoryMock = CreateRepositoryMock();
        repositoryMock
            .Setup(r => r.First<Product>(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new Product("SKU-001", "Producto existente", 100m, 1));

        var service = CreateService(repositoryMock);

        var exception = await Assert.ThrowsAsync<DuplicatedEntityException>(() => service.AddProduct(
            new ProductModel.Request("SKU-001", "INT-002", "Auriculares", null, 50000m, 10)));

        Assert.Contains("SKU-001", exception.Message);
        repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task AddProduct_DatosInvalidos_LanzaInvalidDataException()
    {
        var repositoryMock = CreateRepositoryMock();
        var service = CreateService(repositoryMock);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.AddProduct(
            new ProductModel.Request("", "INT-003", "Auriculares", null, 50000m, 10)));

        Assert.Contains("sku", exception.Message, StringComparison.OrdinalIgnoreCase);
        repositoryMock.Verify(
            r => r.First<Product>(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>(), It.IsAny<string[]>()),
            Times.Never);
    }

    private static Mock<IRepository> CreateRepositoryMock() => new();

    private static ProductsManagementService CreateService(Mock<IRepository> repositoryMock)
    {
        var loggerMock = new Mock<ILogger<ProductsManagementService>>();
        return new ProductsManagementService(repositoryMock.Object, loggerMock.Object);
    }
}