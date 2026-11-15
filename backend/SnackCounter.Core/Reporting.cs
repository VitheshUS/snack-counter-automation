using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

public class ReportingOptions
{
    // Simulated NOK per INR. Rational fixed rate, never floating point.
    public long InrToNokNumerator { get; set; } = 12;
    public long InrToNokDenominator { get; set; } = 100;
}
public class ReportingRate
{
    public string Month { get; set; } = "";
    public long Numerator { get; set; }
    public long Denominator { get; set; }
    public string SourceCurrency { get; set; } = "INR";
    public string TargetCurrency { get; set; } = "NOK";
    public long RecordedAt { get; set; }
}
public record OfficeSpending(int OfficeId, string Office, string Month, string Currency, long SpentMinor, long OutstandingMinor);
public record ConsolidatedSpending(string Month, string Currency, decimal SpentMinor, decimal OutstandingMinor,
    ReportingRate ExchangeRate, List<OfficeSpending> NativeTotals);
public class ReportingService(SnackDb db, TimeProvider clock, BudgetOptions budgets, ReportingOptions rates)
{
    public static void ValidateMonth(string month)
    {
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Month must be yyyy-MM.");
    }
    public async Task<List<OfficeSpending>> Native(string month)
    {
        ValidateMonth(month);
        // One SELECT gives a consistent view of spending and commitments during reconciliation.
        var rows = await db.Offices.AsNoTracking().Select(o => new
        {
            o.Id, o.Name,
            Currency = db.MonthlyBudgets.Where(b => b.OfficeId == o.Id && b.Month == month).Select(b => b.Currency).FirstOrDefault(),
            Spent = db.PurchaseCommitments.Where(p => p.OfficeId == o.Id && p.Month == month && !p.Cancelled).Sum(p => p.ActualMinor ?? 0),
            Outstanding = db.PurchaseCommitments.Where(p => p.OfficeId == o.Id && p.Month == month && !p.Cancelled && p.ActualMinor == null).Sum(p => p.EstimateMinor)
        }).ToListAsync();
        return rows.Select(r => new OfficeSpending(r.Id, r.Name, month, r.Currency ?? budgets.Offices[r.Id].Currency, r.Spent, r.Outstanding)).ToList();
    }
    public async Task<ConsolidatedSpending> Consolidated(string month)
    {
        ValidateMonth(month);
        using var tx = await db.Database.BeginTransactionAsync();
        var now = clock.GetUtcNow().UtcTicks;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT OR IGNORE INTO ReportingRates (Month, Numerator, Denominator, SourceCurrency, TargetCurrency, RecordedAt) VALUES ({month}, {rates.InrToNokNumerator}, {rates.InrToNokDenominator}, {"INR"}, {"NOK"}, {now})");
        var rate = await db.ReportingRates.AsNoTracking().SingleAsync(r => r.Month == month);
        var native = await Native(month);
        decimal Convert(long amount, string currency) => currency switch
        {
            "NOK" => amount,
            "INR" => decimal.Round((decimal)amount * rate.Numerator / rate.Denominator, 0, MidpointRounding.ToEven),
            _ => throw new InvalidOperationException("Unsupported report currency.")
        };
        var result = new ConsolidatedSpending(month, "NOK", native.Sum(n => Convert(n.SpentMinor, n.Currency)),
            native.Sum(n => Convert(n.OutstandingMinor, n.Currency)), rate, native);
        await tx.CommitAsync();
        return result;
    }
}
