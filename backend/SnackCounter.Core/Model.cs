using Microsoft.EntityFrameworkCore;

namespace SnackCounter.Core;

public enum RefillState { Open, Reserved, Purchasing, NeedsReview, Completed }
public enum AttemptState { Invited, Declined, Ignored, Reserved, Released, Expired, Purchasing, Completed }
public class Office { public int Id { get; set; } public string Name { get; set; } = ""; }
public class Employee
{
    public int Id { get; set; }
    public int OfficeId { get; set; }
    public string Name { get; set; } = "";
    public bool Volunteers { get; set; }
}
public class RefillRequest
{
    public int Id { get; set; }
    public int OfficeId { get; set; }
    public int ReportedById { get; set; }
    public RefillState State { get; set; }
    public long CreatedAt { get; set; }
    public long? CompletedAt { get; set; }
    public string? Outcome { get; set; }
}
public class RefillAssignment
{
    public int Id { get; set; }
    public int RefillRequestId { get; set; }
    public int EmployeeId { get; set; }
    public AttemptState State { get; set; }
    public long InvitedAt { get; set; }
    public long InvitationExpiresAt { get; set; }
    public long? RespondedAt { get; set; }
    public long? ReservedAt { get; set; }
    public long? ReservationExpiresAt { get; set; }
    public long? PurchasingAt { get; set; }
    public long? EndedAt { get; set; }
}
public class SnackDb(DbContextOptions<SnackDb> options) : DbContext(options)
{
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<RefillRequest> RefillRequests => Set<RefillRequest>();
    public DbSet<RefillAssignment> RefillAssignments => Set<RefillAssignment>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Employee>().HasOne<Office>().WithMany().HasForeignKey(x => x.OfficeId);
        m.Entity<RefillRequest>().HasOne<Office>().WithMany().HasForeignKey(x => x.OfficeId);
        m.Entity<RefillRequest>().HasOne<Employee>().WithMany().HasForeignKey(x => x.ReportedById);
        m.Entity<RefillRequest>().HasIndex(x => x.OfficeId).IsUnique().HasFilter("State <> 4");
        m.Entity<RefillAssignment>().HasOne<RefillRequest>().WithMany().HasForeignKey(x => x.RefillRequestId);
        m.Entity<RefillAssignment>().HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId);
        m.Entity<RefillAssignment>().HasIndex(x => new { x.RefillRequestId, x.EmployeeId }).IsUnique()
            .HasFilter("State IN (0, 3, 6)");
        m.Entity<RefillAssignment>().HasIndex(x => x.RefillRequestId).IsUnique().HasFilter("State IN (3, 6)");
        m.Entity<Office>().HasData(new Office { Id = 1, Name = "Oslo" }, new Office { Id = 2, Name = "Bengaluru" });
        m.Entity<Employee>().HasData(Enumerable.Range(1, 6).Select(i => new Employee
        { Id = i, OfficeId = i <= 3 ? 1 : 2, Name = new[] { "Ada", "Erik", "Liv", "Anu", "Ravi", "Priya" }[i - 1], Volunteers = true }));
    }

    public async Task InitializeAsync()
    {
        await Database.EnsureCreatedAsync();
        // Phase 1's only schema change: retain history, enforce uniqueness only for active attempts.
        // Upgrade the existing demo database without deleting its records.
        using var tx = await Database.BeginTransactionAsync();
        await Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_RefillAssignments_RefillRequestId_EmployeeId");
        await Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IX_RefillAssignments_RefillRequestId_EmployeeId ON RefillAssignments (RefillRequestId, EmployeeId) WHERE State IN (0, 3, 6)");
        await tx.CommitAsync();
    }
}
