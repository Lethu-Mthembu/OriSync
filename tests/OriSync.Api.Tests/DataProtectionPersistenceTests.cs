using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OriSync.Api.Authentication;
using OriSync.Api.Data;
using Testcontainers.PostgreSql;

namespace OriSync.Api.Tests;

public sealed class DataProtectionPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("orisync_data_protection_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task AntiforgeryTokenRemainsValidAcrossApplicationInstances()
    {
        await using var firstFactory = CreateFactory();
        await MigrateAsync(firstFactory);
        using var firstClient = CreateClient(firstFactory);

        var csrfResponse = await firstClient.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>();
        var antiforgeryCookie = csrfResponse.Headers.GetValues("Set-Cookie")
            .Single()
            .Split(';', 2)[0];

        await using var secondFactory = CreateFactory();
        using var secondClient = CreateClient(secondFactory);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(
                "missing@orisync.test",
                "not-a-real-password"))
        };
        loginRequest.Headers.Add("Cookie", antiforgeryCookie);
        loginRequest.Headers.Add(AuthenticationConstants.AntiforgeryHeaderName, csrf!.RequestToken);

        var response = await secondClient.SendAsync(loginRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:OriSync", _postgres.GetConnectionString()));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OriSyncDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
