using Microsoft.EntityFrameworkCore;

namespace CopyAzureToAWS.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<TableAzureToAWSRequest> TableAzureToAWSRequest { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TableAzureToAWSRequest>(entity =>
        {
            entity.HasKey(e => e.CallDetailID);
            entity.Property(e => e.CallDetailID);
            entity.Property(e => e.AudioFile).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Status).HasMaxLength(50);

            entity.HasIndex(e => e.CallDetailID).IsUnique();
            entity.HasIndex(e => e.Status);
        });
    }
}