using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

public class OfficeBudgetPolicy
{
    public string Currency { get; set; } = "NOK";
    public long MonthlyLimitMinor { get; set; } = 600_000;
    public string TimeZoneId { get; set; } = "Europe/Oslo";
}
public class BudgetOptions
{
    public Dictionary<int, OfficeBudgetPolicy> Offices { get; set; } = new()
    {
        [1] = new(),
        [2] = new() { Currency = "INR", MonthlyLimitMinor = 1_500_000, TimeZoneId = "Asia/Kolkata" }
    };
}
public class MonthlyBudget
{
    public int OfficeId { get; set; }
    public string Month { get; set; } = "";
    public string Currency { get; set; } = "";
    public long LimitMinor { get; set; }
}
public class PurchaseCommitment
{
    public int Id { get; set; }
    public int RefillAssignmentId { get; set; }
    public int OfficeId { get; set; }
    public string Month { get; set; } = "";
    public long EstimateMinor { get; set; }
    public long? ActualMinor { get; set; }
    public bool Cancelled { get; set; }
    public bool RequiresHrReview { get; set; }
    public long AuthorizedAt { get; set; }
    public long? ResolvedAt { get; set; }
}
public record BudgetResult(bool Success, string? Error = null, bool AllowanceWarning = false, bool RequiresHrReview = false);
public record BudgetBalance(int OfficeId, string Month, string Currency, long LimitMinor, long ReleasedMinor,
    long SpentMinor, long ReservedMinor)
{
    public long AvailableMinor => LimitMinor - SpentMinor - ReservedMinor;
}
public class BudgetService(SnackDb db, TimeProvider clock, BudgetOptions options)
{
    // Both supported currencies use 100 minor units per major unit. SQLite INTEGER
    // avoids decimal TEXT storage and unsupported decimal SUM/comparison operations.
    public const long MaxMoneyMinor = 1_000_000_000_000;
    private (string Month, int Period) Calendar(int officeId, long ticks)
    {
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(ticks, TimeSpan.Zero),
            TimeZoneInfo.FindSystemTimeZoneById(options.Offices[officeId].TimeZoneId));
        return (local.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Math.Min(4, (local.Day - 1) / 7 + 1));
    }
    private async Task<MonthlyBudget> Month(int officeId, string month)
    {
        var policy = options.Offices[officeId];
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO MonthlyBudgets (OfficeId, Month, Currency, LimitMinor) VALUES ({officeId}, {month}, {policy.Currency}, {policy.MonthlyLimitMinor})");
        return await db.MonthlyBudgets.AsNoTracking().SingleAsync(b => b.OfficeId == officeId && b.Month == month);
    }
    private async Task<BudgetBalance> Balance(MonthlyBudget month, int period)
    {
        var purchases = db.PurchaseCommitments.Where(p => p.OfficeId == month.OfficeId && p.Month == month.Month && !p.Cancelled);
        var spent = await purchases.SumAsync(p => p.ActualMinor ?? 0);
        var reserved = await purchases.Where(p => p.ActualMinor == null).SumAsync(p => p.EstimateMinor);
        return new(month.OfficeId, month.Month, month.Currency, month.LimitMinor,
            month.LimitMinor * period / 4, spent, reserved);
    }
    public async Task<BudgetBalance?> GetBalance(int officeId)
    {
        if (!options.Offices.ContainsKey(officeId) || !await db.Offices.AnyAsync(o => o.Id == officeId)) return null;
        using var tx = await db.Database.BeginTransactionAsync();
        var calendar = Calendar(officeId, clock.GetUtcNow().UtcTicks);
        var result = await Balance(await Month(officeId, calendar.Month), calendar.Period);
        await tx.CommitAsync();
        return result;
    }
    public async Task<BudgetResult> Authorize(int requestId, int employeeId, long estimateMinor, string currency)
    {
        if (estimateMinor <= 0 || estimateMinor > MaxMoneyMinor) return new(false, "Estimate must be positive and within the supported money range.");
        using var tx = await db.Database.BeginTransactionAsync(); // SQLite immediate write transaction serializes authorizations.
        var now = clock.GetUtcNow().UtcTicks;
        var request = await db.RefillRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId);
        if (request == null || !options.Offices.ContainsKey(request.OfficeId)) return new(false, "Unknown request or office policy.");
        var assignment = await db.RefillAssignments.AsNoTracking().SingleOrDefaultAsync(a =>
            a.RefillRequestId == requestId && a.EmployeeId == employeeId && a.State == AttemptState.Reserved);
        if (assignment == null) return new(false, "Active reservation required.");
        var calendar = Calendar(request.OfficeId, now);
        var month = await Month(request.OfficeId, calendar.Month);
        if (currency != month.Currency) return new(false, "Currency does not match the office budget.");
        var balance = await Balance(month, calendar.Period);
        if (estimateMinor > balance.AvailableMinor) return new(false, "Monthly budget exhausted.");
        now = clock.GetUtcNow().UtcTicks;
        // Recalculate the month if the clock crossed a boundary while authorizing.
        if (Calendar(request.OfficeId, now).Month != calendar.Month) return new(false, "Month changed; retry authorization.");
        if (await db.RefillRequests.Where(r => r.Id == requestId && r.State == RefillState.Reserved &&
                db.RefillAssignments.Any(a => a.Id == assignment.Id && a.State == AttemptState.Reserved && a.ReservationExpiresAt > now))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, RefillState.Purchasing)) != 1)
            return new(false, "Reservation expired or unavailable.");
        await db.RefillAssignments.Where(a => a.Id == assignment.Id).ExecuteUpdateAsync(s =>
            s.SetProperty(a => a.State, AttemptState.Purchasing).SetProperty(a => a.PurchasingAt, now));
        db.PurchaseCommitments.Add(new() { RefillAssignmentId = assignment.Id, OfficeId = request.OfficeId,
            Month = calendar.Month, EstimateMinor = estimateMinor, AuthorizedAt = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        var released = month.LimitMinor * Calendar(request.OfficeId, now).Period / 4;
        return new(true, AllowanceWarning: balance.SpentMinor + balance.ReservedMinor + estimateMinor > released);
    }
    public async Task<BudgetResult> Resolve(int requestId, int employeeId, long actualMinor, string currency, bool cancel = false)
    {
        if (actualMinor < 0 || actualMinor > MaxMoneyMinor || (cancel && actualMinor != 0)) return new(false, "Invalid actual amount.");
        using var tx = await db.Database.BeginTransactionAsync();
        var purchase = await db.PurchaseCommitments.SingleOrDefaultAsync(p => !p.Cancelled && p.ActualMinor == null &&
            db.RefillAssignments.Any(a => a.Id == p.RefillAssignmentId && a.RefillRequestId == requestId &&
                a.EmployeeId == employeeId && a.State == AttemptState.Purchasing));
        if (purchase == null) return new(false, "Outstanding purchase owned by this employee required.");
        var month = await db.MonthlyBudgets.AsNoTracking().SingleAsync(b => b.OfficeId == purchase.OfficeId && b.Month == purchase.Month);
        if (currency != month.Currency) return new(false, "Currency does not match the commitment.");
        var now = clock.GetUtcNow().UtcTicks;
        if (await db.RefillRequests.Where(r => r.Id == requestId && (r.State == RefillState.Purchasing || r.State == RefillState.NeedsReview))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.State, cancel ? RefillState.Open : RefillState.Completed)
                .SetProperty(r => r.CompletedAt, cancel ? null : (long?)now).SetProperty(r => r.Outcome, cancel ? null : "complete")) != 1)
            return new(false, "Task unavailable.");
        purchase.Cancelled = cancel;
        purchase.ActualMinor = cancel ? null : actualMinor;
        purchase.ResolvedAt = now;
        purchase.RequiresHrReview = !cancel && actualMinor > purchase.EstimateMinor;
        await db.RefillAssignments.Where(a => a.Id == purchase.RefillAssignmentId).ExecuteUpdateAsync(s =>
            s.SetProperty(a => a.State, cancel ? AttemptState.Released : AttemptState.Completed).SetProperty(a => a.EndedAt, now));
        await db.SaveChangesAsync();
        if (!cancel)
            await Contributions.Award(db, requestId, employeeId, actualMinor > 0 ? "Refill" : "Inspection", now);
        await tx.CommitAsync();
        return new(true, RequiresHrReview: purchase.RequiresHrReview);
    }
}
