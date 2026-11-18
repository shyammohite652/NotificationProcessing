using Microsoft.EntityFrameworkCore;
using NotificationApi.Models;

namespace NotificationApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var n = modelBuilder.Entity<Notification>();

        n.HasKey(x => x.Id);
        n.Property(x => x.Id).ValueGeneratedNever();
        n.Property(x => x.CustomerId).HasMaxLength(Notification.CustomerIdMaxLength);
        n.Property(x => x.Recipient).HasMaxLength(Notification.RecipientMaxLength);
        n.Property(x => x.Subject).HasMaxLength(Notification.SubjectMaxLength);
        n.Property(x => x.Message).HasMaxLength(Notification.MessageMaxLength);
        n.Property(x => x.FailureReason).HasMaxLength(Notification.FailureReasonMaxLength);
        n.Property(x => x.DeduplicationKey).HasMaxLength(100).IsUnicode(false);

        // Duplicate protection at database level: the same request in the same time slot can only be stored once.
        n.HasIndex(x => x.DeduplicationKey).IsUnique();

        // Processing query: WHERE Status = 0 ... ORDER BY Priority DESC, CreatedAt.
        n.HasIndex(x => new { x.Status, x.Priority, x.CreatedAt }).IsDescending(false, true, false);

        // Customer list: WHERE CustomerId = @id ORDER BY CreatedAt DESC.
        n.HasIndex(x => new { x.CustomerId, x.CreatedAt });
    }
}
