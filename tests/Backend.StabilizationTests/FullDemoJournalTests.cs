using FullDemoDataSeeder;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Backend.StabilizationTests;

public sealed class FullDemoJournalTests
{
    private sealed class Row { public int Id { get; set; } public string Title { get; set; } = ""; }
    private sealed class Fixture(DbContextOptions<Fixture> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<Row>().ToTable("Rows");
    }

    [Fact]
    public async Task PartialResumeRefusesToOverwriteHumanChanges()
    {
        var options = new DbContextOptionsBuilder<Fixture>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var journal = new DemoJournal();
        await using (var initial = new Fixture(options))
        {
            initial.Rows.Add(new Row { Id = 1, Title = "Seeded" });
            await journal.SaveAsync("Fixture", initial);
        }
        await using (var human = new Fixture(options))
        {
            (await human.Rows.SingleAsync()).Title = "Human edit";
            await human.SaveChangesAsync();
        }
        var originalReceipt = journal.Rows.Single().AfterDigest;
        await using (var resumed = new Fixture(options))
        {
            (await resumed.Rows.SingleAsync()).Title = "Later seed phase";
            await Assert.ThrowsAsync<InvalidOperationException>(() => journal.SaveAsync("Fixture", resumed));
        }
        await using var check = new Fixture(options);
        Assert.Equal("Human edit", (await check.Rows.SingleAsync()).Title);
        Assert.Equal(originalReceipt, journal.Rows.Single().AfterDigest);
    }

    [Fact]
    public async Task LaterPhaseCanUpdateAnUnchangedOwnedRow()
    {
        var options = new DbContextOptionsBuilder<Fixture>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var journal = new DemoJournal();
        await using var db = new Fixture(options);
        var row = new Row { Id = 1, Title = "Seeded" }; db.Add(row);
        await journal.SaveAsync("Fixture", db);
        row.Title = "Linked";
        await journal.SaveAsync("Fixture", db);
        Assert.Equal(DemoJournal.Digest(DemoJournal.Snapshot(db.Entry(row))), journal.Rows.Single().AfterDigest);
        Assert.Null(journal.Rows.Single().BeforeJson);
    }
}
