using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ClubReportHub.Shared.Data;

public static class BoundedJsonRequest
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static async Task<byte[]> ReadBytesAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maxBytes) throw new BadHttpRequestException("Payload too large.", 413);
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public static async Task<T> ReadAsync<T>(HttpContext http, int maxBytes, CancellationToken ct)
    {
        if (http.Request.ContentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) != true)
            throw new BadHttpRequestException("Use application/json.", 415);
        if (http.Request.ContentLength > maxBytes) throw new BadHttpRequestException("Payload too large.", 413);
        var bytes = await ReadBytesAsync(http.Request.Body, maxBytes, ct);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 20 });
        ValidateUnique(document.RootElement);
        return document.RootElement.Deserialize<T>(Options) ?? throw new JsonException();
    }
    private static void ValidateUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in value.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw new JsonException("Duplicate fields.");
                ValidateUnique(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateUnique(item);
    }
    public static string Utf8(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
}
