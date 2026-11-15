using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

public interface IChat { Task Invite(int employeeId, int requestId); }
public class SimulatedChat : IChat
{
    public Task Invite(int employeeId, int requestId)
    {
        Console.WriteLine($"[Simulated private chat to employee {employeeId}] Optional refill #{requestId}. Accept or decline; ignoring is fine. Please reserve before buying.");
        return Task.CompletedTask;
    }
}
public class InvitationOptions
{
    public TimeSpan NotificationCooldown { get; set; } = TimeSpan.FromHours(1);
    public int MaxInvitationAttempts { get; set; } = 6; // Total notifications per refill, across all volunteers.
}
public class RefillService(SnackDb db, TimeProvider clock, IChat chat, InvitationOptions? options = null, BudgetOptions? budgets = null)
{
    private readonly InvitationOptions policy = options ?? new();
    private long Now => clock.GetUtcNow().UtcTicks;
    private static long Minutes(int n) => TimeSpan.FromMinutes(n).Ticks;
    public async Task<RefillRequest> Report(int officeId, int employeeId)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId && e.OfficeId == officeId))
            throw new ArgumentException("Reporter must belong to the office.");
        var request = new RefillRequest { OfficeId = officeId, ReportedById = employeeId, CreatedAt = Now };
        db.Add(request);
        try { await db.SaveChangesAsync(); return request; }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 })
        {
            db.Entry(request).State = EntityState.Detached;
            return await db.RefillRequests.AsNoTracking().SingleAsync(r => r.OfficeId == officeId && r.State != RefillState.Completed);
        }
    }
    public async Task<int[]> Invite(int requestId, int count = 2)
    {
        await Sweep();
        return await SendInvitations(requestId, count);
    }
    private async Task<int[]> SendInvitations(int requestId, int count = 2)
    {
        using var tx = await db.Database.BeginTransactionAsync();
        var request = await db.RefillRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId);
        if (request?.State != RefillState.Open) return [];
        var now = Now;
        var history = db.RefillAssignments.Where(a => a.RefillRequestId == requestId);
        var remaining = Math.Max(0, policy.MaxInvitationAttempts - await history.CountAsync(a => a.InvitationExpiresAt > a.InvitedAt));
        var slots = Math.Min(remaining, Math.Clamp(count, 1, 10) - await history.CountAsync(a => a.State == AttemptState.Invited));
        if (slots <= 0) return [];
        var cutoff = now - policy.NotificationCooldown.Ticks;
        // Completed contributions determine fairness; declines and silence never add a penalty.
        // Oldest invitation breaks ties, rotating invitations among equally contributing volunteers.
        var ids = await db.Employees.Where(e => e.OfficeId == request.OfficeId && e.Volunteers &&
                !history.Any(a => a.EmployeeId == e.Id && (a.State == AttemptState.Invited ||
                    a.State == AttemptState.Reserved || a.State == AttemptState.Purchasing || a.InvitedAt > cutoff)))
            .OrderBy(e => db.RefillAssignments.Count(a => a.EmployeeId == e.Id && a.State == AttemptState.Completed))
            .ThenBy(e => db.RefillAssignments.Where(a => a.EmployeeId == e.Id).Max(a => (long?)a.InvitedAt) ?? 0)
            .ThenBy(e => e.Id).Take(slots).Select(e => e.Id).ToArrayAsync();
        db.AddRange(ids.Select(id => new RefillAssignment { EmployeeId = id, RefillRequestId = requestId,
            InvitedAt = now, InvitationExpiresAt = now + Minutes(30) }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        foreach (var id in ids) await chat.Invite(id, requestId);
        return ids;
    }
    public async Task<bool> Accept(int requestId, int employeeId)
        => await Reserve(requestId, employeeId, false);
    // Explicit consent to help again; does not send a notification or consume its retry budget.
    public async Task<bool> OfferHelp(int requestId, int employeeId)
        => await Reserve(requestId, employeeId, true);
    private async Task<bool> Reserve(int requestId, int employeeId, bool offering)
    {
        await Sweep();
        using var tx = await db.Database.BeginTransactionAsync();
        var now = Now;
        var invitation = db.RefillAssignments.Where(a => a.RefillRequestId == requestId && a.EmployeeId == employeeId &&
            a.State == AttemptState.Invited && a.InvitationExpiresAt > now &&
            db.Employees.Any(e => e.Id == employeeId && e.Volunteers));
        var hasInvitation = await invitation.AnyAsync();
        if (!hasInvitation && (!offering || !await db.Employees.AnyAsync(e => e.Id == employeeId && e.Volunteers &&
            db.RefillRequests.Any(r => r.Id == requestId && r.OfficeId == e.OfficeId)))) return false;
        if (await db.RefillRequests.Where(r => r.Id == requestId && r.State == RefillState.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RefillState.Reserved)) != 1) return false;
        if (!hasInvitation)
        {
            db.Add(new RefillAssignment { RefillRequestId = requestId, EmployeeId = employeeId,
                State = AttemptState.Reserved, InvitedAt = now, InvitationExpiresAt = now,
                RespondedAt = now, ReservedAt = now, ReservationExpiresAt = now + Minutes(15) });
            await db.SaveChangesAsync();
        }
        else if (await invitation.ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AttemptState.Reserved)
            .SetProperty(a => a.RespondedAt, now).SetProperty(a => a.ReservedAt, now)
            .SetProperty(a => a.ReservationExpiresAt, now + Minutes(15))) != 1) return false;
        await tx.CommitAsync();
        return true;
    }
    public async Task<bool> Decline(int requestId, int employeeId)
    {
        var now = Now;
        var declined = await db.RefillAssignments.Where(a => a.RefillRequestId == requestId && a.EmployeeId == employeeId &&
            a.State == AttemptState.Invited && a.InvitationExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AttemptState.Declined)
                .SetProperty(a => a.RespondedAt, now).SetProperty(a => a.EndedAt, now)) == 1;
        if (declined) await Invite(requestId);
        return declined;
    }
    public async Task<bool> Transition(int requestId, int employeeId, string action, long? amountMinor = null, string? currency = null)
    {
        await Sweep();
        // All entry points use the same financial transaction; no legacy bypass.
        var financial = new BudgetService(db, clock, budgets ?? new());
        if (action == "purchase") return amountMinor.HasValue && currency != null &&
            (await financial.Authorize(requestId, employeeId, amountMinor.Value, currency)).Success;
        if (action is "complete" or "cancel-purchase") return amountMinor.HasValue && currency != null &&
            (await financial.Resolve(requestId, employeeId, amountMinor.Value, currency, action == "cancel-purchase")).Success;
        using var tx = await db.Database.BeginTransactionAsync();
        var now = Now;
        var request = await db.RefillRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId);
        if (request?.State != RefillState.Reserved || action is not ("release" or "unnecessary")) return false;
        var attempts = db.RefillAssignments.Where(a => a.RefillRequestId == requestId && a.EmployeeId == employeeId && a.State == AttemptState.Reserved);
        var next = action == "release" ? RefillState.Open : RefillState.Completed;
        var attemptNext = action == "release" ? AttemptState.Released : AttemptState.Completed;
        if (!await attempts.AnyAsync()) return false;
        now = Now;
        if (await db.RefillRequests.Where(r => r.Id == requestId && r.State == request.State)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, next)
                .SetProperty(r => r.CompletedAt, next == RefillState.Completed ? (long?)now : null)
                .SetProperty(r => r.Outcome, next == RefillState.Completed ? action : null)) != 1) return false;
        await attempts.ExecuteUpdateAsync(s => s.SetProperty(a => a.State, attemptNext)
            .SetProperty(a => a.EndedAt, now));
        await tx.CommitAsync();
        return true;
    }
    public async Task Sweep()
    {
        using var tx = await db.Database.BeginTransactionAsync();
        var now = Now;
        var expired = db.RefillAssignments.Where(a => a.State == AttemptState.Reserved && a.ReservationExpiresAt <= now);
        var reopened = await expired.Select(a => a.RefillRequestId).ToArrayAsync();
        await db.RefillRequests.Where(r => r.State == RefillState.Reserved && expired.Any(a => a.RefillRequestId == r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RefillState.Open));
        await expired.ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AttemptState.Expired).SetProperty(a => a.EndedAt, now));
        await db.RefillAssignments.Where(a => a.State == AttemptState.Invited && a.InvitationExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, AttemptState.Ignored).SetProperty(a => a.EndedAt, now));
        await db.RefillRequests.Where(r => r.State == RefillState.Purchasing && db.RefillAssignments.Any(a =>
            a.RefillRequestId == r.Id && a.State == AttemptState.Purchasing && a.PurchasingAt <= now - Minutes(24 * 60)))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RefillState.NeedsReview));
        await tx.CommitAsync();
        foreach (var id in reopened) await SendInvitations(id);
    }
    public async Task SweepAndOffer()
    {
        await Sweep();
        var open = await db.RefillRequests.Where(r => r.State == RefillState.Open).Select(r => r.Id).ToArrayAsync();
        foreach (var id in open) await SendInvitations(id);
    }
}
