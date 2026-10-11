using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TicketDeskPro.Application.Interfaces;
using TicketDeskPro.Domain.Entities;
using TicketDeskPro.Domain.Enums;
using TicketDeskPro.Domain.Interfaces;

namespace TicketDeskPro.Infrastructure.Data;

public class AppDbContext : DbContext
{
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public AppDbContext(DbContextOptions<AppDbContext> options, IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options)
    {
        _domainEventDispatcher = domainEventDispatcher;
    }

    // Identity & Access
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    // Ticketing
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<TicketTag> TicketTags => Set<TicketTag>();
    public DbSet<TicketStatusHistory> TicketStatusHistories => Set<TicketStatusHistory>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();

    // SLA
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<SlaTimer> SlaTimers => Set<SlaTimer>();
    public DbSet<SlaBreach> SlaBreaches => Set<SlaBreach>();

    // Audit & Infrastructure
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<EmailOutbox> EmailOutbox => Set<EmailOutbox>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureIdentity(modelBuilder);
        ConfigureTicketing(modelBuilder);
        ConfigureSla(modelBuilder);
        ConfigureAuditAndInfrastructure(modelBuilder);

        ApplySoftDeleteQueryFilters(modelBuilder);
        DisableXminForNonRelationalProviders(modelBuilder);
        DeclareAppAssignedKeys(modelBuilder);
    }

    /// <summary>IAuditable bookkeeping: anything modified gets UpdatedAt = now.</summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// FRD §7.7: domain events are dispatched AFTER the transaction commits, so
    /// handlers (notifications, emails, SignalR) can rely on the data being durable.
    /// ponytail: the sync SaveChanges path does not dispatch — everything is async here.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetUpdatedAt();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        var events = ChangeTracker.Entries<BaseEntity>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();
        if (events.Count > 0)
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
                entry.Entity.ClearDomainEvents();

            if (_domainEventDispatcher is not null)
                await _domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        return result;
    }

    private void SetUpdatedAt()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditable>()
                     .Where(e => e.State is EntityState.Modified or EntityState.Added))
            entry.Entity.UpdatedAt = now; // Added too: a fresh row's UpdatedAt must not be 0001-01-01
    }

    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(e => typeof(ISoftDeletable).IsAssignableFrom(e.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)),
                Expression.Constant(false));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }

    /// <summary>
    /// xmin is a PostgreSQL system column — the InMemory test provider cannot emulate it and
    /// treats the property as a strict concurrency token, failing every UPDATE. Drop the token
    /// for non-relational providers only; Postgres keeps full ETag concurrency.
    /// </summary>
    private void DisableXminForNonRelationalProviders(ModelBuilder modelBuilder)
    {
        if (Database.IsRelational()) return;

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(e => e.GetProperties())
                     .Where(p => p.Name == "Xmin"))
        {
            property.ValueGenerated = ValueGenerated.Never;
            property.IsConcurrencyToken = false;
        }
    }

    /// <summary>
    /// Guid PKs are store-generated by convention, so EF assumes an entity whose key is ALREADY
    /// SET must pre-exist in the store — a nav-discovered new TicketStatusHistory would enter the
    /// tracker as Modified (UPDATE of a nonexistent row). Our keys are assigned in BaseEntity's
    /// constructor, i.e. app-owned: declare them ValueGenerated.Never so new entities are Added.
    /// </summary>
    private void DeclareAppAssignedKeys(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var isAppKeyed = typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)
                             || entityType.ClrType == typeof(AuditLog);
            var id = entityType.FindProperty(nameof(BaseEntity.Id));
            if (isAppKeyed && id is not null && id.ClrType == typeof(Guid))
                id.ValueGenerated = ValueGenerated.Never;
        }
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(builder =>
        {
            builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
            builder.Property(u => u.PasswordHash).IsRequired();
            builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            builder.Property(u => u.Phone).HasMaxLength(30);
            builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(128);

            // BRD/DB Design §3: unique email among non-deleted users.
            builder.HasIndex(u => u.Email).IsUnique()
                .HasDatabaseName("ux_users_email_live")
                .HasFilter("\"IsDeleted\" = FALSE");
        });

        modelBuilder.Entity<Role>(builder =>
        {
            builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
            builder.Property(r => r.Description).HasMaxLength(200);
            builder.HasIndex(r => r.Name).IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");
        });

        modelBuilder.Entity<Permission>(builder =>
        {
            builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
            builder.Property(p => p.Module).HasMaxLength(50).IsRequired();
            builder.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<UserRole>(builder =>
        {
            builder.HasKey(ur => new { ur.UserId, ur.RoleId });
            builder.HasOne(ur => ur.User).WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(ur => ur.Role).WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(builder =>
        {
            builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            builder.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(builder =>
        {
            builder.Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
            builder.HasIndex(t => t.TokenHash).IsUnique();
            builder.HasIndex(t => t.FamilyId);

            // Rotation safety: conditional UPDATE via xmin — parallel reuse of the same
            // refresh token raises DbUpdateConcurrencyException for the loser.
            builder.Property(t => t.Xmin)
                .HasColumnName("xmin")
                .ValueGeneratedOnAddOrUpdate()
                .IsRowVersion();
            builder.HasOne(t => t.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginAttempt>(builder =>
        {
            builder.Property(a => a.Email).HasMaxLength(256).IsRequired();
            builder.Property(a => a.IpAddress).HasMaxLength(45); // IPv6 max length
            builder.HasIndex(a => a.Email);
            builder.HasIndex(a => a.CreatedAt);
        });
    }

    private static void ConfigureTicketing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(builder =>
        {
            builder.Property(t => t.TicketNumber).HasMaxLength(20).IsRequired();
            builder.Property(t => t.Title).HasMaxLength(200).IsRequired();
            builder.Property(t => t.Description).IsRequired();

            builder.HasIndex(t => t.TicketNumber).IsUnique();

            // DB Design §3: dashboard queries filter by status + assignee.
            builder.HasIndex(t => new { t.Status, t.AssignedToId })
                .HasDatabaseName("ix_tickets_status_assigned");

            // DB Design §3: filtered index — deleted tickets are excluded from every query.
            builder.HasIndex(t => t.IsDeleted)
                .HasDatabaseName("ix_tickets_live")
                .HasFilter("\"IsDeleted\" = FALSE");

            // PostgreSQL optimistic concurrency via the xmin system column.
            builder.Property(t => t.Xmin)
                .HasColumnName("xmin")
                .ValueGeneratedOnAddOrUpdate()
                .IsRowVersion();

            builder.HasOne(t => t.Category).WithMany(c => c.Tickets)
                .HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(t => t.CreatedBy).WithMany(u => u.CreatedTickets)
                .HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);
            builder.HasOne(t => t.AssignedTo).WithMany(u => u.AssignedTickets)
                .HasForeignKey(t => t.AssignedToId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TicketMessage>(builder =>
        {
            builder.Property(m => m.Content).IsRequired();

            builder.HasOne(m => m.Ticket).WithMany(t => t.Messages)
                .HasForeignKey(m => m.TicketId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(m => m.CreatedBy).WithMany()
                .HasForeignKey(m => m.CreatedById).OnDelete(DeleteBehavior.Restrict);
            builder.HasIndex(m => m.TicketId);
        });

        modelBuilder.Entity<TicketAttachment>(builder =>
        {
            builder.Property(a => a.FileName).HasMaxLength(260).IsRequired(); // OS-friendly limit
            builder.Property(a => a.StoredPath).HasMaxLength(500).IsRequired();
            builder.Property(a => a.ContentType).HasMaxLength(100).IsRequired();

            builder.HasOne(a => a.Ticket).WithMany(t => t.Attachments)
                .HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(a => a.UploadedBy).WithMany()
                .HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TicketTag>(builder =>
        {
            builder.HasKey(tt => new { tt.TicketId, tt.TagId });
            builder.HasOne(tt => tt.Ticket).WithMany(t => t.Tags)
                .HasForeignKey(tt => tt.TicketId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(tt => tt.Tag).WithMany(tg => tg.TicketTags)
                .HasForeignKey(tt => tt.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketStatusHistory>(builder =>
        {
            builder.Property(h => h.Reason).HasMaxLength(500);

            builder.HasOne(h => h.Ticket).WithMany(t => t.StatusHistory)
                .HasForeignKey(h => h.TicketId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(h => h.ChangedBy).WithMany()
                .HasForeignKey(h => h.ChangedById).OnDelete(DeleteBehavior.SetNull);
            builder.HasIndex(h => h.TicketId);
        });

        modelBuilder.Entity<Category>(builder =>
        {
            builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
            builder.Property(c => c.Description).HasMaxLength(500);
            builder.HasIndex(c => c.Name).IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");
        });

        modelBuilder.Entity<Tag>(builder =>
        {
            builder.Property(t => t.Name).HasMaxLength(50).IsRequired();
            builder.HasIndex(t => t.Name).IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");
        });
    }

    private static void ConfigureSla(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SlaPolicy>(builder =>
        {
            builder.Property(p => p.Name).HasMaxLength(100).IsRequired();

            // One LIVE policy per priority — soft-deleted policies keep their slot
            // out of the index so a priority can be recreated after deletion.
            builder.HasIndex(p => p.Priority).IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE");
        });

        modelBuilder.Entity<SlaTimer>(builder =>
        {
            // Exactly one timer per ticket.
            builder.HasIndex(t => t.TicketId).IsUnique();

            builder.HasOne(t => t.Ticket).WithOne(x => x.SlaTimer)
                .HasForeignKey<SlaTimer>(t => t.TicketId).OnDelete(DeleteBehavior.Cascade);
            builder.HasOne(t => t.SlaPolicy).WithMany(p => p.SlaTimers)
                .HasForeignKey(t => t.SlaPolicyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SlaBreach>(builder =>
        {
            builder.Property(b => b.BreachType).HasConversion<int>();

            builder.HasOne(b => b.Ticket).WithMany()
                .HasForeignKey(b => b.TicketId).OnDelete(DeleteBehavior.Cascade);

            // One row per breach type per ticket.
            builder.HasIndex(b => new { b.TicketId, b.BreachType }).IsUnique();
        });
    }

    private static void ConfigureAuditAndInfrastructure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(builder =>
        {
            builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
            builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
            builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();

            // DB Design §3: history timeline + retention purge queries.
            builder.HasIndex(a => new { a.EntityType, a.EntityId });
            builder.HasIndex(a => a.Timestamp);

            // Audit history must survive even a hard user delete.
            builder.HasOne(a => a.User).WithMany()
                .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmailOutbox>(builder =>
        {
            builder.Property(o => o.ToEmail).HasMaxLength(256).IsRequired();
            builder.Property(o => o.Subject).HasMaxLength(200).IsRequired();
            builder.Property(o => o.Status).HasMaxLength(20).HasConversion<string>().IsRequired();
            builder.Property(o => o.LastError).HasMaxLength(1000);

            // Outbox processor backoff: only pick rows whose NextAttemptAt has passed.
            builder.HasIndex(o => new { o.Status, o.NextAttemptAt });

            // The outbox processor polls pending rows constantly.
            builder.HasIndex(o => o.Status)
                .HasFilter("\"Status\" = 'Pending'");
        });

        modelBuilder.Entity<Notification>(builder =>
        {
            builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
            builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
            builder.Property(n => n.Link).HasMaxLength(500);

            builder.HasOne(n => n.User).WithMany()
                .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(n => new { n.UserId, n.IsRead });
        });

        modelBuilder.Entity<NotificationPreference>(builder =>
        {
            // One preference row per user.
            builder.HasIndex(p => p.UserId).IsUnique();
            builder.HasOne(p => p.User).WithOne(u => u.NotificationPreference)
                .HasForeignKey<NotificationPreference>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
