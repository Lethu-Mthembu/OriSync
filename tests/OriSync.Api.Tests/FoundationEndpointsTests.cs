using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OriSync.Api.Tests;

public sealed class FoundationEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task HealthEndpointReportsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("healthy", payload.Status);
    }

    [Fact]
    public async Task ReadinessEndpointReportsUnhealthyWithoutDatabaseConfiguration()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("unhealthy", payload.Status);
    }

    [Fact]
    public async Task StatusEndpointIdentifiesTheService()
    {
        var response = await _client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<StatusResponse>();
        Assert.NotNull(payload);
        Assert.Equal("OriSync.Api", payload.Service);
        Assert.Equal("ready", payload.Status);
    }

    private sealed record HealthResponse(string Status);

    private sealed record StatusResponse(string Service, string Status, string Environment);
}
