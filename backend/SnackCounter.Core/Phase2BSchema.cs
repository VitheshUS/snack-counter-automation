using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

internal static class Phase2BSchema
{
    public static void Configure(ModelBuilder m)
    {
        m.Entity<ReimbursementClaim>().HasOne<PurchaseCommitment>().WithMany().HasForeignKey(c => c.PurchaseCommitmentId);
        m.Entity<ReimbursementClaim>().HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId);
        m.Entity<ReimbursementClaim>().HasIndex(c => c.PurchaseCommitmentId).IsUnique();
        m.Entity<ReimbursementClaim>().HasIndex(c => c.IdempotencyKey).IsUnique();
        m.Entity<MockHrClaim>().HasKey(h => h.IdempotencyKey);
        m.Entity<Contribution>().HasOne<RefillRequest>().WithMany().HasForeignKey(c => c.RefillRequestId);
        m.Entity<Contribution>().HasOne<Employee>().WithMany().HasForeignKey(c => c.EmployeeId);
        m.Entity<Contribution>().HasIndex(c => new { c.RefillRequestId, c.EmployeeId, c.Kind }).IsUnique();
        m.Entity<ReportingRate>().HasKey(r => r.Month);
    }
    public static Task Upgrade(SnackDb db) => db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS ReimbursementClaims (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            PurchaseCommitmentId INTEGER NOT NULL REFERENCES PurchaseCommitments(Id),
            EmployeeId INTEGER NOT NULL REFERENCES Employees(Id), AmountMinor INTEGER NOT NULL,
            Currency TEXT NOT NULL, Description TEXT NOT NULL, ReceiptReference TEXT NOT NULL,
            IdempotencyKey TEXT NOT NULL, State INTEGER NOT NULL, RequiresHrReview INTEGER NOT NULL,
            CreatedAt INTEGER NOT NULL, UpdatedAt INTEGER NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ReimbursementClaims_PurchaseCommitmentId ON ReimbursementClaims(PurchaseCommitmentId);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_ReimbursementClaims_IdempotencyKey ON ReimbursementClaims(IdempotencyKey);
        CREATE INDEX IF NOT EXISTS IX_ReimbursementClaims_EmployeeId ON ReimbursementClaims(EmployeeId);
        CREATE TABLE IF NOT EXISTS MockHrClaims (
            IdempotencyKey TEXT NOT NULL PRIMARY KEY, EmployeeId INTEGER NOT NULL, AmountMinor INTEGER NOT NULL,
            Currency TEXT NOT NULL, Description TEXT NOT NULL, ReceiptReference TEXT NOT NULL,
            RequiresHrReview INTEGER NOT NULL, State INTEGER NOT NULL, SubmittedAt INTEGER NOT NULL, DecisionAvailableAt INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS Contributions (
            Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, RefillRequestId INTEGER NOT NULL REFERENCES RefillRequests(Id),
            EmployeeId INTEGER NOT NULL REFERENCES Employees(Id), Kind TEXT NOT NULL, Points INTEGER NOT NULL, EarnedAt INTEGER NOT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Contributions_RefillRequestId_EmployeeId_Kind ON Contributions(RefillRequestId, EmployeeId, Kind);
        CREATE INDEX IF NOT EXISTS IX_Contributions_EmployeeId ON Contributions(EmployeeId);
        CREATE TABLE IF NOT EXISTS ReportingRates (
            Month TEXT NOT NULL PRIMARY KEY, Numerator INTEGER NOT NULL, Denominator INTEGER NOT NULL,
            SourceCurrency TEXT NOT NULL, TargetCurrency TEXT NOT NULL, RecordedAt INTEGER NOT NULL);
        """);
}
