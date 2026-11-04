using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
namespace HippoCrm.Core;

// Internal data component: application services remain the authorization/audit boundary.
internal sealed class CrmDbContext : DbContext
{
    private readonly SqliteConnection dbEncryptedConnection;
    private int iAuthorizedThreadId;
    private Session? objOperationSession;
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
    public override void Dispose()
    {
        try { base.Dispose(); }
        finally { dbEncryptedConnection.Dispose(); }
    }
    public override async ValueTask DisposeAsync()
    {
        try { await base.DisposeAsync().ConfigureAwait(false); }
        finally { await dbEncryptedConnection.DisposeAsync().ConfigureAwait(false); }
    }
    protected override void OnConfiguring(DbContextOptionsBuilder objOptions)
    {
        objOptions.UseSqlite(dbEncryptedConnection, contextOwnsConnection: true);
        objOptions.EnableSensitiveDataLogging(false); objOptions.EnableDetailedErrors(false);
        objOptions.AddInterceptors(new AuthorizedCommandInterceptor(this));
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
        objContact.HasQueryFilter(objRow => objRow.Status == enVisibleStatus && !objRow.Archived && objRow.strClassification == CuiPolicy.strMarking);

        var objOrganization = objModel.Entity<Organization>();
        objOrganization.ToTable("organizations"); objOrganization.HasKey(objRow => objRow.Id);
        MapExisting(objOrganization); Timestamps(objOrganization);
        objOrganization.Property(objRow => objRow.Status).HasConversion<string>();
        objOrganization.Property(objRow => objRow.Revision).IsConcurrencyToken();
        objOrganization.HasQueryFilter(objRow => objRow.Status == enVisibleStatus && !objRow.Archived && objRow.strClassification == CuiPolicy.strMarking);

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
                nameof(Contact.strClassification) => "classification",
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
            CuiPolicy.ValidateMarking(objEntry.Entity is Contact objContact ? objContact.strClassification : ((Organization)objEntry.Entity).strClassification);
            var enStatus = objEntry.Entity is Contact objStatusContact ? objStatusContact.Status : ((Organization)objEntry.Entity).Status;
            if (!Enum.IsDefined(enStatus)) throw new ArgumentException("Select a valid lifecycle status.");
            if (objEntry.Entity is Contact objAvailability && !Enum.IsDefined(objAvailability.Availability)) throw new ArgumentException("Select valid availability.");
            if (objEntry.State == EntityState.Added && (objEntry.Entity is Contact objArchiveContact ? objArchiveContact.Archived : ((Organization)objEntry.Entity).Archived))
                throw new ArgumentException("Save the record before archiving it.");
            if (objEntry.State == EntityState.Added)
            { objEntry.Property("strCreatedAt").CurrentValue = strNow; objEntry.Property("strUpdatedAt").CurrentValue = strNow; }
            else if (objEntry.State == EntityState.Modified)
            {
                objEntry.Property("strCreatedAt").IsModified = false;
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

    internal IReadOnlyList<Contact> ReadAuthorizedContacts(Session objSession)
    {
        using var txContext = Database.BeginTransaction();
        var txSqlite = (SqliteTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(txContext);
        var objIdentity = AccessControl.Identity(dbEncryptedConnection, txSqlite, objSession);
        Contact[] arrContacts;
        using (AllowCommands(objSession))
        {
            var objQuery = dbContacts.AsNoTracking().Where(objContact => objIdentity.bCuiAuthorized);
            if (objIdentity.enRole != UserRole.Administrator)
                objQuery = objQuery.Where(objContact => dbContactAccess.Any(objGrant => objGrant.lngUserId == objIdentity.lngUserId && objGrant.lngContactId == objContact.Id));
            arrContacts = objQuery.ToArray();
            var objOrganizations = dbOrganizations.AsNoTracking().Where(objOrganization => objIdentity.bCuiAuthorized);
            if (objIdentity.enRole != UserRole.Administrator)
                objOrganizations = objOrganizations.Where(objOrganization => dbOrganizationAccess.Any(objGrant => objGrant.lngUserId == objIdentity.lngUserId && objGrant.lngOrganizationId == objOrganization.Id));
            var setOrganizations = objOrganizations.Select(objOrganization => objOrganization.Id).ToHashSet();
            foreach (var objContact in arrContacts)
                if (objContact.lngOrganizationId is { } lngOrganization && !setOrganizations.Contains(lngOrganization)) objContact.lngOrganizationId = null;
        }
        SecurityAudit.Append(dbEncryptedConnection, txSqlite, objIdentity.lngUserId, AuditEntity.Contacts, 0, AuditAction.AccessVerified);
        txContext.Commit(); return arrContacts;
    }

    internal IReadOnlyList<Organization> ReadAuthorizedOrganizations(Session objSession)
    {
        using var txContext = Database.BeginTransaction();
        var txSqlite = (SqliteTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(txContext);
        var objIdentity = AccessControl.Identity(dbEncryptedConnection, txSqlite, objSession);
        Organization[] arrOrganizations;
        using (AllowCommands(objSession))
        {
            var objQuery = dbOrganizations.AsNoTracking().Where(objOrganization => objIdentity.bCuiAuthorized);
            if (objIdentity.enRole != UserRole.Administrator)
                objQuery = objQuery.Where(objOrganization => dbOrganizationAccess.Any(objGrant => objGrant.lngUserId == objIdentity.lngUserId && objGrant.lngOrganizationId == objOrganization.Id));
            arrOrganizations = objQuery.ToArray();
        }
        SecurityAudit.Append(dbEncryptedConnection, txSqlite, objIdentity.lngUserId, AuditEntity.Organizations, 0, AuditAction.AccessVerified);
        txContext.Commit(); return arrOrganizations;
    }

    internal int SaveAuthorizedChanges(Session objSession)
    {
        if (Database.CurrentTransaction is not null) throw new InvalidOperationException("A new audited transaction is required.");
        using var txContext = Database.BeginTransaction();
        var txSqlite = (SqliteTransaction)Microsoft.EntityFrameworkCore.Storage.DbContextTransactionExtensions.GetDbTransaction(txContext);
        var objIdentity = AccessControl.Identity(dbEncryptedConnection, txSqlite, objSession);
        AccessControl.RequireCui(dbEncryptedConnection, txSqlite, objIdentity);
        PrepareChanges();
        var arrChanges = ChangeTracker.Entries().Where(objEntry => objEntry.State is EntityState.Added or EntityState.Modified).ToArray();
        foreach (var objEntry in arrChanges)
        {
            if (objEntry.Entity is Contact objContact)
            {
                if (objEntry.State == EntityState.Added) AccessControl.Administrator(dbEncryptedConnection, txSqlite, objIdentity);
                else AccessControl.Require(dbEncryptedConnection, txSqlite, objIdentity, RecordKind.Contact, objContact.Id, true);
                if (objContact.lngOrganizationId is { } lngOrganization && (objEntry.State == EntityState.Added || objEntry.Property(nameof(Contact.lngOrganizationId)).IsModified))
                    AccessControl.Require(dbEncryptedConnection, txSqlite, objIdentity, RecordKind.Organization, lngOrganization, false);
            }
            else if (objEntry.Entity is Organization objOrganization)
            {
                if (objEntry.State == EntityState.Added) AccessControl.Administrator(dbEncryptedConnection, txSqlite, objIdentity);
                else AccessControl.Require(dbEncryptedConnection, txSqlite, objIdentity, RecordKind.Organization, objOrganization.Id, true);
            }
            else throw new InvalidOperationException("Infrastructure changes must use the retention services.");
            if (objEntry.State == EntityState.Modified)
            {
                // Disconnected Update marks every field modified, even unchanged
                // lifecycle fields. Compare against live protected storage rather
                // than trusting EF's caller-supplied original values.
                var strTable = objEntry.Entity is Contact ? "contacts" : "organizations";
                var lngRecordId = objEntry.Entity is Contact objExistingContact ? objExistingContact.Id : ((Organization)objEntry.Entity).Id;
                var strOrganizationColumn = objEntry.Entity is Contact ? "organization_id" : "NULL";
                using var cmdLifecycle = new SqliteCommand($"SELECT status,archived,{strOrganizationColumn} FROM {strTable} WHERE id=$id", dbEncryptedConnection, txSqlite);
                cmdLifecycle.Parameters.AddWithValue("$id", lngRecordId);
                using var rdrLifecycle = cmdLifecycle.ExecuteReader();
                var enStatus = objEntry.Entity is Contact objStatusContact ? objStatusContact.Status : ((Organization)objEntry.Entity).Status;
                var bArchived = objEntry.Entity is Contact objArchivedContact ? objArchivedContact.Archived : ((Organization)objEntry.Entity).Archived;
                if (!rdrLifecycle.Read() || rdrLifecycle.GetString(0) != enStatus.ToString() || (rdrLifecycle.GetInt64(1) != 0) != bArchived)
                    throw new InvalidOperationException("Use the authorized lifecycle and archive commands.");
                long? lngPreviousOrganization = rdrLifecycle.IsDBNull(2) ? null : rdrLifecycle.GetInt64(2);
                rdrLifecycle.Close();
                if (objEntry.Entity is Contact objLinkedContact && lngPreviousOrganization is { } lngPrevious
                    && objLinkedContact.lngOrganizationId != lngPrevious
                    && !AccessControl.CanAccess(dbEncryptedConnection, txSqlite, objIdentity, RecordKind.Organization, lngPrevious, false))
                {
                    if (objLinkedContact.lngOrganizationId is not null)
                        AccessControl.Deny(dbEncryptedConnection, txSqlite, objIdentity.lngUserId, AuditEntity.Organizations, lngPrevious);
                    // A masked null from a read is not authority to remove the
                    // hidden reference. Preserve storage without exposing its ID.
                    objEntry.Property(nameof(Contact.lngOrganizationId)).IsModified = false;
                }
                objEntry.Property("Status").IsModified = false; objEntry.Property("Archived").IsModified = false;
            }
        }
        int iChanged;
        using (AllowCommands(objSession)) iChanged = base.SaveChanges(acceptAllChangesOnSuccess: false);
        foreach (var objEntry in arrChanges)
            SecurityAudit.Append(dbEncryptedConnection, txSqlite, objIdentity.lngUserId,
                objEntry.Entity is Contact ? AuditEntity.Contacts : AuditEntity.Organizations,
                objEntry.Entity is Contact objContact ? objContact.Id : ((Organization)objEntry.Entity).Id,
                objEntry.State == EntityState.Added ? AuditAction.Created : AuditAction.Updated);
        txContext.Commit();
        // Do not retain records in a shared context where Local/Find could return
        // a previous actor's cached entity without executing an intercepted query.
        ChangeTracker.AcceptAllChanges(); ChangeTracker.Clear(); return iChanged;
    }

    private IDisposable AllowCommands(Session objSession) => new CommandScope(this, objSession);

    private void GuardCommand(DbCommand cmdOperation)
    {
        if (iAuthorizedThreadId == Environment.CurrentManagedThreadId && objOperationSession is not null)
        {
            // The gateway owns an immediate SQLite transaction, keeping grants and
            // session state stable until its audit insert and commit complete.
            objOperationSession.UseTokenHash(arrHash => true);
            if (cmdOperation.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Deletion is prohibited. Update the record status.");
            return;
        }
        // A direct DbSet query, ExecuteSql, or ExecuteDelete must never become an
        // accidental bypass of the authorized, scoped gateway methods above.
        if (cmdOperation.Transaction is SqliteTransaction txExisting)
            SecurityAudit.Append(dbEncryptedConnection, txExisting, null, AuditEntity.Sessions, 0, AuditAction.PermissionDenied);
        else
        {
            using var txDenied = dbEncryptedConnection.BeginTransaction(false);
            SecurityAudit.Append(dbEncryptedConnection, txDenied, null, AuditEntity.Sessions, 0, AuditAction.PermissionDenied);
            txDenied.Commit();
        }
        throw new UnauthorizedAccessException("Use the authorized, audited data-access methods.");
    }

    private sealed class CommandScope : IDisposable
    {
        private readonly CrmDbContext dbContext;
        internal CommandScope(CrmDbContext dbContext, Session objSession)
        {
            if (Interlocked.CompareExchange(ref dbContext.iAuthorizedThreadId, Environment.CurrentManagedThreadId, 0) != 0)
                throw new InvalidOperationException("Another context operation is already running.");
            this.dbContext = dbContext; dbContext.objOperationSession = objSession;
        }
        public void Dispose() { dbContext.objOperationSession = null; Volatile.Write(ref dbContext.iAuthorizedThreadId, 0); }
    }

    private sealed class AuthorizedCommandInterceptor(CrmDbContext dbContext) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand cmdOperation, CommandEventData objEvent, InterceptionResult<DbDataReader> objResult)
        { dbContext.GuardCommand(cmdOperation); return objResult; }
        public override InterceptionResult<object> ScalarExecuting(DbCommand cmdOperation, CommandEventData objEvent, InterceptionResult<object> objResult)
        { dbContext.GuardCommand(cmdOperation); return objResult; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand cmdOperation, CommandEventData objEvent, InterceptionResult<int> objResult)
        { dbContext.GuardCommand(cmdOperation); return objResult; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand cmdOperation, CommandEventData objEvent, InterceptionResult<DbDataReader> objResult, CancellationToken ctCancellation = default)
        { dbContext.GuardCommand(cmdOperation); return ValueTask.FromResult(objResult); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand cmdOperation, CommandEventData objEvent, InterceptionResult<object> objResult, CancellationToken ctCancellation = default)
        { dbContext.GuardCommand(cmdOperation); return ValueTask.FromResult(objResult); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand cmdOperation, CommandEventData objEvent, InterceptionResult<int> objResult, CancellationToken ctCancellation = default)
        { dbContext.GuardCommand(cmdOperation); return ValueTask.FromResult(objResult); }
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
