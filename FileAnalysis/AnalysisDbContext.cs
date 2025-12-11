using Microsoft.EntityFrameworkCore;

public class AnalysisDbContext : DbContext
{
    public AnalysisDbContext(DbContextOptions<AnalysisDbContext> options) : base(options) { }

    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<AnalysisReport> Reports => Set<AnalysisReport>();
}
