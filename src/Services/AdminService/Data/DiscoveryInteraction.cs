using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Data;

[PrimaryKey(nameof(UserId), nameof(EventId))]
[Index(nameof(UserId), nameof(OccurredAtUtc))]
[Index(nameof(OccurredAtUtc))]
public sealed class DiscoveryInteraction
{
    public int UserId { get; set; }
    public Guid EventId { get; set; }
    [MaxLength(8)] public string EntityKind { get; set; } = "";
    public int EntityId { get; set; }
    [MaxLength(8)] public string Action { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string TagIdsJson { get; set; } = "[]";
}
