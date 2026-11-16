using Microsoft.EntityFrameworkCore;
using SnackCounter.Core;

public static class Phase2BEndpoints
{
    public static void MapPhase2B(this WebApplication app)
    {
        // DEMO ONLY: supplied identities are trusted. Production must authorize employee
        // claim access and restrict /demo/hr operations, contribution data and receipts to HR.
        app.MapGet("/demo/employees/{employeeId:int}/reimbursements", async (int employeeId, ReimbursementService service) =>
            Results.Ok(await service.ForEmployee(employeeId)));
        app.MapPost("/purchases/{purchaseId:int}/reimbursement", async (int purchaseId, ClaimDraft body, ReimbursementService service) =>
        {
            try
            {
                var claim = await service.Draft(purchaseId, body.EmployeeId, body.Description, body.ReceiptReference);
                return claim == null ? Results.Conflict(new { error = "Completed purchase owned by this employee required." }) : Results.Ok(claim);
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        app.MapGet("/reimbursements/{id:int}", async (int id, int employeeId, ReimbursementService service) =>
            await service.Get(id, employeeId) is { } claim ? Results.Ok(claim) : Results.NotFound());
        app.MapPost("/reimbursements/{id:int}/submit", async (int id, ClaimSubmit body, ReimbursementService service) =>
        {
            try { return await service.Submit(id, body.EmployeeId, body.DemoMode) is { } claim ? Results.Ok(claim) : Results.NotFound(); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        app.MapPost("/reimbursements/{id:int}/reconcile", async (int id, Identity body, ReimbursementService service) =>
            await service.Reconcile(id, body.EmployeeId) is { } claim ? Results.Ok(claim) : Results.NotFound());
        app.MapPost("/demo/hr/reimbursements/{id:int}/decision", async (int id, HrDecision body, SnackDb db, SimulatedHr hr, ReimbursementService service) =>
        {
            var claim = await db.ReimbursementClaims.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
            if (claim == null) return Results.NotFound();
            if (!await hr.Decide(claim.IdempotencyKey, body.Decision))
                return Results.Conflict(new { error = "Decision unavailable. Approve/reject after one simulated day; payment requires approval." });
            return Results.Ok(await service.Reconcile(id, claim.EmployeeId));
        });
        app.MapGet("/reports/monthly/{month}", async (string month, ReportingService service) =>
        {
            try { return Results.Ok(await service.Native(month)); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        app.MapGet("/reports/monthly/{month}/consolidated", async (string month, ReportingService service) =>
        {
            try { return Results.Ok(await service.Consolidated(month)); }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        });
        app.MapGet("/demo/hr/contributions", async (SnackDb db) =>
            await db.Contributions.AsNoTracking().OrderByDescending(c => c.EarnedAt).ThenBy(c => c.Id).ToListAsync());
        app.MapGet("/demo/hr/contributions/leaderboard", async (SnackDb db) => await Contributions.Leaderboard(db));
        app.MapGet("/demo/employees/{employeeId:int}/purchases", async (int employeeId, SnackDb db) =>
            await db.PurchaseCommitments.AsNoTracking().Where(p => db.RefillAssignments.Any(a => a.Id == p.RefillAssignmentId && a.EmployeeId == employeeId))
                .OrderByDescending(p => p.Id).ToListAsync());
    }
}
public record ClaimDraft(int EmployeeId, string Description, string ReceiptReference);
public record ClaimSubmit(int EmployeeId, SubmissionMode DemoMode = SubmissionMode.Success);
public record HrDecision(ClaimState Decision);
