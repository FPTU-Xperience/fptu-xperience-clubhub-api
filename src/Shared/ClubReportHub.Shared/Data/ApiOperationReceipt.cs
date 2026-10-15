using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Data;
using ClubReportHub.Shared.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClubReportHub.Shared.Data;

public sealed class ApiOperationReceipt
{
    public long Id { get; set; }
    public int ActorId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class ApiOperations
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void ApplyApiOperationConfiguration(this ModelBuilder model)
    {
        model.Entity<ApiOperationReceipt>(entity =>
        {
            entity.HasIndex(x => new { x.ActorId, x.Operation, x.Key }).IsUnique();
            entity.Property(x => x.Operation).HasMaxLength(300);
            entity.Property(x => x.Key).HasMaxLength(100);
            entity.Property(x => x.RequestHash).HasMaxLength(64);
        });
    }

    // Call after CURRENT resource authorization, including on a replay. The receipt
    // and all handler saves commit together; it never caches authorization.
    public static async Task<IResult> ExecuteAsync(
        HttpContext http, DbContext db, object request, Func<Task<IResult>> action,
        CancellationToken cancellationToken, bool required = true)
    {
        var key = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(key))
        {
            if (required) return Error(400, "IDEMPOTENCY_KEY_REQUIRED", "Provide Idempotency-Key.");
        }
        if (key.Length > 100 || key.Any(c => c < 33 || c > 126))
            return Error(400, "INVALID_IDEMPOTENCY_KEY", "Use 1–100 printable ASCII characters.");

        var actor = http.User.GetUserId();
        var operation = http.Request.Method + " " + http.Request.Path.Value;
        if (operation.Length > 300)
            return Error(400, "INVALID_OPERATION", "The operation path is too long.");
        var hash = Fingerprint(request);
        async Task<ApiOperationReceipt?> FindAsync() => await db.Set<ApiOperationReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ActorId == actor && x.Operation == operation && x.Key == key, cancellationToken);
        var previous = string.IsNullOrEmpty(key) ? null : await FindAsync();
        if (previous is not null) return Replay(previous, hash);

        // Retain the handler's object references while resetting changes after a
        // rolled-back execution-strategy attempt. A stale resource then fails CAS.
        var original = db.ChangeTracker.Entries().Select(e => new
        { e.Entity, e.State, Current = e.CurrentValues.Clone(), Original = e.OriginalValues.Clone() }).ToArray();
        void Restore()
        {
            db.ChangeTracker.Clear();
            foreach (var saved in original)
            {
                var entry = db.Entry(saved.Entity);
                entry.State = EntityState.Unchanged;
                entry.CurrentValues.SetValues(saved.Current);
                entry.OriginalValues.SetValues(saved.Original);
                entry.State = saved.State;
            }
        }

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var recovered = string.IsNullOrEmpty(key) ? null : await FindAsync();
            if (recovered is not null) return Replay(recovered, hash);
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
            var committed = false;
            try
            {
                var result = await action();
                var status = (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
                if (status is < 200 or >= 300)
                {
                    if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                    return result;
                }
                if (!string.IsNullOrEmpty(key))
                {
                    if (result is not IValueHttpResult && status != StatusCodes.Status204NoContent)
                        throw new InvalidOperationException("Idempotent operations must return a JSON value.");
                    db.Set<ApiOperationReceipt>().Add(new ApiOperationReceipt
                    {
                        ActorId = actor,
                        Operation = operation,
                        Key = key,
                        RequestHash = hash,
                        ResponseJson = JsonSerializer.Serialize((result as IValueHttpResult)?.Value, JsonOptions),
                        StatusCode = status,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    });
                }
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                committed = true;
                return result;
            }
            catch (DbUpdateException)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                var winner = string.IsNullOrEmpty(key) ? null : await FindAsync();
                return winner is not null ? Replay(winner, hash)
                    : Error(409, "CONCURRENT_UPDATE", "The resource changed. Reload before retrying.");
            }
            finally { if (!committed) Restore(); }
        });
    }

    private static string Fingerprint(object request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(JsonSerializer.SerializeToElement(request, JsonOptions), writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); Write(property.Value, writer); }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(item, writer); writer.WriteEndArray();
        }
        else element.WriteTo(writer);
    }

    private static IResult Replay(ApiOperationReceipt receipt, string hash)
    {
        if (receipt.RequestHash != hash)
            return Error(409, "IDEMPOTENCY_KEY_REUSED", "This key was used with another request.");
        if (receipt.StatusCode == StatusCodes.Status204NoContent) return Results.NoContent();
        using var document = JsonDocument.Parse(receipt.ResponseJson);
        return Results.Json(document.RootElement.Clone(), statusCode: receipt.StatusCode);
    }

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new { code, message }, statusCode: status);
}
