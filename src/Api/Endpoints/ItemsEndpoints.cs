using Api.Data;
using Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

/// <summary>
/// Maps all /api/v1/items endpoints onto the provided route builder.
/// </summary>
public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items")
                       .WithTags("Items")
                       .WithOpenApi();

        group.MapGet("/", GetItems)
             .WithName("GetItems")
             .WithSummary("List items with pagination");

        group.MapGet("/{id:guid}", GetItem)
             .WithName("GetItem")
             .WithSummary("Get a single item by ID");

        group.MapPost("/", CreateItem)
             .WithName("CreateItem")
             .WithSummary("Create a new item");

        group.MapPut("/{id:guid}", UpdateItem)
             .WithName("UpdateItem")
             .WithSummary("Update an existing item");

        group.MapDelete("/{id:guid}", DeleteItem)
             .WithName("DeleteItem")
             .WithSummary("Delete an item");

        return app;
    }

    // GET /api/v1/items?page=1&pageSize=20
    private static async Task<Ok<PagedResult<Item>>> GetItems(
        AppDbContext db,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var totalCount = await db.Items.CountAsync(ct);
        var items = await db.Items
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return TypedResults.Ok(new PagedResult<Item>(items, page, pageSize, totalCount));
    }

    // GET /api/v1/items/{id}
    private static async Task<Results<Ok<Item>, NotFound>> GetItem(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return item is not null ? TypedResults.Ok(item) : TypedResults.NotFound();
    }

    // POST /api/v1/items
    private static async Task<Results<Created<Item>, ValidationProblem>> CreateItem(
        CreateItemRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Name"] = ["Name is required and cannot be empty."]
                });
        }

        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/v1/items/{item.Id}", item);
    }

    // PUT /api/v1/items/{id}
    private static async Task<Results<Ok<Item>, NotFound, ValidationProblem>> UpdateItem(
        Guid id,
        UpdateItemRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["Name"] = ["Name is required and cannot be empty."]
                });
        }

        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        item.Name = request.Name.Trim();
        item.Description = request.Description?.Trim();
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(item);
    }

    // DELETE /api/v1/items/{id}
    private static async Task<Results<NoContent, NotFound>> DeleteItem(
        Guid id,
        AppDbContext db,
        CancellationToken ct)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        db.Items.Remove(item);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
