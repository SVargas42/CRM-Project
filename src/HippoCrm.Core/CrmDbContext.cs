using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace HippoCrm.Core;

// Internal data component: application services remain the authorization/audit boundary.
internal sealed class CrmDbContext : DbContext
{
    private readonly SqliteConnection dbEncryptedConnection;
    public RecordStatus enVisibleStatus { get; }
    public DbSet<Contact> dbContacts => Set<Contact>();
    public DbSet<Organization> dbOrganizations => Set<Organization>();
    public DbSet<SessionRow> dbSessions => Set<SessionRow>();
    public DbSet<ContactAccessRow> dbContactAccess => Set<ContactAccessRow>();
    public DbSet<OrganizationAccessRow> dbOrganizationAccess => Set<OrganizationAccessRow>();
    public DbSet<ContactOrganizationRow> dbContactOrganizations => Set<ContactOrganizationRow>();

    public CrmDbContext(string strDatabasePath, IDatabaseKeyStore objKeyStore, RecordStatus enVisibleStatus = RecordStatus.Active)
        : this(new Database(strDatabasePath, objKeyStore), enVisibleStatus) { }
    internal CrmDbContext(Database objDatabase, RecordStatus enVisibleStatus = RecordStatus.Active)
    {
        if (!Enum.IsDefined(enVisibleStatus)) throw new ArgumentException("Select a valid record status.");
        this.enVisibleStatus = enVisibleStatus;
        dbEncryptedConnection = objDatabase.Open();
    }
    protected override void OnConfiguring(DbContextOptionsBuilder objOptions)
    {
        objOptions.UseSqlite(dbEncryptedConnection, contextOwnsConnection: true);
        objOptions.EnableSensitiveDataLogging(false); objOptions.EnableDetailedErrors(false);
    }
    protected override void OnModelCreating(ModelBuilder objModel)
    {
        var objContact = objModel.Entity<Contact>();
        objContact.ToTable("contacts"); objContact.HasKey(objRow => objRow.Id);
        objContact.Ignore(objRow => objRow.lstOrganizationIds); objContact.Ignore(objRow => objRow.lstTags); objContact.Ignore(objRow => objRow.DisplayName);
        MapExisting(objContact, nameof(Contact.lstOrganizationIds));
        objContact.Property(objRow => objRow.Status).HasConversion<string>();
        objContact.Property(objRow => objRow.Availability).HasConversion<string>();
        objContact.Property(objRow => objRow.Revision).IsConcurrencyToken();
        Timestamps(objContact);
        objContact.HasQueryFilter(objRow => objRow.Status == enVisibleStatus && !objRow.Archived);

        var objOrganization = objModel.Entity<Organization>();
        objOrganization.ToTable("organizations"); objOrganization.HasKey(objRow => objRow.Id);
        MapExisting(objOrganization); Timestamps(objOrganization);
        objOrganization.Property(objRow => objRow.Status).HasConversion<string>();
        objOrganization.Property(objRow => objRow.Revision).IsConcurrencyToken();
        objOrganization.HasQueryFilter(objRow => objRow.Status == enVisibleStatus && !objRow.Archived);

        var objSessions = objModel.Entity<SessionRow>(); objSessions.ToTable("sessions"); objSessions.HasKey(objRow => objRow.arrTokenHash);
        objSessions.Property(objRow => objRow.arrTokenHash).HasColumnName("token_hash").ValueGeneratedNever();
        objSessions.Property(objRow => objRow.lngUserId).HasColumnName("user_id");
        objSessions.Property(objRow => objRow.strCreatedAt).HasColumnName("created_at"); Status(objSessions);
        objSessions.HasQueryFilter(objRow => objRow.enStatus == RecordStatus.Active);

        var objContactAccess = objModel.Entity<ContactAccessRow>(); objContactAccess.ToTable("contact_access");
        objContactAccess.HasKey(objRow => new { objRow.lngUserId, objRow.lngContactId });
        objContactAccess.Property(objRow => objRow.lngUserId).HasColumnName("user_id");
        objContactAccess.Property(objRow => objRow.lngContactId).HasColumnName("contact_id");
        objContactAccess.Property(objRow => objRow.bCanWrite).HasColumnName("can_write"); Status(objContactAccess);
        objContactAccess.HasQueryFilter(objRow => objRow.enStatus == RecordStatus.Active);

        var objOrganizationAccess = objModel.Entity<OrganizationAccessRow>(); objOrganizationAccess.ToTable("organization_access");
        objOrganizationAccess.HasKey(objRow => new { objRow.lngUserId, objRow.lngOrganizationId });
        objOrganizationAccess.Property(objRow => objRow.lngUserId).HasColumnName("user_id");
        objOrganizationAccess.Property(objRow => objRow.lngOrganizationId).HasColumnName("organization_id");
        objOrganizationAccess.Property(objRow => objRow.bCanWrite).HasColumnName("can_write"); Status(objOrganizationAccess);
        objOrganizationAccess.HasQueryFilter(objRow => objRow.enStatus == RecordStatus.Active);

        var objLinks = objModel.Entity<ContactOrganizationRow>(); objLinks.ToTable("contact_organizations");
        objLinks.HasKey(objRow => new { objRow.lngContactId, objRow.lngOrganizationId });
        objLinks.Property(objRow => objRow.lngContactId).HasColumnName("contact_id");
        objLinks.Property(objRow => objRow.lngOrganizationId).HasColumnName("organization_id"); Status(objLinks);
        objLinks.HasQueryFilter(objRow => objRow.enStatus == RecordStatus.Active);
    }
    private static void MapExisting<T>(EntityTypeBuilder<T> objEntity, params string[] arrExcluded) where T : class
    {
        foreach (var objProperty in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!objProperty.CanWrite || arrExcluded.Contains(objProperty.Name)) continue;
            var strColumn = objProperty.Name switch
            {
                nameof(Contact.lngOrganizationId) => "organization_id",
                nameof(Contact.strTags) => "tags",
                _ => Regex.Replace(objProperty.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant()
            };
            objEntity.Property(objProperty.Name).HasColumnName(strColumn);
        }
    }
    private static void Timestamps<T>(EntityTypeBuilder<T> objEntity) where T : class
    {
        objEntity.Property<string>("strCreatedAt").HasColumnName("created_at");
        objEntity.Property<string>("strUpdatedAt").HasColumnName("updated_at");
    }
    private static void Status<T>(EntityTypeBuilder<T> objEntity) where T : class, IRetainedRow =>
        objEntity.Property(objRow => objRow.enStatus).HasColumnName("status").HasConversion<string>();
    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries().Any(objEntry => objEntry.State == EntityState.Deleted))
            throw new InvalidOperationException("Deletion is prohibited. Update the record status.");
        var strNow = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        foreach (var objEntry in ChangeTracker.Entries())
        {
            if (objEntry.Entity is not Contact && objEntry.Entity is not Organization) continue;
            if (objEntry.State == EntityState.Added)
            { objEntry.Property("strCreatedAt").CurrentValue = strNow; objEntry.Property("strUpdatedAt").CurrentValue = strNow; }
            else if (objEntry.State == EntityState.Modified)
            {
                objEntry.Property("strUpdatedAt").CurrentValue = strNow;
                var objRevision = objEntry.Property("Revision"); objRevision.CurrentValue = (int)objRevision.OriginalValue! + 1;
            }
        }
    }
    // A context cannot commit unaudited writes. Repositories must stage changes
    // through the authorized transaction API rather than ordinary SaveChanges.
    public override int SaveChanges(bool bAcceptChangesOnSuccess) =>
        throw new InvalidOperationException("Use the authorized, audited transaction API.");
    public override Task<int> SaveChangesAsync(bool bAcceptChangesOnSuccess, CancellationToken ctCancellation = default) =>
        throw new InvalidOperationException("Use the authorized, audited transaction API.");

    internal int SaveAuthorizedChanges(Session objSession)
    {
        if (Database.CurrentTransaction is not null) throw new InvalidOperationException("A new audited transaction is required.");
        using var txContext = Database.BeginTransaction();
        var txSqlite = (SqliteTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(txContext);
        var objIdentity = AccessControl.Identity(dbEncryptedConnection, txSqlite, objSession);
        PrepareChanges();
        var arrChanges = ChangeTracker.Entries().Where(objEntry => objEntry.State is EntityState.Added or EntityState.Modified).ToArray();
        foreach (var objEntry in arrChanges)
        {
            if (objEntry.Entity is Contact objContact)
            {
                if (objEntry.State == EntityState.Added) AccessControl.Administrator(dbEncryptedConnection, txSqlite, objIdentity);
                else AccessControl.Require(dbEncryptedConnection, txSqlite, objIdentity, RecordKind.Contact, objContact.Id, true);
            }
            else if (objEntry.Entity is Organization objOrganization)
            {
                if (objEntry.State == EntityState.Added) AccessControl.Administrator(dbEncryptedConnection, txSqlite, objIdentity);
                else AccessControl.Require(dbEncryptedConnection, txSqlite, objIdentity, RecordKind.Organization, objOrganization.Id, true);
            }
            else throw new InvalidOperationException("Infrastructure changes must use the retention services.");
        }
        var iChanged = base.SaveChanges(acceptAllChangesOnSuccess: false);
        foreach (var objEntry in arrChanges)
            SecurityAudit.Append(dbEncryptedConnection, txSqlite, objIdentity.lngUserId,
                objEntry.Entity is Contact ? AuditEntity.Contacts : AuditEntity.Organizations,
                objEntry.Entity is Contact objContact ? objContact.Id : ((Organization)objEntry.Entity).Id,
                objEntry.State == EntityState.Added ? AuditAction.Created : AuditAction.Updated);
        txContext.Commit(); ChangeTracker.AcceptAllChanges(); return iChanged;
    }
}
internal interface IRetainedRow { RecordStatus enStatus { get; set; } }
internal sealed class SessionRow : IRetainedRow
{
    public byte[] arrTokenHash { get; set; } = [];
    public long lngUserId { get; set; }
    public string strCreatedAt { get; set; } = "";
    public RecordStatus enStatus { get; set; } = RecordStatus.Active;
}
internal sealed class ContactAccessRow : IRetainedRow
{
    public long lngUserId { get; set; } public long lngContactId { get; set; }
    public bool bCanWrite { get; set; } public RecordStatus enStatus { get; set; } = RecordStatus.Active;
}
internal sealed class OrganizationAccessRow : IRetainedRow
{
    public long lngUserId { get; set; } public long lngOrganizationId { get; set; }
    public bool bCanWrite { get; set; } public RecordStatus enStatus { get; set; } = RecordStatus.Active;
}
internal sealed class ContactOrganizationRow : IRetainedRow
{
    public long lngContactId { get; set; } public long lngOrganizationId { get; set; }
    public RecordStatus enStatus { get; set; } = RecordStatus.Active;
}
