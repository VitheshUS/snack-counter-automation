using Microsoft.EntityFrameworkCore;
using SnackCounter.Core;

namespace SnackCounter.Tests;

public class BudgetTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"budget-{Guid.NewGuid()}.db");
    private readonly Clock clock = new();
    private readonly BudgetOptions options = new();
    private SnackDb Db() => new(new DbContextOptionsBuilder<SnackDb>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options);
    private async Task<T> Finance<T>(Func<BudgetService, Task<T>> action)
    {
        await using var db = Db();
        return await action(new(db, clock, options));
    }
    private async Task<T> Refill<T>(Func<RefillService, Task<T>> action)
    {
        await using var db = Db();
        return await action(new(db, clock, new SimulatedChat(), budgets: options));
    }
    private async Task<int> Reserved(int office = 1)
    {
        await using var db = Db();
        await db.InitializeAsync();
        var employee = office == 1 ? 1 : 4;
        var id = (await Refill(s => s.Report(office, employee))).Id;
        Assert.True(await Refill(s => s.OfferHelp(id, employee)));
        return id;
    }
    private Task<BudgetResult> Buy(int id, long amount, string currency = "NOK", int employee = 1) =>
        Finance(s => s.Authorize(id, employee, amount, currency));
    private async Task<BudgetBalance> Balance(int office = 1) => (await Finance(s => s.GetBalance(office)))!;

    [Fact]
    public async Task OfficesHaveIndependentBudgetsAndCurrencies()
    {
        var oslo = await Reserved();
        var bengaluru = await Reserved(2);
        Assert.True((await Buy(oslo, 600_000)).Success);
        Assert.True((await Buy(bengaluru, 1_500_000, "INR", 4)).Success);
        Assert.Equal(0, (await Balance()).AvailableMinor);
        Assert.Equal(0, (await Balance(2)).AvailableMinor);
        Assert.Equal("INR", (await Balance(2)).Currency);
    }
    [Fact]
    public async Task MonthlyExhaustionRollsBackPurchaseAndBudgetChanges()
    {
        var first = await Reserved();
        Assert.True((await Buy(first, 600_000)).Success);
        Assert.True((await Finance(s => s.Resolve(first, 1, 600_000, "NOK"))).Success);
        var next = await Reserved();
        Assert.False((await Buy(next, 1)).Success);
        await using var db = Db();
        Assert.Equal(RefillState.Reserved, (await db.RefillRequests.FindAsync(next))!.State);
        Assert.Equal(1, await db.PurchaseCommitments.CountAsync());
        Assert.Equal(0, (await Balance()).ReservedMinor);
    }
    [Theory]
    [InlineData(0, "NOK", 1)]
    [InlineData(-1, "NOK", 1)]
    [InlineData(100, "INR", 1)]
    [InlineData(100, "NOK", 2)]
    [InlineData(1000000000001, "NOK", 1)]
    public async Task InvalidAuthorizationDoesNotChangeStateOrConsumeFunds(long amount, string currency, int employee)
    {
        var id = await Reserved();
        Assert.False((await Buy(id, amount, currency, employee)).Success);
        Assert.Equal(600_000, (await Balance()).AvailableMinor);
        await using var db = Db();
        Assert.Empty(await db.PurchaseCommitments.ToArrayAsync());
        Assert.Equal(RefillState.Reserved, (await db.RefillRequests.FindAsync(id))!.State);
    }
    [Fact]
    public async Task ReleasedAllowanceIsSoftAndUnusedAllocationCarriesForward()
    {
        var id = await Reserved(); // January 1: 150,000 released.
        var result = await Buy(id, 200_000);
        Assert.True(result.Success);
        Assert.True(result.AllowanceWarning);
        Assert.True((await Finance(s => s.Resolve(id, 1, 100_000, "NOK"))).Success);
        clock.Now = new(2026, 1, 8, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(300_000, (await Balance()).ReleasedMinor);
        var second = await Reserved();
        var next = await Buy(second, 200_000); // 100,000 spent + 200,000 = cumulative allowance.
        Assert.True(next.Success);
        Assert.False(next.AllowanceWarning);
    }
    [Theory]
    [InlineData(1, 150000)]
    [InlineData(7, 150000)]
    [InlineData(8, 300000)]
    [InlineData(15, 450000)]
    [InlineData(22, 600000)]
    [InlineData(31, 600000)]
    public async Task FourCumulativePeriodsCoverWholeMonth(int day, long released)
    {
        await Reserved();
        clock.Now = new(2026, 1, day, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(released, (await Balance()).ReleasedMinor);
    }
    [Fact]
    public async Task ConcurrentAuthorizationsCannotConsumeRemainingBudgetTwice()
    {
        var first = await Reserved();
        Assert.True((await Buy(first, 300_000)).Success);
        Assert.True((await Finance(s => s.Resolve(first, 1, 300_000, "NOK"))).Success);
        var id = await Reserved();
        using var barrier = new Barrier(2);
        Task<BudgetResult> Compete() => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Buy(id, 200_000);
        });
        var results = await Task.WhenAll(Compete(), Compete());
        Assert.Single(results, r => r.Success);
        var balance = await Balance();
        Assert.Equal(100_000, balance.AvailableMinor);
        Assert.Equal(200_000, balance.ReservedMinor);
    }
    [Theory]
    [InlineData(80000, false, 520000)]
    [InlineData(100000, false, 500000)]
    [InlineData(700000, true, -100000)]
    public async Task ReconciliationReplacesEstimateAndRecordsFullOverrun(long actual, bool review, long available)
    {
        var id = await Reserved();
        Assert.True((await Buy(id, 100_000)).Success);
        Assert.Equal(100_000, (await Balance()).ReservedMinor);
        var result = await Finance(s => s.Resolve(id, 1, actual, "NOK"));
        Assert.True(result.Success);
        Assert.Equal(review, result.RequiresHrReview);
        var balance = await Balance();
        Assert.Equal(available, balance.AvailableMinor);
        Assert.Equal(actual, balance.SpentMinor);
        Assert.Equal(0, balance.ReservedMinor);
        Assert.False((await Finance(s => s.Resolve(id, 1, actual, "NOK"))).Success);
        await using var db = Db();
        var purchase = await db.PurchaseCommitments.SingleAsync();
        Assert.Equal(actual, purchase.ActualMinor);
        Assert.Equal(review, purchase.RequiresHrReview);
    }
    [Fact]
    public async Task CancellationRequiresExplicitOwnerConfirmationOfZeroSpending()
    {
        var id = await Reserved();
        Assert.True(await Refill(s => s.Transition(id, 1, "release")));
        Assert.Equal(0, (await Balance()).ReservedMinor);
        Assert.True(await Refill(s => s.OfferHelp(id, 1)));
        Assert.True((await Buy(id, 100_000)).Success);
        Assert.False((await Finance(s => s.Resolve(id, 1, 1, "NOK", true))).Success);
        Assert.False((await Finance(s => s.Resolve(id, 2, 0, "NOK", true))).Success);
        Assert.Equal(100_000, (await Balance()).ReservedMinor);
        Assert.True((await Finance(s => s.Resolve(id, 1, 0, "NOK", true))).Success);
        Assert.Equal(600_000, (await Balance()).AvailableMinor);
        await using var db = Db();
        Assert.True((await db.PurchaseCommitments.SingleAsync()).Cancelled);
        Assert.Equal(RefillState.Open, (await db.RefillRequests.FindAsync(id))!.State);
    }
    [Fact]
    public async Task NeedsReviewAndMonthRolloverPreserveOriginalCommitment()
    {
        clock.Now = new(2026, 1, 31, 12, 0, 0, TimeSpan.Zero);
        var id = await Reserved();
        Assert.True((await Buy(id, 100_000)).Success);
        clock.Now = new(2026, 2, 2, 12, 0, 0, TimeSpan.Zero);
        await Refill(async s => { await s.Sweep(); return true; });
        Assert.Equal("2026-02", (await Balance()).Month);
        Assert.Equal(600_000, (await Balance()).AvailableMinor);
        await using (var db = Db())
        {
            Assert.Equal(RefillState.NeedsReview, (await db.RefillRequests.FindAsync(id))!.State);
            var held = await db.PurchaseCommitments.SingleAsync();
            Assert.Equal("2026-01", held.Month);
            Assert.Null(held.ActualMinor);
            Assert.False(held.Cancelled);
        }
        Assert.True((await Finance(s => s.Resolve(id, 1, 80_000, "NOK"))).Success);
        Assert.Equal(0, (await Balance()).SpentMinor);
        await using var verify = Db();
        Assert.Equal(80_000, (await verify.PurchaseCommitments.SingleAsync()).ActualMinor);
    }
    [Fact]
    public async Task OfficeLocalMonthBoundariesAndPolicySnapshotsAreRespected()
    {
        await Reserved();
        await Reserved(2);
        clock.Now = new(2026, 1, 31, 20, 0, 0, TimeSpan.Zero);
        Assert.Equal("2026-01", (await Balance()).Month);
        Assert.Equal("2026-02", (await Balance(2)).Month);
        options.Offices[1].MonthlyLimitMinor = 800_000;
        Assert.Equal(600_000, (await Balance()).LimitMinor);
        clock.Now = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(800_000, (await Balance()).LimitMinor);
    }
    [Fact]
    public async Task ExactExpiryDuringAuthorizationRollsBackLedgerAndTask()
    {
        var id = await Reserved();
        clock.Now += TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1);
        var reads = 0;
        clock.BeforeRead = () => { if (++reads == 2) clock.Now += TimeSpan.FromTicks(1); };
        Assert.False((await Buy(id, 100_000)).Success);
        clock.BeforeRead = null;
        await using var db = Db();
        Assert.Empty(await db.PurchaseCommitments.ToArrayAsync());
        Assert.Empty(await db.MonthlyBudgets.ToArrayAsync()); // Lazy monthly row creation rolled back too.
        Assert.Equal(RefillState.Reserved, (await db.RefillRequests.FindAsync(id))!.State);
    }
    [Fact]
    public async Task PhaseOneDatabaseGetsBudgetTablesWithoutLosingExistingRows()
    {
        var id = await Reserved();
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE PurchaseCommitments; DROP TABLE MonthlyBudgets;");
            await db.InitializeAsync();
            await db.InitializeAsync();
            Assert.Equal(6, await db.Employees.CountAsync());
            Assert.Equal(1, await db.RefillRequests.CountAsync());
        }
        Assert.True((await Buy(id, 100_000)).Success);
    }
    [Fact]
    public async Task MonthChangingDuringAuthorizationRequiresRetryWithoutConsumingFunds()
    {
        clock.Now = new(2026, 1, 31, 22, 59, 59, TimeSpan.Zero); // Oslo midnight is 23:00 UTC.
        var id = await Reserved();
        var reads = 0;
        clock.BeforeRead = () => { if (++reads == 2) clock.Now += TimeSpan.FromSeconds(1); };
        Assert.False((await Buy(id, 100_000)).Success);
        clock.BeforeRead = null;
        Assert.True((await Buy(id, 100_000)).Success);
        await using var db = Db();
        Assert.Equal("2026-02", (await db.PurchaseCommitments.SingleAsync()).Month);
    }
    public void Dispose() => File.Delete(path);
    private class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public Action? BeforeRead;
        public override DateTimeOffset GetUtcNow() { BeforeRead?.Invoke(); return Now; }
    }
}
