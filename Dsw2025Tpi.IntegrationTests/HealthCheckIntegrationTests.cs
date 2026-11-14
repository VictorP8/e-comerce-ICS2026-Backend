using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class HealthCheckIntegrationTests
{
    private readonly HttpClient _client;

    public HealthCheckIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_DevuelveCodigoExitoso()
    {
        var response = await _client.GetAsync("/healthcheck");
        response.EnsureSuccessStatusCode();
    }
}
