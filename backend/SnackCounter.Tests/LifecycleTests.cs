using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SnackCounter.Core;

namespace SnackCounter.Tests;

// Every operation uses a separate connection/context against a real temporary SQLite file.
public class LifecycleTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"snacks-{Guid.NewGuid()}.db");
    private readonly ManualClock clock = new();
    private readonly RecordingChat chat = new();
    private readonly InvitationOptions policy = new();
    private SnackDb Db() => new(new DbContextOptionsBuilder<SnackDb>()
        .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=30").Options);
    private async Task<T> Run<T>(Func<RefillService, Task<T>> operation)
    {
        await using var db = Db();
        return await operation(new RefillService(db, clock, chat, policy));
    }
    private async Task<int> Setup()
    {
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        var id = (await Run(s => s.Report(1, 1))).Id;
        await Run(s => s.Invite(id));
        return id;
    }
    private async Task<RefillState> State(int id)
    {
        await using var db = Db();
        return (await db.RefillRequests.SingleAsync(r => r.Id == id)).State;
    }
    [Fact]
    public async Task SimultaneousVolunteersHaveExactlyOneWinner()
    {
        var id = await Setup();
        using var barrier = new Barrier(2);
        Task<bool> Accept(int employee) => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Run(s => s.Accept(id, employee));
        });
        var results = await Task.WhenAll(Accept(1), Accept(2));
        Assert.Single(results, x => x);
        await using var db = Db();
        Assert.Equal(1, await db.RefillAssignments.CountAsync(a => a.State == AttemptState.Reserved));
        Assert.Equal(RefillState.Reserved, await State(id));
    }
    [Fact]
    public async Task ExpiredReservationCanBeTakenByAnotherVolunteer()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.True(await Run(s => s.Accept(id, 2)));
        await using var db = Db();
        Assert.Equal(AttemptState.Expired, (await db.RefillAssignments.SingleAsync(a => a.EmployeeId == 1)).State);
    }
    [Fact]
    public async Task PurchasingNeverBecomesAvailableAutomatically()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        Assert.True(await Run(s => s.Transition(id, 1, "purchase", 100, "NOK")));
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.False(await Run(s => s.Accept(id, 2)));
        Assert.Equal(RefillState.Purchasing, await State(id));
        clock.Advance(TimeSpan.FromDays(2));
        await Run(async s => { await s.Sweep(); return true; });
        Assert.Equal(RefillState.NeedsReview, await State(id));
        Assert.Empty(await Run(s => s.Invite(id)));
        Assert.False(await Run(s => s.Transition(id, 1, "release")));
        Assert.True(await Run(s => s.Transition(id, 1, "complete", 100, "NOK")));
    }
    [Fact]
    public async Task DeclineDoesNotChangeEligibilityOrCountAsContribution()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Decline(id, 1)));
        Assert.True(await Run(s => s.Accept(id, 2)));
        Assert.True(await Run(s => s.Transition(id, 2, "unnecessary")));
        var next = (await Run(s => s.Report(1, 3))).Id;
        var invited = await Run(s => s.Invite(next, 3));
        Assert.Contains(1, invited);
        Assert.Equal(2, invited.Last()); // Only the completed contribution lowers priority.
        await using var db = Db();
        Assert.True((await db.Employees.FindAsync(1))!.Volunteers);
        Assert.Equal(0, await db.RefillAssignments.CountAsync(a => a.EmployeeId == 1 && a.State == AttemptState.Completed));
    }
    [Fact]
    public async Task SimultaneousReportsShareOneActiveOpportunity()
    {
        await Setup();
        // Use the other office so both concurrent calls try to create a fresh opportunity.
        using var barrier = new Barrier(2);
        Task<RefillRequest> Report(int employee) => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Run(s => s.Report(2, employee));
        });
        var requests = await Task.WhenAll(Report(4), Report(5));
        Assert.Equal(requests[0].Id, requests[1].Id);
        await using var db = Db();
        Assert.Equal(1, await db.RefillRequests.CountAsync(r => r.OfficeId == 2));
    }
    [Fact]
    public async Task CompletedTaskAllowsNewRequest()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        Assert.True(await Run(s => s.Transition(id, 1, "unnecessary")));
        var next = await Run(s => s.Report(1, 2));
        Assert.NotEqual(id, next.Id);
        Assert.Equal(RefillState.Open, next.State);
    }
    [Fact]
    public async Task ReleaseAllowsAnotherVolunteerButOnlyOwnerCanTransition()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        Assert.False(await Run(s => s.Transition(id, 2, "purchase", 100, "NOK")));
        Assert.True(await Run(s => s.Transition(id, 1, "release")));
        Assert.True(await Run(s => s.Accept(id, 2)));
    }
    [Fact]
    public async Task IgnoredInvitationDoesNotBlockOthersOrChangeEligibility()
    {
        var id = await Setup();
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(await Run(s => s.Accept(id, 1)));
        Assert.Equal(new[] { 3 }, await Run(s => s.Invite(id)));
        Assert.True(await Run(s => s.Accept(id, 3)));
        await using var db = Db();
        Assert.Equal(2, await db.RefillAssignments.CountAsync(a => a.State == AttemptState.Ignored));
        Assert.True(await db.Employees.AllAsync(e => e.Volunteers));
        Assert.Equal(3, chat.Invited.Count);
    }
    [Fact]
    public async Task NonVolunteerAndOtherOfficeCannotAccept()
    {
        var id = await Setup();
        Assert.False(await Run(s => s.Accept(id, 4)));
        await using (var db = Db())
            await db.Employees.Where(e => e.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(e => e.Volunteers, false));
        Assert.False(await Run(s => s.Accept(id, 1)));
    }
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task PurchaseDeadlineIsStrictEvenWhenSweepSawUnexpiredReservation(int offsetTicks, bool expected)
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        clock.Advance(TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(2));
        // Sweep and initial transaction read see a live reservation. Move the clock
        // only at the final SQL predicate to exercise the guard independently of Sweep.
        var reads = 0;
        clock.BeforeRead = () =>
        {
            if (++reads == 3) clock.Advance(TimeSpan.FromTicks(2 + offsetTicks));
        };
        Assert.Equal(expected, await Run(s => s.Transition(id, 1, "purchase", 100, "NOK")));
        clock.BeforeRead = null;
        await Run(async s => { await s.Sweep(); return true; });
        Assert.Equal(expected ? RefillState.Purchasing : RefillState.Open, await State(id));
        await using var db = Db();
        var attempt = await db.RefillAssignments.SingleAsync(a => a.EmployeeId == 1);
        Assert.Equal(expected ? AttemptState.Purchasing : AttemptState.Expired, attempt.State);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ConcurrentSweepAndPurchaseKeepRequestAndAssignmentConsistent(int offsetTicks)
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(offsetTicks));
        using var barrier = new Barrier(2);
        var purchase = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Run(s => s.Transition(id, 1, "purchase", 100, "NOK"));
        });
        var sweep = Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            return await Run(async s => { await s.Sweep(); return true; });
        });
        await Task.WhenAll(purchase, sweep);
        Assert.Equal(offsetTicks < 0, await purchase);
        Assert.Equal(offsetTicks < 0 ? RefillState.Purchasing : RefillState.Open, await State(id));
        await using var db = Db();
        Assert.Equal(offsetTicks < 0 ? AttemptState.Purchasing : AttemptState.Expired,
            (await db.RefillAssignments.SingleAsync(a => a.EmployeeId == 1)).State);
        Assert.Equal(1, await db.RefillRequests.CountAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VolunteerCanOfferAfterDecliningOrIgnoringWithHistoryPreserved(bool ignore)
    {
        var id = await Setup();
        if (ignore) clock.Advance(TimeSpan.FromMinutes(31));
        else Assert.True(await Run(s => s.Decline(id, 1)));
        var notifications = chat.Invited.Count;
        Assert.True(await Run(s => s.OfferHelp(id, 1)));
        Assert.False(await Run(s => s.OfferHelp(id, 1)));
        Assert.False(await Run(s => s.OfferHelp(id, 2)));
        Assert.Equal(notifications, chat.Invited.Count);
        await using var db = Db();
        var history = await db.RefillAssignments.Where(a => a.EmployeeId == 1).OrderBy(a => a.Id).ToArrayAsync();
        Assert.Equal(2, history.Length);
        Assert.Equal(ignore ? AttemptState.Ignored : AttemptState.Declined, history[0].State);
        Assert.NotNull(history[0].EndedAt);
        Assert.Equal(AttemptState.Reserved, history[1].State);
    }
    [Fact]
    public async Task DeclineAutomaticallyInvitesReplacementWithoutAssigningResponsibility()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Decline(id, 1)));
        Assert.Equal(new[] { 1, 2, 3 }, chat.Invited);
        Assert.Equal(RefillState.Open, await State(id));
        await using var db = Db();
        Assert.Equal(2, await db.RefillAssignments.CountAsync(a => a.State == AttemptState.Invited));
        Assert.Equal(0, await db.RefillAssignments.CountAsync(a => a.State == AttemptState.Reserved));
    }
    [Fact]
    public async Task ExpiryAutomaticallyInvitesReplacementAndRetriesAreIdempotent()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Accept(id, 1)));
        clock.Advance(TimeSpan.FromMinutes(16));
        await Run(async s => { await s.Sweep(); return true; });
        Assert.Contains(3, chat.Invited);
        await Run(async s => { await s.SweepAndOffer(); return true; });
        Assert.Equal(3, chat.Invited.Count);
        Assert.Equal(RefillState.Open, await State(id));
        Assert.True(await Run(s => s.Accept(id, 3)));
    }
    [Fact]
    public async Task CooldownAndTotalAttemptCapBoundNotificationsAcrossWorkerRetries()
    {
        policy.NotificationCooldown = TimeSpan.FromMinutes(60);
        policy.MaxInvitationAttempts = 4;
        var id = await Setup();
        clock.Advance(TimeSpan.FromMinutes(31));
        await Run(async s => { await s.SweepAndOffer(); return true; });
        Assert.Equal(new[] { 1, 2, 3 }, chat.Invited);
        await Run(async s => { await s.SweepAndOffer(); return true; });
        Assert.Equal(3, chat.Invited.Count);
        clock.Advance(TimeSpan.FromMinutes(30));
        await Run(async s => { await s.SweepAndOffer(); return true; });
        Assert.Equal(4, chat.Invited.Count);
        Assert.Equal(1, chat.Invited.Last());
        clock.Advance(TimeSpan.FromDays(1));
        await Run(async s => { await s.SweepAndOffer(); return true; });
        Assert.Equal(4, chat.Invited.Count);
        Assert.True(await Run(s => s.OfferHelp(id, 2))); // Notification cap never prevents consent.
    }
    [Fact]
    public async Task ConcurrentInvitationRetriesDoNotDuplicateActiveInvitations()
    {
        var id = await Setup();
        clock.Advance(TimeSpan.FromMinutes(61));
        using var barrier = new Barrier(2);
        Task Retry() => Task.Run(async () =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            await Run(async s => { await s.SweepAndOffer(); return true; });
        });
        await Task.WhenAll(Retry(), Retry());
        await using var db = Db();
        var active = await db.RefillAssignments.Where(a => a.State == AttemptState.Invited).ToArrayAsync();
        Assert.Equal(2, active.Length);
        Assert.Equal(2, active.Select(a => a.EmployeeId).Distinct().Count());
        Assert.Equal(4, chat.Invited.Count);
        Assert.Equal(1, await db.RefillRequests.CountAsync());
    }
    [Fact]
    public async Task ExistingDatabaseIndexUpgradePreservesHistoryAndAllowsReturn()
    {
        var id = await Setup();
        Assert.True(await Run(s => s.Decline(id, 1)));
        await using (var db = Db())
        {
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_RefillAssignments_RefillRequestId_EmployeeId");
            await db.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_RefillAssignments_RefillRequestId_EmployeeId ON RefillAssignments (RefillRequestId, EmployeeId)");
            await db.InitializeAsync();
            await db.InitializeAsync();
        }
        Assert.True(await Run(s => s.OfferHelp(id, 1)));
        await using var verify = Db();
        Assert.Equal(2, await verify.RefillAssignments.CountAsync(a => a.EmployeeId == 1));
        Assert.Equal(6, await verify.Employees.CountAsync());
    }
    public void Dispose()
    {
        File.Delete(path);
    }
    private class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Action? BeforeRead { get; set; }
        public override DateTimeOffset GetUtcNow() { BeforeRead?.Invoke(); return now; }
        public void Advance(TimeSpan amount) => now += amount;
    }
    private class RecordingChat : IChat
    {
        public System.Collections.Concurrent.ConcurrentQueue<int> Invited { get; } = new();
        public Task Invite(int employeeId, int requestId) { Invited.Enqueue(employeeId); return Task.CompletedTask; }
    }
}
