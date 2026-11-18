using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationProcessing.Domain.Entities;

namespace NotificationProcessing.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        // Id is generated in the domain (Guid.NewGuid) so it is known before saving.
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();

        builder.Property(n => n.CustomerId).IsRequired().HasMaxLength(Notification.CustomerIdMaxLength);
        builder.Property(n => n.Recipient).IsRequired().HasMaxLength(Notification.RecipientMaxLength);
        builder.Property(n => n.Subject).HasMaxLength(Notification.SubjectMaxLength);
        builder.Property(n => n.Message).IsRequired().HasMaxLength(Notification.MessageMaxLength);
        builder.Property(n => n.FailureReason).HasMaxLength(Notification.FailureReasonMaxLength);

        // Enums are stored as int (EF default). Priority MUST be numeric so ORDER BY Priority DESC is High→Normal→Low.
        builder.Property(n => n.Type).IsRequired();
        builder.Property(n => n.Priority).IsRequired();
        builder.Property(n => n.Status).IsRequired();

        // datetimeoffset keeps the UTC offset explicit; all values are written as UTC.
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.RequestedDeliveryTime).IsRequired();

        builder.Property(n => n.Fingerprint).IsRequired().IsUnicode(false).HasMaxLength(Notification.FingerprintLength);
        builder.Property(n => n.DeduplicationKey).IsRequired().IsUnicode(false).HasMaxLength(Notification.DeduplicationKeyMaxLength);

        // Database-level duplicate protection: two identical requests in the same window cannot both be inserted.
        builder.HasIndex(n => n.DeduplicationKey)
            .IsUnique()
            .HasDatabaseName("UX_Notifications_DeduplicationKey");

        // Supports the sliding-window duplicate lookup: WHERE Fingerprint = @f AND CreatedAt >= @since.
        builder.HasIndex(n => new { n.Fingerprint, n.CreatedAt })
            .HasDatabaseName("IX_Notifications_Fingerprint_CreatedAt");

        // Supports GET /api/customers/{id}/notifications (newest first).
        builder.HasIndex(n => new { n.CustomerId, n.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Notifications_CustomerId_CreatedAt");

        // The processing queue. Filtered to Pending rows only, so it stays small no matter how many Sent rows exist,
        // and its key order matches the ORDER BY exactly (no sort operator needed). RequestedDeliveryTime is
        // included so the "is it due?" check is evaluated from the index.
        builder.HasIndex(n => new { n.Priority, n.CreatedAt, n.Id })
            .IsDescending(true, false, false)
            .HasFilter("[Status] = 0")
            .IncludeProperties(n => n.RequestedDeliveryTime)
            .HasDatabaseName("IX_Notifications_PendingQueue");
    }
}
