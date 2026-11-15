using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SnackCounter.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<SnackDb>(o => o.UseSqlite(builder.Configuration.GetConnectionString("SnackCounter")
    ?? "Data Source=snackcounter.db;Default Timeout=30"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IChat, SimulatedChat>();
var invitations = builder.Configuration.GetSection("Invitations").Get<InvitationOptions>() ?? new();
if (invitations.NotificationCooldown <= TimeSpan.Zero || invitations.MaxInvitationAttempts < 1)
    throw new InvalidOperationException("Invitations requires a positive cooldown and maximum attempts.");
builder.Services.AddSingleton(invitations);
var budgets = builder.Configuration.GetSection("Budgets").Get<BudgetOptions>() ?? new();
foreach (var policy in budgets.Offices.Values)
{
    if (policy.Currency is not ("NOK" or "INR") || policy.MonthlyLimitMinor <= 0 || policy.MonthlyLimitMinor > BudgetService.MaxMoneyMinor)
        throw new InvalidOperationException("Invalid office budget policy.");
    TimeZoneInfo.FindSystemTimeZoneById(policy.TimeZoneId);
}
builder.Services.AddSingleton(budgets);
builder.Services.AddScoped<BudgetService>();
builder.Services.AddScoped<RefillService>();
var reporting = builder.Configuration.GetSection("Reporting").Get<ReportingOptions>() ?? new();
if (reporting.InrToNokNumerator <= 0 || reporting.InrToNokNumerator > 1_000_000 || reporting.InrToNokDenominator <= 0 || reporting.InrToNokDenominator > 1_000_000)
    throw new InvalidOperationException("Simulated exchange rate components must be between 1 and 1000000.");
builder.Services.AddSingleton(reporting);
builder.Services.AddScoped<SimulatedHr>();
builder.Services.AddScoped<IHrAdapter>(s => s.GetRequiredService<SimulatedHr>());
builder.Services.AddScoped<ReimbursementService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddHostedService<ExpirationWorker>();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<SnackDb>().InitializeAsync();

// DEMO ONLY: employee IDs in bodies/URLs are trusted. No authentication or authorization.
// Public request projections deliberately omit reporter and assignee identities.
app.MapGet("/", () => new { application = "Snack Counter", warning = "Demo: identities are simulated and trusted; no authentication.",
    endpoints = new[] { "GET /offices", "GET /offices/{officeId}/budget", "GET /demo/employees", "GET /refills", "POST /offices/{officeId}/refills",
        "POST /refills/{id}/invite", "GET /demo/employees/{employeeId}/invitations", "POST /refills/{id}/{action}",
        "GET /demo/employees/{employeeId}/purchases", "POST /purchases/{purchaseId}/reimbursement",
        "GET /reimbursements/{id}?employeeId=...", "POST /reimbursements/{id}/submit", "POST /reimbursements/{id}/reconcile",
        "POST /demo/hr/reimbursements/{id}/decision", "GET /reports/monthly/{month}", "GET /reports/monthly/{month}/consolidated",
        "GET /demo/hr/contributions", "GET /demo/hr/contributions/leaderboard" },
    actions = new[] { "accept", "offer", "decline", "release", "unnecessary", "purchase", "complete", "cancel-purchase" } });
app.MapGet("/offices/{officeId:int}/budget", async (int officeId, BudgetService service) =>
    await service.GetBalance(officeId) is { } balance ? Results.Ok(balance) : Results.NotFound());
app.MapGet("/offices", async (SnackDb db) => await db.Offices.AsNoTracking().ToListAsync());
app.MapGet("/demo/employees", async (SnackDb db) => await db.Employees.AsNoTracking().ToListAsync());
app.MapGet("/refills", async (SnackDb db, RefillService service) =>
{
    await service.Sweep();
    return await db.RefillRequests.AsNoTracking().OrderByDescending(r => r.Id)
        .Select(r => new { r.Id, r.OfficeId, r.State, r.CreatedAt, r.CompletedAt, r.Outcome,
            BeingHandled = r.State == RefillState.Reserved || r.State == RefillState.Purchasing || r.State == RefillState.NeedsReview }).ToListAsync();
});
app.MapPost("/offices/{officeId:int}/refills", async (int officeId, Identity body, RefillService service) =>
{
    try
    {
        var r = await service.Report(officeId, body.EmployeeId);
        return Results.Ok(new { r.Id, r.OfficeId, r.State, r.CreatedAt });
    }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
});
app.MapPost("/refills/{id:int}/invite", async (int id, RefillService service) =>
{
    var ids = await service.Invite(id);
    return Results.Ok(new { InvitedCount = ids.Length });
});
app.MapGet("/demo/employees/{employeeId:int}/invitations", async (int employeeId, SnackDb db, RefillService service) =>
{
    await service.Sweep();
    return await db.RefillAssignments.AsNoTracking().Where(a => a.EmployeeId == employeeId)
        .Select(a => new { a.RefillRequestId, a.State, a.InvitedAt, a.InvitationExpiresAt, a.ReservationExpiresAt }).ToListAsync();
});
app.MapPost("/refills/{id:int}/{action}", async (int id, string action, Identity body, RefillService service, BudgetService financial) =>
{
    if (action is "purchase" or "complete" or "cancel-purchase")
    {
        var amount = action == "purchase" ? body.EstimatedAmountMinor : body.ActualAmountMinor;
        if (amount == null || body.Currency == null) return Results.BadRequest(new { error = "Amount in minor units and currency are required." });
        var result = action == "purchase"
            ? await financial.Authorize(id, body.EmployeeId, amount.Value, body.Currency)
            : await financial.Resolve(id, body.EmployeeId, amount.Value, body.Currency, action == "cancel-purchase");
        return result.Success ? Results.Ok(result) : Results.Conflict(result);
    }
    if (action is not ("accept" or "offer" or "decline" or "release" or "unnecessary"))
        return Results.BadRequest(new { error = "Unknown action." });
    var ok = action switch
    {
        "accept" => await service.Accept(id, body.EmployeeId),
        "offer" => await service.OfferHelp(id, body.EmployeeId),
        "decline" => await service.Decline(id, body.EmployeeId),
        _ => await service.Transition(id, body.EmployeeId, action)
    };
    return ok ? Results.NoContent() : Results.Conflict(new { error = "Action unavailable, invitation expired, or employee is not the reservation owner." });
});
app.MapPhase2B();
app.Run();

public record Identity(int EmployeeId, long? EstimatedAmountMinor = null, long? ActualAmountMinor = null, string? Currency = null);
public class ExpirationWorker(IServiceScopeFactory scopes, ILogger<ExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<RefillService>().SweepAndOffer();
                }
                catch (Exception e) { logger.LogError(e, "Refill expiration sweep failed; retrying next interval."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
