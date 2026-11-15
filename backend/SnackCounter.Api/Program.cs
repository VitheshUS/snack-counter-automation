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
builder.Services.AddScoped<RefillService>();
builder.Services.AddHostedService<ExpirationWorker>();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<SnackDb>().InitializeAsync();

// DEMO ONLY: employee IDs in bodies/URLs are trusted. No authentication or authorization.
// Public request projections deliberately omit reporter and assignee identities.
app.MapGet("/", () => new { application = "Snack Counter", warning = "Demo: identities are simulated and trusted; no authentication.",
    endpoints = new[] { "GET /offices", "GET /demo/employees", "GET /refills", "POST /offices/{officeId}/refills",
        "POST /refills/{id}/invite", "GET /demo/employees/{employeeId}/invitations", "POST /refills/{id}/{action}" },
    actions = new[] { "accept", "offer", "decline", "release", "unnecessary", "purchase", "complete" } });
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
app.MapPost("/refills/{id:int}/{action}", async (int id, string action, Identity body, RefillService service) =>
{
    if (action is not ("accept" or "offer" or "decline" or "release" or "unnecessary" or "purchase" or "complete"))
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
app.Run();

public record Identity(int EmployeeId);
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
