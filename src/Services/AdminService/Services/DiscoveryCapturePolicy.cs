using System.Text.Json;
using AdminService.Contracts;
using AdminService.Data;
using Microsoft.EntityFrameworkCore;

namespace AdminService.Services;

internal static class DiscoveryCapturePolicy
{
    internal static DiscoveryCaptureSettings Decode(string? json) => json is null
        ? DiscoveryCaptureSettings.Default
        : JsonSerializer.Deserialize<DiscoveryCaptureSettings>(json) ?? throw new JsonException("Missing capture settings.");

    internal static string Encode(DiscoveryCaptureSettings settings) => JsonSerializer.Serialize(settings);

    internal static async Task<DiscoveryCaptureSettings> ReadAsync(AdminDbContext db, CancellationToken ct)
    {
        var json = await db.PlatformSettings.AsNoTracking()
            .Select(row => row.DiscoveryCaptureJson).FirstOrDefaultAsync(ct);
        return Decode(json);
    }
}
