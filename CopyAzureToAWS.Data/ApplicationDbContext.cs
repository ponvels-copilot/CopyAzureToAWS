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
            entity.Property(e => e.IsEncryptedAudio).HasMaxLength(1);
            entity.Property(e => e.IsEncryptedVideo).HasMaxLength(1);
            entity.Property(e => e.IsAzureCloudAudio);
            entity.Property(e => e.IsAzureCloudVideo);
            entity.Property(e => e.AudioFile).HasMaxLength(300);
            entity.Property(e => e.VideoFile).HasMaxLength(300);
            entity.Property(e => e.AudioFileLocation).HasMaxLength(150);
            entity.Property(e => e.VideoFileLocation).HasMaxLength(150);
            entity.Property(e => e.AudioBucketName).HasMaxLength(150);
            entity.Property(e => e.VideoBucketName).HasMaxLength(150);
            entity.Property(e => e.AudioStorageID);
            entity.Property(e => e.VideoStorageID);
        });
    }
}