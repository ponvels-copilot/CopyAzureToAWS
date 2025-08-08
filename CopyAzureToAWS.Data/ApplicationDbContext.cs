using Microsoft.EntityFrameworkCore;
using CopyAzureToAWS.Data.Models;

namespace CopyAzureToAWS.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<CallDetail> CallDetails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CallDetail>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CallDetailId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.AudioFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.AzureConnectionString).HasMaxLength(1000);
            entity.Property(e => e.AzureBlobUrl).HasMaxLength(500);
            entity.Property(e => e.S3BucketName).HasMaxLength(255);
            entity.Property(e => e.S3Key).HasMaxLength(500);
            entity.Property(e => e.Md5Checksum).HasMaxLength(32);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
            
            entity.HasIndex(e => e.CallDetailId).IsUnique();
            entity.HasIndex(e => e.Status);
        });
    }
}