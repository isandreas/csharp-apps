using System.ComponentModel.DataAnnotations;

namespace Api.Models;

/// <summary>
/// Represents an application item resource.
/// </summary>
public sealed class Item
{
    public Guid Id { get; init; } = Guid.NewGuid();

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Request body for creating a new item.</summary>
public sealed record CreateItemRequest(
    [property: Required, MaxLength(256)] string Name,
    [property: MaxLength(2000)] string? Description);

/// <summary>Request body for updating an existing item.</summary>
public sealed record UpdateItemRequest(
    [property: Required, MaxLength(256)] string Name,
    [property: MaxLength(2000)] string? Description);

/// <summary>Paginated list response wrapper.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);
