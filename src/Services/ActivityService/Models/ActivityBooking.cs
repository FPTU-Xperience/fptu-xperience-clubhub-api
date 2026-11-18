using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ActivityService.Models;

public sealed class CampusVenue
{
    public int Id { get; set; }
    public string CampusCode { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string VenueType { get; set; } = "";
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public int Capacity { get; set; }
    public int? PlanX { get; set; }
    public int? PlanY { get; set; }
    public string? PlanArea { get; set; }
    [JsonIgnore]
    public string? SharedFloorAreas { get; set; }
    [NotMapped]
    public Dictionary<string, string>? PlanAreasByFloor => SharedFloorAreas is null
        ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(SharedFloorAreas);
    public bool IsBookable { get; set; } = true;
}

public sealed class CampusBookingPolicy
{
    public string CampusCode { get; set; } = "";
    public int CutoffDay { get; set; }
    public TimeOnly CutoffTime { get; set; }
}

public sealed class CampusBookingLayout
{
    public string CampusCode { get; set; } = "";
    public string Building { get; set; } = "";
    public string Floor { get; set; } = "";
    public string? ImageUrl { get; set; }
    public int? PlanX { get; set; }
    public int? PlanY { get; set; }
}

public sealed class CampusEquipment
{
    public int Id { get; set; }
    public string CampusCode { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public bool IsBookable { get; set; } = true;
}

public sealed class ActivityBooking
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public ActivitySession Session { get; set; } = null!;
    public string CampusCode { get; set; } = "";
    public int? RequestedVenueId { get; set; }
    public int? ConfirmedVenueId { get; set; }
    public string? OriginalLocation { get; set; }
    public string? RequestedVenueType { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public string Status { get; set; } = "PENDING";
    public int RequestedByUserId { get; set; }
    public int? ProcessedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? CutoffAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<ActivityBookingEquipment> Equipment { get; set; } = [];
    public List<ActivityBookingVenue> AdditionalVenues { get; set; } = [];
}

public sealed class ActivityBookingVenue
{
    public int BookingId { get; set; }
    public int VenueId { get; set; }
    public bool IsConfirmed { get; set; }
}

public sealed class ActivityBookingEquipment
{
    public int BookingId { get; set; }
    public int EquipmentId { get; set; }
    public int RequestedQuantity { get; set; }
    public int? ConfirmedQuantity { get; set; }
}
