using AppFileSync.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AppFileSync.Api.Data;

public sealed class AppFileSyncDbContext(DbContextOptions<AppFileSyncDbContext> options) : DbContext(options)
{
    public DbSet<RegisteredApp> Apps => Set<RegisteredApp>();

    public DbSet<SyncedFile> Files => Set<SyncedFile>();

    public DbSet<FileVersion> FileVersions => Set<FileVersion>();

    public DbSet<DeviceRegistration> Devices => Set<DeviceRegistration>();

    public DbSet<KeyEnvelope> KeyEnvelopes => Set<KeyEnvelope>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RegisteredApp>(entity =>
        {
            entity.ToTable("apps");
            entity.HasKey(app => app.AppId);
            entity.Property(app => app.AppId).HasColumnName("app_id");
            entity.Property(app => app.OidcClientId).HasColumnName("oidc_client_id").IsRequired();
            entity.Property(app => app.DisplayName).HasColumnName("display_name").IsRequired();
            entity.Property(app => app.MaxPlaintextBytes).HasColumnName("max_plaintext_bytes");
            entity.Property(app => app.IsEnabled).HasColumnName("is_enabled");
            entity.Property(app => app.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<SyncedFile>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(file => file.Id);
            entity.Property(file => file.Id).HasColumnName("id");
            entity.Property(file => file.OwnerSubject).HasColumnName("owner_subject").IsRequired();
            entity.Property(file => file.AppId).HasColumnName("app_id").IsRequired();
            entity.Property(file => file.FileId).HasColumnName("file_id").IsRequired();
            entity.Property(file => file.Revision).HasColumnName("revision");
            entity.Property(file => file.ChangeSequence).HasColumnName("change_sequence");
            entity.Property(file => file.EncryptedMetadata).HasColumnName("encrypted_metadata").IsRequired();
            entity.Property(file => file.Ciphertext).HasColumnName("ciphertext").IsRequired();
            entity.Property(file => file.CipherHash).HasColumnName("cipher_hash").IsRequired();
            entity.Property(file => file.PlaintextSizeBytes).HasColumnName("plaintext_size_bytes");
            entity.Property(file => file.CiphertextSizeBytes).HasColumnName("ciphertext_size_bytes");
            entity.Property(file => file.IsDeleted).HasColumnName("is_deleted");
            entity.Property(file => file.CreatedAt).HasColumnName("created_at");
            entity.Property(file => file.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(file => new { file.OwnerSubject, file.AppId, file.FileId }).IsUnique();
            entity.HasIndex(file => new { file.OwnerSubject, file.AppId, file.ChangeSequence }).IsUnique();
        });

        modelBuilder.Entity<FileVersion>(entity =>
        {
            entity.ToTable("file_versions");
            entity.HasKey(version => version.Id);
            entity.Property(version => version.Id).HasColumnName("id");
            entity.Property(version => version.SyncedFileId).HasColumnName("file_id");
            entity.Property(version => version.Revision).HasColumnName("revision");
            entity.Property(version => version.EncryptedMetadata).HasColumnName("encrypted_metadata").IsRequired();
            entity.Property(version => version.Ciphertext).HasColumnName("ciphertext").IsRequired();
            entity.Property(version => version.CipherHash).HasColumnName("cipher_hash").IsRequired();
            entity.Property(version => version.CreatedAt).HasColumnName("created_at");
            entity.Property(version => version.CreatedByDeviceId).HasColumnName("created_by_device_id");
            entity.HasOne(version => version.SyncedFile)
                .WithMany(file => file.Versions)
                .HasForeignKey(version => version.SyncedFileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(version => new { version.SyncedFileId, version.Revision }).IsUnique();
        });

        modelBuilder.Entity<DeviceRegistration>(entity =>
        {
            entity.ToTable("devices");
            entity.HasKey(device => device.Id);
            entity.Property(device => device.Id).HasColumnName("id");
            entity.Property(device => device.OwnerSubject).HasColumnName("owner_subject").IsRequired();
            entity.Property(device => device.AppId).HasColumnName("app_id").IsRequired();
            entity.Property(device => device.DisplayName).HasColumnName("display_name").IsRequired();
            entity.Property(device => device.PublicKey).HasColumnName("public_key").IsRequired();
            entity.Property(device => device.CreatedAt).HasColumnName("created_at");
            entity.Property(device => device.RevokedAt).HasColumnName("revoked_at");
            entity.HasIndex(device => new { device.OwnerSubject, device.AppId, device.Id }).IsUnique();
        });

        modelBuilder.Entity<KeyEnvelope>(entity =>
        {
            entity.ToTable("key_envelopes");
            entity.HasKey(envelope => envelope.Id);
            entity.Property(envelope => envelope.Id).HasColumnName("id");
            entity.Property(envelope => envelope.OwnerSubject).HasColumnName("owner_subject").IsRequired();
            entity.Property(envelope => envelope.AppId).HasColumnName("app_id").IsRequired();
            entity.Property(envelope => envelope.DeviceId).HasColumnName("device_id");
            entity.Property(envelope => envelope.EnvelopeVersion).HasColumnName("envelope_version");
            entity.Property(envelope => envelope.EncryptedAppKey).HasColumnName("encrypted_app_key").IsRequired();
            entity.Property(envelope => envelope.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(envelope => new { envelope.OwnerSubject, envelope.AppId, envelope.DeviceId, envelope.EnvelopeVersion });
        });
    }
}
