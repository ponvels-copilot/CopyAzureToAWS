using CopyAzureToAWS.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace CopyAzureToAWS.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<TableAzureToAWSRequest> TableAzureToAWSRequest { get; set; } = null!;
    public DbSet<TableCallRecordingDetails> TableCallRecordingDetails { get; set; } = null!;
    public DbSet<TableCallDetails> TableCallDetails { get; set; } = null!;
    public DbSet<TableStorage> TableStorage { get; set; } = null!; // ADDED

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

        modelBuilder.Entity<TableCallRecordingDetails>(entity =>
        {
            entity.HasKey(e => e.CallRecordingDetailsID);
            entity.HasIndex(e => e.CallDetailID);
            entity.Property(e => e.IsAzureCloudAudio);
            entity.Property(e => e.IsAzureCloudVideo);
            entity.Property(e => e.AudioFile).HasMaxLength(300);
            entity.Property(e => e.VideoFile).HasMaxLength(300);
            entity.Property(e => e.AudioFileLocation).HasMaxLength(150);
            entity.Property(e => e.VideoFileLocation).HasMaxLength(150);
        });

        // ADDED: storage table mapping
        modelBuilder.Entity<TableStorage>(entity =>
        {
            entity.HasKey(e => e.StorageID);

            entity.Property(e => e.StorageType)
                  .IsRequired()
                  .HasMaxLength(10);

            entity.Property(e => e.CountryID)
                  .IsRequired();

            entity.Property(e => e.Json)
                  .IsRequired()
                  .HasColumnType("jsonb");

            entity.Property(e => e.DefaultStorage)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.ActiveInd)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.CreatedBy)
                  .IsRequired()
                  .HasMaxLength(30);

            entity.Property(e => e.CreatedDate)
                  .IsRequired()
                  .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.UpdatedBy)
                  .HasMaxLength(30);

            // Computed (generated always stored) columns
            entity.Property(e => e.BucketName)
                  .HasComputedColumnSql("(json ->> 'AWSBucketName')", stored: true);

            entity.Property(e => e.AzureBlobEndpoint)
                  .HasComputedColumnSql("((json -> 'MSAzureBlob') ->> 'EndPoint')", stored: true);

            // Helpful indexes
            entity.HasIndex(e => new { e.CountryID, e.StorageType });
            entity.HasIndex(e => e.DefaultStorage);
            entity.HasIndex(e => e.ActiveInd);
        });
    }
}