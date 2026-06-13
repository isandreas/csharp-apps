using System.Net;
using System.Net.Http.Json;
using Api.Data;
using Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Api.Tests;

/// <summary>
/// WebApplicationFactory that swaps PostgreSQL for an in-memory EF provider
/// and injects a test API key.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key-12345";

    // Use a static, stable database name so all tests share the same in-memory store
    private static readonly string DbName = $"ApiTests_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Remove ALL descriptors that reference AppDbContext (Npgsql options, context factory, etc.)
            var toRemove = services
                .Where(d =>
                    d.ServiceType == typeof(AppDbContext) ||
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GenericTypeArguments.Any(t => t == typeof(AppDbContext))))
                .ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            // Register a fresh in-memory DbContext
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(DbName));
        });

        builder.UseSetting("API_KEY", TestApiKey);
    }
}

public sealed class ItemsEndpointsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private readonly HttpClient _authenticatedClient;
    private readonly HttpClient _unauthenticatedClient;

    public ItemsEndpointsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _authenticatedClient = factory.CreateClient();
        _authenticatedClient.DefaultRequestHeaders.Add("X-Api-Key", ApiWebApplicationFactory.TestApiKey);

        _unauthenticatedClient = factory.CreateClient();
    }

    // ── GET /api/v1/items ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetItems_WithValidApiKey_Returns200()
    {
        var response = await _authenticatedClient.GetAsync("/api/v1/items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetItems_WithoutApiKey_Returns401()
    {
        var response = await _unauthenticatedClient.GetAsync("/api/v1/items");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetItems_WithWrongApiKey_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        var response = await client.GetAsync("/api/v1/items");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetItems_ReturnsPaginatedResult()
    {
        var response = await _authenticatedClient.GetAsync("/api/v1/items?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<Item>>();
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
    }

    // ── POST /api/v1/items ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateItem_WithValidBody_Returns201()
    {
        var request = new CreateItemRequest("Test Item", "A description");
        var response = await _authenticatedClient.PostAsJsonAsync("/api/v1/items", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<Item>();
        Assert.NotNull(created);
        Assert.Equal("Test Item", created.Name);
        Assert.Equal("A description", created.Description);
        Assert.NotEqual(Guid.Empty, created.Id);
    }

    [Fact]
    public async Task CreateItem_WithEmptyName_Returns400()
    {
        var request = new CreateItemRequest("", null);
        var response = await _authenticatedClient.PostAsJsonAsync("/api/v1/items", request);

        // Validation problem returns 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateItem_WithWhitespaceName_Returns400()
    {
        var request = new CreateItemRequest("   ", null);
        var response = await _authenticatedClient.PostAsJsonAsync("/api/v1/items", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateItem_WithoutApiKey_Returns401()
    {
        var request = new CreateItemRequest("Test", null);
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/v1/items", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── GET /api/v1/items/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task GetItem_ExistingId_Returns200()
    {
        // Create item first
        var createRequest = new CreateItemRequest("Get Test Item", null);
        var createResponse = await _authenticatedClient.PostAsJsonAsync("/api/v1/items", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Item>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        // Then retrieve it
        var response = await _authenticatedClient.GetAsync($"/api/v1/items/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var item = await response.Content.ReadFromJsonAsync<Item>();
        Assert.NotNull(item);
        Assert.Equal(created.Id, item.Id);
    }

    [Fact]
    public async Task GetItem_NonExistentId_Returns404()
    {
        var response = await _authenticatedClient.GetAsync($"/api/v1/items/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── PUT /api/v1/items/{id} ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateItem_ExistingId_Returns200()
    {
        var createRequest = new CreateItemRequest("Original Name", null);
        var createResponse = await _authenticatedClient.PostAsJsonAsync("/api/v1/items", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Item>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        var updateRequest = new UpdateItemRequest("Updated Name", "New description");
        var response = await _authenticatedClient.PutAsJsonAsync($"/api/v1/items/{created.Id}", updateRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<Item>();
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal("New description", updated.Description);
    }

    [Fact]
    public async Task UpdateItem_NonExistentId_Returns404()
    {
        var updateRequest = new UpdateItemRequest("Name", null);
        var response = await _authenticatedClient.PutAsJsonAsync($"/api/v1/items/{Guid.NewGuid()}", updateRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /api/v1/items/{id} ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteItem_ExistingId_Returns204()
    {
        var createRequest = new CreateItemRequest("Delete Me", null);
        var createResponse = await _authenticatedClient.PostAsJsonAsync("/api/v1/items", createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Item>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);

        var response = await _authenticatedClient.DeleteAsync($"/api/v1/items/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_NonExistentId_Returns404()
    {
        var response = await _authenticatedClient.DeleteAsync($"/api/v1/items/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /healthz ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthCheck_ReturnsOk_WithoutApiKey()
    {
        var response = await _unauthenticatedClient.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk_WithApiKey()
    {
        var response = await _authenticatedClient.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
