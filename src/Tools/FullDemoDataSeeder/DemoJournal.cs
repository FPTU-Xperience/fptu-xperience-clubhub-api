using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FullDemoDataSeeder;

public sealed class DemoJournal
{
    public string Dataset { get; set; } = FullDemoOptions.Dataset;
    public DateOnly ReferenceDate { get; set; }
    public List<int> ClubIds { get; set; } = [];
    public Dictionary<string, string> Checkpoints { get; set; } = [];
    public List<DemoRowReceipt> Rows { get; set; } = [];
    public string? ProtectedIdentityDigest { get; set; }

    public static DemoJournal Load(FullDemoOptions options)
    {
        var journal = File.Exists(options.JournalPath)
            ? JsonSerializer.Deserialize<DemoJournal>(File.ReadAllText(options.JournalPath))! : new DemoJournal { ReferenceDate = options.ReferenceDate };
        if (journal.Dataset != FullDemoOptions.Dataset || journal.ReferenceDate != options.ReferenceDate)
            throw new InvalidOperationException("Journal belongs to another dataset/date. Reuse its original reference date.");
        return journal;
    }

    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }

    public static string Snapshot(EntityEntry entry) => JsonSerializer.Serialize(entry.Properties
        .OrderBy(p => p.Metadata.Name, StringComparer.Ordinal).ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public void Observe(string service, EntityEntry entry, bool created)
    {
        var key = JsonSerializer.Serialize(entry.Metadata.FindPrimaryKey()!.Properties
            .ToDictionary(p => p.Name, p => entry.Property(p.Name).CurrentValue));
        var table = entry.Metadata.GetTableName()!;
        var snapshot = Snapshot(entry);
        var receipt = Rows.SingleOrDefault(r => r.Service == service && r.Table == table && r.KeyJson == key);
        if (receipt is null)
        {
            receipt = new DemoRowReceipt { Service = service, Table = table, KeyJson = key, BeforeJson = created ? null : snapshot };
            Rows.Add(receipt);
        }
        receipt.AfterDigest = Digest(snapshot);
    }

    public async Task SaveAsync(string service, DbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var known = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        var changes = db.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => (Entry: e, Added: e.State == EntityState.Added, Before: e.State == EntityState.Added ? null :
                JsonSerializer.Serialize(e.OriginalValues.Properties.OrderBy(p => p.Name, StringComparer.Ordinal)
                    .ToDictionary(p => p.Name, p => e.OriginalValues[p.Name])))).ToList();
        foreach (var (entry, added, before) in changes.Where(c => !c.Added))
        {
            var key = JsonSerializer.Serialize(entry.Metadata.FindPrimaryKey()!.Properties
                .ToDictionary(p => p.Name, p => entry.Property(p.Name).CurrentValue));
            var table = entry.Metadata.GetTableName()!;
            var prior = Rows.SingleOrDefault(r => r.Service == service && r.Table == table && r.KeyJson == key);
            if (prior is not null && prior.AfterDigest != Digest(before!))
                throw new InvalidOperationException($"Demo row {service}.{table} {key} changed after its checkpoint. Preserve the human edit and reconcile before resuming.");
        }
        await db.SaveChangesAsync();
        changes.AddRange(db.ChangeTracker.Entries().Where(e => !known.Contains(e.Entity))
            .Select(e => (e, true, (string?)null)));
        foreach (var (entry, added, before) in changes)
        {
            var key = JsonSerializer.Serialize(entry.Metadata.FindPrimaryKey()!.Properties
                .ToDictionary(p => p.Name, p => entry.Property(p.Name).CurrentValue));
            var table = entry.Metadata.GetTableName()!;
            var receipt = Rows.SingleOrDefault(r => r.Service == service && r.Table == table && r.KeyJson == key);
            if (receipt is null)
            {
                receipt = new DemoRowReceipt { Service = service, Table = table, KeyJson = key, BeforeJson = added ? null : before };
                Rows.Add(receipt);
            }
            receipt.AfterDigest = Digest(Snapshot(entry));
        }
    }
}

public sealed class DemoRowReceipt
{
    public string Service { get; set; } = "";
    public string Table { get; set; } = "";
    public string KeyJson { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string AfterDigest { get; set; } = "";
}
