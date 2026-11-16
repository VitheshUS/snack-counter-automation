using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

public enum ClaimState { Draft, SubmissionPending, PendingHR, SubmissionFailed, SubmissionUncertain, Approved, Rejected, Reimbursed }
public enum SubmissionMode { Success, DefiniteFailure, TimeoutAfterCreation, TimeoutBeforeCreation }
public class ReimbursementClaim
{
    public int Id { get; set; }
    public int PurchaseCommitmentId { get; set; }
    public int EmployeeId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "";
    public string Description { get; set; } = "";
    public string ReceiptReference { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public ClaimState State { get; set; }
    public bool RequiresHrReview { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}
public class MockHrClaim
{
    public string IdempotencyKey { get; set; } = "";
    public int EmployeeId { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "";
    public string Description { get; set; } = "";
    public string ReceiptReference { get; set; } = "";
    public bool RequiresHrReview { get; set; }
    public ClaimState State { get; set; }
    public long SubmittedAt { get; set; }
    public long DecisionAvailableAt { get; set; }
}
public record HrSubmission(ClaimState State);
public interface IHrAdapter
{
    Task<HrSubmission> Submit(ReimbursementClaim claim, SubmissionMode mode);
    Task<MockHrClaim?> Lookup(string idempotencyKey);
}
// Persistent mock provider. Real HR must support an idempotency key or reliable claim
// lookup to recover safely from timeouts. Never infer payment from approval.
public class SimulatedHr(SnackDb db, TimeProvider clock) : IHrAdapter
{
    public Task<MockHrClaim?> Lookup(string key) => db.MockHrClaims.AsNoTracking().SingleOrDefaultAsync(h => h.IdempotencyKey == key);
    public async Task<HrSubmission> Submit(ReimbursementClaim claim, SubmissionMode mode)
    {
        using var tx = await db.Database.BeginTransactionAsync();
        if (await Lookup(claim.IdempotencyKey) != null) return new(ClaimState.PendingHR);
        if (mode == SubmissionMode.DefiniteFailure) return new(ClaimState.SubmissionFailed);
        if (mode == SubmissionMode.TimeoutBeforeCreation) return new(ClaimState.SubmissionUncertain);
        var now = clock.GetUtcNow().UtcTicks;
        db.MockHrClaims.Add(new() { IdempotencyKey = claim.IdempotencyKey, EmployeeId = claim.EmployeeId,
            AmountMinor = claim.AmountMinor, Currency = claim.Currency, Description = claim.Description,
            ReceiptReference = claim.ReceiptReference, RequiresHrReview = claim.RequiresHrReview,
            State = ClaimState.PendingHR, SubmittedAt = now, DecisionAvailableAt = now + TimeSpan.FromDays(1).Ticks });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(mode == SubmissionMode.TimeoutAfterCreation ? ClaimState.SubmissionUncertain : ClaimState.PendingHR);
    }
    public async Task<bool> Decide(string key, ClaimState decision)
    {
        var now = clock.GetUtcNow().UtcTicks;
        if (decision == ClaimState.Reimbursed)
            return await db.MockHrClaims.Where(h => h.IdempotencyKey == key && h.State == ClaimState.Approved)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.State, decision)) == 1;
        if (decision is not (ClaimState.Approved or ClaimState.Rejected)) return false;
        return await db.MockHrClaims.Where(h => h.IdempotencyKey == key && h.State == ClaimState.PendingHR && h.DecisionAvailableAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.State, decision)) == 1;
    }
}
public class ReimbursementService(SnackDb db, TimeProvider clock, IHrAdapter hr)
{
    // Demo identity filtering only, not authentication. Read-only discovery must
    // neither create drafts nor call HR. Check both claim and purchase ownership.
    public Task<List<ReimbursementClaim>> ForEmployee(int employeeId) => db.ReimbursementClaims.AsNoTracking()
        .Where(c => c.EmployeeId == employeeId && db.PurchaseCommitments.Any(p =>
            p.Id == c.PurchaseCommitmentId && !p.Cancelled && p.ActualMinor > 0 &&
            db.RefillAssignments.Any(a => a.Id == p.RefillAssignmentId && a.EmployeeId == employeeId && a.State == AttemptState.Completed)))
        .OrderByDescending(c => c.Id).ToListAsync();
    public Task<ReimbursementClaim?> Get(int id, int employeeId) => db.ReimbursementClaims.AsNoTracking()
        .SingleOrDefaultAsync(c => c.Id == id && c.EmployeeId == employeeId);
    public async Task<ReimbursementClaim?> Draft(int purchaseId, int employeeId, string description, string receipt)
    {
        // References are opaque strings, never fetched or interpreted as local filenames.
        if (string.IsNullOrWhiteSpace(description) || description.Length > 500 || string.IsNullOrWhiteSpace(receipt) || receipt.Length > 500)
            throw new ArgumentException("Description and private receipt reference are required (maximum 500 characters each).");
        using var tx = await db.Database.BeginTransactionAsync();
        var purchase = await db.PurchaseCommitments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == purchaseId && !p.Cancelled && p.ActualMinor > 0 &&
            db.RefillAssignments.Any(a => a.Id == p.RefillAssignmentId && a.EmployeeId == employeeId && a.State == AttemptState.Completed));
        if (purchase == null) return null;
        var existing = await db.ReimbursementClaims.AsNoTracking().SingleOrDefaultAsync(c => c.PurchaseCommitmentId == purchaseId);
        if (existing != null) return existing;
        var month = await db.MonthlyBudgets.AsNoTracking().SingleAsync(b => b.OfficeId == purchase.OfficeId && b.Month == purchase.Month);
        var now = clock.GetUtcNow().UtcTicks;
        var claim = new ReimbursementClaim { PurchaseCommitmentId = purchase.Id, EmployeeId = employeeId,
            AmountMinor = purchase.ActualMinor!.Value, Currency = month.Currency, Description = description.Trim(),
            ReceiptReference = receipt.Trim(), IdempotencyKey = Guid.NewGuid().ToString("N"),
            RequiresHrReview = purchase.RequiresHrReview, CreatedAt = now, UpdatedAt = now };
        db.Add(claim);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return claim;
    }
    public async Task<ReimbursementClaim?> Submit(int id, int employeeId, SubmissionMode mode = SubmissionMode.Success)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Invalid simulation mode.");
        var now = clock.GetUtcNow().UtcTicks;
        // Persist intent BEFORE contacting HR; a crash leaves a recoverable Pending state.
        var won = await db.ReimbursementClaims.Where(c => c.Id == id && c.EmployeeId == employeeId &&
                (c.State == ClaimState.Draft || c.State == ClaimState.SubmissionFailed))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.State, ClaimState.SubmissionPending).SetProperty(c => c.UpdatedAt, now));
        var claim = await Get(id, employeeId);
        if (won == 0 || claim == null) return claim; // Uncertain/Pending submissions require lookup, never blind resubmission.
        HrSubmission outcome;
        try { outcome = await hr.Submit(claim, mode); }
        catch { outcome = new(ClaimState.SubmissionUncertain); }
        await db.ReimbursementClaims.Where(c => c.Id == id && c.State == ClaimState.SubmissionPending)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.State, outcome.State).SetProperty(c => c.UpdatedAt, now));
        return await Get(id, employeeId);
    }
    public async Task<ReimbursementClaim?> Reconcile(int id, int employeeId)
    {
        // Serialize lookup with mock provider submissions/decisions. Production needs the
        // provider's equivalent lookup/idempotency guarantee, not this local transaction.
        using var tx = await db.Database.BeginTransactionAsync();
        var claim = await Get(id, employeeId);
        if (claim == null || claim.State is ClaimState.Draft or ClaimState.SubmissionFailed) return claim;
        var external = await hr.Lookup(claim.IdempotencyKey);
        var state = external?.State ?? ClaimState.SubmissionFailed;
        if (external == null && claim.State is not (ClaimState.SubmissionPending or ClaimState.SubmissionUncertain))
            return claim;
        var now = clock.GetUtcNow().UtcTicks;
        await db.ReimbursementClaims.Where(c => c.Id == id).ExecuteUpdateAsync(s =>
            s.SetProperty(c => c.State, state).SetProperty(c => c.UpdatedAt, now));
        await tx.CommitAsync();
        return await Get(id, employeeId);
    }
}
