using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

public class Contribution
{
    public int Id { get; set; }
    public int RefillRequestId { get; set; }
    public int EmployeeId { get; set; }
    public string Kind { get; set; } = "";
    public int Points { get; set; }
    public long EarnedAt { get; set; }
}
public record ContributionTotal(int EmployeeId, string Name, int Points, int Reports, int Inspections, int Refills);
public static class Contributions
{
    // Called only in the task-completion transaction. Unique index makes retries safe.
    public static async Task Award(SnackDb db, int requestId, int volunteerId, string kind, long now)
    {
        var reporter = await db.RefillRequests.Where(r => r.Id == requestId).Select(r => r.ReportedById).SingleAsync();
        var points = kind == "Refill" ? 5 : 2;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO Contributions (RefillRequestId, EmployeeId, Kind, Points, EarnedAt) VALUES ({requestId}, {volunteerId}, {kind}, {points}, {now})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO Contributions (RefillRequestId, EmployeeId, Kind, Points, EarnedAt) VALUES ({requestId}, {reporter}, {"Report"}, {1}, {now})");
    }
    public static async Task<List<ContributionTotal>> Leaderboard(SnackDb db)
    {
        var totals = await db.Employees.AsNoTracking()
        .Select(e => new ContributionTotal(e.Id, e.Name,
            db.Contributions.Where(c => c.EmployeeId == e.Id).Sum(c => c.Points),
            db.Contributions.Count(c => c.EmployeeId == e.Id && c.Kind == "Report"),
            db.Contributions.Count(c => c.EmployeeId == e.Id && c.Kind == "Inspection"),
            db.Contributions.Count(c => c.EmployeeId == e.Id && c.Kind == "Refill"))).ToListAsync();
        return totals.OrderByDescending(e => e.Points).ThenBy(e => e.EmployeeId).ToList();
    }
}
