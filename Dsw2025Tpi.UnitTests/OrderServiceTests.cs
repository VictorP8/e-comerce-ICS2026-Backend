using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
namespace Dsw2025Tpi.UnitTests;
public class OrderServiceTests
{
    [Fact]
    public async Task CreateOrder_DatosValidos_CalculaTotalYActualizaStock()
    {
        var repositoryMock = new Mock<IRepository>();
        var customerId = Guid.NewGuid();
        var product = new Product("SKU-001", "Auriculares", 100m, 10, internalCode: "INT-001");
        repositoryMock.Setup(r => r.GetById<Customer>(customerId))
        .ReturnsAsync(new Customer { Id = customerId, Name = "Juan" });
        repositoryMock.Setup(r => r.GetById<Product>(product.Id))
        .ReturnsAsync(product);
        repositoryMock.Setup(r => r.Update(It.IsAny<Product>()))
        .ReturnsAsync((Product updatedProduct) => updatedProduct);
        repositoryMock.Setup(r => r.Add(It.IsAny<Order>()))
        .ReturnsAsync((Order order) => order);
        var service = CreateService(repositoryMock);
        var result = await service.CreateOrder(new OrderRequest(
        customerId,
        "Calle 123",
        "Calle 123",
        "Entregar por la tarde",
        new List<OrderItemRequest> { new(product.Id, 3) }));
        Assert.Equal(300m, result.TotalAmount);
        Assert.Equal(7, product.StockQuantity);
        Assert.Single(result.Items);
        Assert.Equal(300m, result.Items[0].Subtotal);
        Assert.Equal("Juan", result.ClientName);
        repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Once);
        repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Once);
    }
    [Fact]
    public async Task CreateOrder_StockInsuficiente_LanzaInsufficientStockException()
    {
        var repositoryMock = new Mock<IRepository>();
        var customerId = Guid.NewGuid();
        var product = new Product("SKU-001", "Auriculares", 100m, 2, internalCode: "INT-001");
        repositoryMock.Setup(r => r.GetById<Customer>(customerId))
        .ReturnsAsync(new Customer { Id = customerId, Name = "Juan" });
        repositoryMock.Setup(r => r.GetById<Product>(product.Id))
        .ReturnsAsync(product);
        var service = CreateService(repositoryMock);
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
        service.CreateOrder(CreateOrderRequest(customerId, product.Id, 5)));
        repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
    }
    [Fact]
    public async Task CreateOrder_ClienteInexistente_LanzaCustomerNotFoundException()
    {
        var repositoryMock = new Mock<IRepository>();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        repositoryMock.Setup(r => r.GetById<Customer>(customerId))
        .ReturnsAsync((Customer?)null);
        var service = CreateService(repositoryMock);
        await Assert.ThrowsAsync<CustomerNotFoundException>(() =>
        service.CreateOrder(CreateOrderRequest(customerId, productId, 1)));
        repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
        repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
    }
    private static OrderRequest CreateOrderRequest(Guid customerId, Guid productId, int quantity) =>
    new(customerId, "Calle 123", "Calle 123", null, new List<OrderItemRequest>
    {
new ( productId ,  quantity )
    });
    private static OrdersManagementService CreateService(Mock<IRepository> repositoryMock)
    {
        var loggerMock = new Mock<ILogger<OrdersManagementService>>();
        return new OrdersManagementService(repositoryMock.Object, loggerMock.Object);
    }
}