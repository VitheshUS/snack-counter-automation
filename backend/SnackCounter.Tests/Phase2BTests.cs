using Microsoft.EntityFrameworkCore;
using SnackCounter.Core;

namespace SnackCounter.Tests;

public class Phase2BTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"hr-{Guid.NewGuid()}.db");
    private readonly Clock clock = new();
    private readonly BudgetOptions budgets = new();
    private readonly ReportingOptions rates = new();
    private SnackDb Db() => new(new DbContextOptionsBuilder<SnackDb>().UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options);
    private async Task<T> Claims<T>(Func<ReimbursementService, Task<T>> operation)
    {
        await using var db = Db();
        return await operation(new(db, clock, new SimulatedHr(db, clock)));
    }
    private async Task<(int Request, int Purchase)> Purchase(int office = 1, long actual = 10000, bool complete = true, bool cancel = false)
    {
        await using var db = Db();
        await db.InitializeAsync();
        var employee = office == 1 ? 2 : 5;
        var reporter = office == 1 ? 1 : 4;
        var refills = new RefillService(db, clock, new SimulatedChat());
        var request = await refills.Report(office, reporter);
        Assert.True(await refills.OfferHelp(request.Id, employee));
        var finance = new BudgetService(db, clock, budgets);
        var currency = office == 1 ? "NOK" : "INR";
        Assert.True((await finance.Authorize(request.Id, employee, 10000, currency)).Success);
        if (complete) Assert.True((await finance.Resolve(request.Id, employee, cancel ? 0 : actual, currency, cancel)).Success);
        return (request.Id, await db.PurchaseCommitments.AsNoTracking().Where(p => db.RefillAssignments.Any(a =>
            a.Id == p.RefillAssignmentId && a.RefillRequestId == request.Id && a.EmployeeId == employee))
            .OrderByDescending(p => p.Id).Select(p => p.Id).FirstAsync());
    }
    private async Task<ReimbursementClaim> Draft(int purchase, int employee = 2) =>
        (await Claims(s => s.Draft(purchase, employee, "Office snacks", "private-receipt:demo-123")))!;
    private async Task<ConsolidatedSpending> Report(string month = "2026-01")
    {
        await using var db = Db();
        return await new ReportingService(db, clock, budgets, rates).Consolidated(month);
    }
    [Fact]
    public async Task ClaimDiscoveryReturnsEmptyWithoutCreatingOrSubmittingClaims()
    {
        await Purchase();
        Assert.Empty(await Claims(s => s.ForEmployee(2)));
        Assert.Empty(await Claims(s => s.ForEmployee(999)));
        await using var db = Db();
        Assert.Empty(await db.ReimbursementClaims.ToArrayAsync());
        Assert.Empty(await db.MockHrClaims.ToArrayAsync());
    }
    [Fact]
    public async Task ClaimDiscoveryFiltersEmployeeAndPurchaseOwnership()
    {
        var oslo = await Purchase();
        var bengaluru = await Purchase(2);
        var osloClaim = await Draft(oslo.Purchase);
        await Draft(bengaluru.Purchase, 5);
        Assert.Equal(osloClaim.Id, Assert.Single(await Claims(s => s.ForEmployee(2))).Id);
        Assert.Single(await Claims(s => s.ForEmployee(5)));
        Assert.Empty(await Claims(s => s.ForEmployee(1))); // Reporter is not the claimant.
        // Defense in depth: inconsistent ownership cannot expose another purchaser's receipt.
        await using (var db = Db())
            await db.ReimbursementClaims.Where(c => c.Id == osloClaim.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.EmployeeId, 3));
        Assert.Empty(await Claims(s => s.ForEmployee(2)));
        Assert.Empty(await Claims(s => s.ForEmployee(3)));
    }
    [Fact]
    public async Task ClaimDiscoveryPreservesStatusReceiptAndIdempotencyAcrossReads()
    {
        var purchase = await Purchase();
        var draft = await Draft(purchase.Purchase);
        await Claims(s => s.Submit(draft.Id, 2));
        var first = Assert.Single(await Claims(s => s.ForEmployee(2)));
        var second = Assert.Single(await Claims(s => s.ForEmployee(2)));
        Assert.Equal(ClaimState.PendingHR, first.State);
        Assert.Equal("private-receipt:demo-123", first.ReceiptReference);
        Assert.Equal(draft.IdempotencyKey, first.IdempotencyKey);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        await using var db = Db();
        Assert.Equal(1, await db.ReimbursementClaims.CountAsync());
        Assert.Equal(1, await db.MockHrClaims.CountAsync());
        Assert.Equal(10000, (await db.PurchaseCommitments.SingleAsync()).ActualMinor);
    }
    [Fact]
    public async Task SuccessfulSubmissionContainsAuthoritativeEmployeeActualCurrencyAndReceipt()
    {
        var purchase = await Purchase(2, 12000);
        var draft = await Draft(purchase.Purchase, 5);
        Assert.Equal(ClaimState.Draft, draft.State);
        var claim = (await Claims(s => s.Submit(draft.Id, 5)))!;
        Assert.Equal(ClaimState.PendingHR, claim.State);
        Assert.True(claim.RequiresHrReview);
        await using var db = Db();
        var external = await db.MockHrClaims.SingleAsync();
        Assert.Equal(5, external.EmployeeId);
        Assert.Equal(12000, external.AmountMinor);
        Assert.Equal("INR", external.Currency);
        Assert.Equal("Office snacks", external.Description);
        Assert.Equal("private-receipt:demo-123", external.ReceiptReference);
        Assert.True(external.RequiresHrReview);
        Assert.Equal(draft.IdempotencyKey, external.IdempotencyKey);
    }
    [Fact]
    public async Task DuplicateDraftsAndSubmissionRetriesKeepOneLogicalAndOneExternalClaim()
    {
        var purchase = await Purchase();
        var first = await Draft(purchase.Purchase);
        var second = await Draft(purchase.Purchase);
        Assert.Equal(first.Id, second.Id);
        await Claims(s => s.Submit(first.Id, 2));
        await Claims(s => s.Submit(first.Id, 2));
        await using var db = Db();
        Assert.Equal(1, await db.ReimbursementClaims.CountAsync());
        Assert.Equal(1, await db.MockHrClaims.CountAsync());
    }
    [Fact]
    public async Task ConcurrentDraftAndSubmitCallsCannotCreateDuplicates()
    {
        var purchase = await Purchase();
        using var barrier = new Barrier(2);
        Task<int> Submit() => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            var claim = await Draft(purchase.Purchase);
            await Claims(s => s.Submit(claim.Id, 2));
            return claim.Id;
        });
        var ids = await Task.WhenAll(Submit(), Submit());
        Assert.Equal(ids[0], ids[1]);
        var reconciled = await Claims(s => s.Reconcile(ids[0], 2));
        Assert.Equal(ClaimState.PendingHR, reconciled!.State);
        await using var db = Db();
        Assert.Equal(1, await db.ReimbursementClaims.CountAsync());
        Assert.Equal(1, await db.MockHrClaims.CountAsync());
    }
    [Fact]
    public async Task DefiniteFailureCanRetryUsingSameKey()
    {
        var purchase = await Purchase();
        var draft = await Draft(purchase.Purchase);
        Assert.Equal(ClaimState.SubmissionFailed, (await Claims(s => s.Submit(draft.Id, 2, SubmissionMode.DefiniteFailure)))!.State);
        await using (var db = Db()) Assert.Empty(await db.MockHrClaims.ToArrayAsync());
        var retried = await Claims(s => s.Submit(draft.Id, 2));
        Assert.Equal(draft.IdempotencyKey, retried!.IdempotencyKey);
        Assert.Equal(ClaimState.PendingHR, retried.State);
    }
    [Theory]
    [InlineData(SubmissionMode.TimeoutAfterCreation, true)]
    [InlineData(SubmissionMode.TimeoutBeforeCreation, false)]
    public async Task UncertainOutcomeRequiresLookupBeforeRetry(SubmissionMode mode, bool exists)
    {
        var purchase = await Purchase();
        var draft = await Draft(purchase.Purchase);
        Assert.Equal(ClaimState.SubmissionUncertain, (await Claims(s => s.Submit(draft.Id, 2, mode)))!.State);
        Assert.Equal(ClaimState.SubmissionUncertain, (await Claims(s => s.Submit(draft.Id, 2)))!.State);
        var reconciled = await Claims(s => s.Reconcile(draft.Id, 2));
        Assert.Equal(exists ? ClaimState.PendingHR : ClaimState.SubmissionFailed, reconciled!.State);
        var retry = await Claims(s => s.Submit(draft.Id, 2));
        Assert.Equal(ClaimState.PendingHR, retry!.State);
        Assert.Equal(draft.IdempotencyKey, retry.IdempotencyKey);
        await using var db = Db();
        Assert.Single(await db.MockHrClaims.ToArrayAsync());
    }
    [Fact]
    public async Task PendingCrashRecoveryAndProviderIdempotencyArePersistent()
    {
        var purchase = await Purchase();
        var draft = await Draft(purchase.Purchase);
        await using (var db = Db())
        {
            await db.ReimbursementClaims.Where(c => c.Id == draft.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.State, ClaimState.SubmissionPending));
            var hr = new SimulatedHr(db, clock);
            await hr.Submit(draft, SubmissionMode.Success);
            await hr.Submit(draft, SubmissionMode.Success);
        }
        Assert.Equal(ClaimState.PendingHR, (await Claims(s => s.Reconcile(draft.Id, 2)))!.State);
        await using var verify = Db();
        Assert.Single(await verify.MockHrClaims.ToArrayAsync());
    }
    [Fact]
    public async Task ApprovalRequiresExplicitDecisionAndPaymentIsSeparate()
    {
        var purchase = await Purchase();
        var draft = await Draft(purchase.Purchase);
        await Claims(s => s.Submit(draft.Id, 2));
        await using var db = Db();
        var hr = new SimulatedHr(db, clock);
        Assert.False(await hr.Decide(draft.IdempotencyKey, ClaimState.Reimbursed));
        Assert.False(await hr.Decide(draft.IdempotencyKey, ClaimState.Approved));
        clock.Now += TimeSpan.FromDays(2);
        Assert.Equal(ClaimState.PendingHR, (await Claims(s => s.Get(draft.Id, 2)))!.State);
        Assert.True(await hr.Decide(draft.IdempotencyKey, ClaimState.Approved));
        Assert.Equal(ClaimState.Approved, (await Claims(s => s.Reconcile(draft.Id, 2)))!.State);
        Assert.True(await hr.Decide(draft.IdempotencyKey, ClaimState.Reimbursed));
        Assert.Equal(ClaimState.Reimbursed, (await Claims(s => s.Reconcile(draft.Id, 2)))!.State);
        Assert.False(await hr.Decide(draft.IdempotencyKey, ClaimState.Rejected));
    }
    [Fact]
    public async Task RejectionCannotRemoveActualSpendingOrCreateReplacementClaim()
    {
        var purchase = await Purchase();
        var claim = await Draft(purchase.Purchase);
        await Claims(s => s.Submit(claim.Id, 2));
        clock.Now += TimeSpan.FromDays(2);
        await using (var db = Db()) Assert.True(await new SimulatedHr(db, clock).Decide(claim.IdempotencyKey, ClaimState.Rejected));
        Assert.Equal(ClaimState.Rejected, (await Claims(s => s.Reconcile(claim.Id, 2)))!.State);
        Assert.Equal(claim.Id, (await Draft(purchase.Purchase)).Id);
        Assert.Equal(ClaimState.Rejected, (await Claims(s => s.Submit(claim.Id, 2)))!.State);
        Assert.Equal(10000, (await Report()).NativeTotals.Single(n => n.OfficeId == 1).SpentMinor);
    }
    [Fact]
    public async Task ClaimsRequireCompletedNonCancelledPositivePurchaseAndCorrectEmployee()
    {
        var cancelled = await Purchase(cancel: true);
        Assert.Null(await Claims(s => s.Draft(cancelled.Purchase, 2, "Snacks", "private-ref")));
        var pending = await Purchase(complete: false);
        Assert.Null(await Claims(s => s.Draft(pending.Purchase, 2, "Snacks", "private-ref")));
        await using (var db = Db())
            Assert.True((await new BudgetService(db, clock, budgets).Resolve(pending.Request, 2, 0, "NOK", true)).Success);
        var completed = await Purchase();
        Assert.Null(await Claims(s => s.Draft(completed.Purchase, 1, "Snacks", "private-ref")));
        var valid = await Draft(completed.Purchase);
        Assert.Null(await Claims(s => s.Get(valid.Id, 1)));
        Assert.Null(await Claims(s => s.Submit(valid.Id, 1)));
        await using (var db = Db())
        {
            var service = new RefillService(db, clock, new SimulatedChat());
            var request = await service.Report(2, 4);
            Assert.True(await service.OfferHelp(request.Id, 5));
            Assert.True(await service.Transition(request.Id, 5, "unnecessary"));
            Assert.False(await db.PurchaseCommitments.AnyAsync(p => db.RefillAssignments.Any(a => a.Id == p.RefillAssignmentId && a.RefillRequestId == request.Id)));
        }
        Assert.Null(await Claims(s => s.Draft(int.MaxValue, 5, "Unnecessary refill", "private-ref")));
    }
    [Theory]
    [InlineData("", "receipt")]
    [InlineData("snacks", " ")]
    public async Task ReceiptReferenceAndDescriptionAreRequired(string description, string receipt)
    {
        var purchase = await Purchase();
        await Assert.ThrowsAsync<ArgumentException>(() => Claims(s => s.Draft(purchase.Purchase, 2, description, receipt)));
    }
    [Fact]
    public async Task NativeAndConsolidatedReportingKeepSpendingAndCommitmentsSeparate()
    {
        await Purchase(1, 10000);
        await Purchase(2, 20000);
        await Purchase(1, complete: false);
        rates.InrToNokNumerator = 15;
        var report = await Report();
        var oslo = report.NativeTotals.Single(n => n.OfficeId == 1);
        var bengaluru = report.NativeTotals.Single(n => n.OfficeId == 2);
        Assert.Equal("NOK", oslo.Currency);
        Assert.Equal("INR", bengaluru.Currency);
        Assert.Equal(10000, oslo.SpentMinor);
        Assert.Equal(10000, oslo.OutstandingMinor);
        Assert.Equal(20000, bengaluru.SpentMinor);
        Assert.Equal(13000m, report.SpentMinor);
        Assert.Equal(10000m, report.OutstandingMinor);
        Assert.Equal("2026-01", report.ExchangeRate.Month);
        Assert.Equal(15, report.ExchangeRate.Numerator);
        Assert.Equal(100, report.ExchangeRate.Denominator);
        Assert.Equal("NOK", report.Currency);
    }
    [Fact]
    public async Task HistoricalRateSurvivesConfigChangesAndNewMonthUsesNewRate()
    {
        await Purchase(2, 20000);
        var original = await Report();
        rates.InrToNokNumerator = 50;
        clock.Now += TimeSpan.FromDays(35);
        Assert.Equal(original.SpentMinor, (await Report()).SpentMinor);
        Assert.Equal(original.ExchangeRate.RecordedAt, (await Report()).ExchangeRate.RecordedAt);
        await Purchase(2, 20000);
        var next = await Report("2026-02");
        Assert.Equal(10000m, next.SpentMinor);
        Assert.Equal(50, next.ExchangeRate.Numerator);
    }
    [Fact]
    public async Task ReporterVolunteerAndInspectionPointsAreAwardedOnceIndependentOfSpending()
    {
        var completed = await Purchase(1, 10000);
        await using var db = Db();
        var service = new RefillService(db, clock, new SimulatedChat());
        var first = await service.Report(1, 1);
        var duplicate = await service.Report(1, 3);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.True(await service.OfferHelp(first.Id, 3));
        Assert.True(await service.Transition(first.Id, 3, "unnecessary"));
        Assert.False(await service.Transition(first.Id, 3, "unnecessary"));
        Assert.False((await new BudgetService(db, clock, budgets).Resolve(completed.Request, 2, 10000, "NOK")).Success);
        var leaderboard = await Contributions.Leaderboard(db);
        Assert.Equal(5, leaderboard.Single(e => e.EmployeeId == 2).Points);
        Assert.Equal(2, leaderboard.Single(e => e.EmployeeId == 1).Points); // Two confirmed reports.
        Assert.Equal(2, leaderboard.Single(e => e.EmployeeId == 3).Points); // Inspection, no duplicate report credit.
        Assert.Equal(4, await db.Contributions.CountAsync());
        var expensive = await Purchase(2, 50000);
        Assert.Equal(5, (await Contributions.Leaderboard(db)).Single(e => e.EmployeeId == 5).Points);
        var claim = await Draft(completed.Purchase);
        await Claims(s => s.Submit(claim.Id, 2));
        await Claims(s => s.Submit(claim.Id, 2));
        Assert.Equal(6, await db.Contributions.CountAsync());
    }
    [Fact]
    public async Task DeclineIgnoreAndCancelHaveNoPointPenaltyOrReward()
    {
        await Purchase(cancel: true);
        await using var db = Db();
        var service = new RefillService(db, clock, new SimulatedChat());
        var request = await service.Report(1, 1);
        await service.Invite(request.Id);
        Assert.True(await service.Decline(request.Id, 1));
        clock.Now += TimeSpan.FromHours(2);
        await service.Sweep();
        Assert.Empty(await db.Contributions.ToArrayAsync());
        Assert.All(await Contributions.Leaderboard(db), e => Assert.Equal(0, e.Points));
    }
    [Fact]
    public async Task ExistingDatabaseUpgradeCreatesPhase2BTablesIdempotently()
    {
        var purchase = await Purchase();
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE ReimbursementClaims; DROP TABLE MockHrClaims; DROP TABLE Contributions; DROP TABLE ReportingRates;");
            await db.InitializeAsync();
            await db.InitializeAsync();
            Assert.Equal(1, await db.PurchaseCommitments.CountAsync());
        }
        Assert.NotNull(await Draft(purchase.Purchase));
        Assert.Equal(10000, (await Report()).NativeTotals.Single(n => n.OfficeId == 1).SpentMinor);
    }
    public void Dispose() => File.Delete(path);
    private class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
