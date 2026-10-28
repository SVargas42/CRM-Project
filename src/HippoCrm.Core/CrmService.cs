using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Core;

public sealed class CrmService
{
    private readonly Database _database;
    private readonly Session _session;
    public CrmService(Database database, Session session)
    {
        _database = database; _session = session;
        using var db = database.Open();
        using var cmd = Database.Command(db,"SELECT count(*) FROM users WHERE id=$id AND username=$name",("$id",session.UserId),("$name",session.Username));
        if ((long)cmd.ExecuteScalar()! != 1) throw new UnauthorizedAccessException("Sign in to continue.");
    }
    private static string Column(string name) => Regex.Replace(name,"(?<!^)([A-Z])","_$1").ToLowerInvariant();
    private static PropertyInfo[] Fields<T>() => typeof(T).GetProperties().Where(p => p.CanWrite && p.Name != "Id").ToArray();
    private static T Read<T>(SqliteDataReader reader) where T : new()
    {
        var item = new T();
        foreach (var property in typeof(T).GetProperties().Where(p => p.CanWrite))
        {
            var value = reader[Column(property.Name)];
            property.SetValue(item, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType,(string)value)
                : property.PropertyType == typeof(bool) ? Convert.ToInt64(value) != 0
                : Convert.ChangeType(value,property.PropertyType,CultureInfo.InvariantCulture));
        }
        return item;
    }
    public IReadOnlyList<Contact> Contacts(string search = "", string skill = "", Availability? availability = null, bool archived = false)
    {
        using var db = _database.Open();
        using var cmd = Database.Command(db,"""
            SELECT * FROM contacts WHERE archived=$archived
            AND (instr(lower(first_name || ' ' || last_name),lower($search))>0 OR instr(lower(email),lower($search))>0 OR instr(lower(phone),lower($search))>0)
            AND instr(lower(skills),lower($skill))>0 AND ($availability IS NULL OR availability=$availability)
            ORDER BY last_name COLLATE NOCASE,first_name COLLATE NOCASE
            """,("$archived",archived),("$search",search.Trim()),("$skill",skill.Trim()),("$availability",availability?.ToString()));
        using var reader = cmd.ExecuteReader(); var result = new List<Contact>();
        while (reader.Read()) result.Add(Read<Contact>(reader));
        return result;
    }
    public IReadOnlyList<Organization> Organizations(string search = "", bool archived = false)
    {
        using var db = _database.Open();
        using var cmd = Database.Command(db,"SELECT * FROM organizations WHERE archived=$archived AND instr(lower(name || ' ' || services || ' ' || tags),lower($search))>0 ORDER BY name COLLATE NOCASE",("$archived",archived),("$search",search.Trim()));
        using var reader = cmd.ExecuteReader(); var result = new List<Organization>();
        while (reader.Read()) result.Add(Read<Organization>(reader));
        return result;
    }
    public long Save(Contact contact)
    {
        if (string.IsNullOrWhiteSpace(contact.DisplayName)) throw new ArgumentException("Enter a contact name.");
        if (!Enum.IsDefined(contact.Status) || !Enum.IsDefined(contact.Availability)) throw new ArgumentException("Select a valid status and availability.");
        contact.FirstName = contact.FirstName.Trim(); contact.LastName = contact.LastName.Trim();
        contact.Id = SaveRecord("contacts",contact,contact.Id); return contact.Id;
    }
    public long Save(Organization organization)
    {
        if (string.IsNullOrWhiteSpace(organization.Name)) throw new ArgumentException("Enter an organization name.");
        if (!Enum.IsDefined(organization.Status)) throw new ArgumentException("Select a valid status.");
        organization.Name = organization.Name.Trim();
        organization.Id = SaveRecord("organizations",organization,organization.Id); return organization.Id;
    }
    private long SaveRecord<T>(string table, T record, long id)
    {
        var fields = Fields<T>();
        return Write(table,id,id == 0 ? "Created" : "Updated", (db, tx) =>
        {
            var columns = fields.Select(p => Column(p.Name)).ToArray();
            var sql = id == 0
                ? $"INSERT INTO {table}({string.Join(',',columns)},created_at,updated_at) VALUES({string.Join(',',columns.Select(c => "$"+c))},$now,$now) RETURNING id"
                : $"UPDATE {table} SET {string.Join(',',columns.Select(c => c+"=$"+c))},updated_at=$now WHERE id=$id RETURNING id";
            using var cmd = Database.Command(db,sql,("$now",Database.Now),("$id",id)); cmd.Transaction = tx;
            foreach (var p in fields)
            {
                var value = p.GetValue(record);
                cmd.Parameters.AddWithValue("$"+Column(p.Name),p.PropertyType.IsEnum ? value!.ToString()! : value ?? "");
            }
            return Convert.ToInt64(cmd.ExecuteScalar() ?? throw new ArgumentException("Record no longer exists."));
        });
    }
    private long Write(string entity, long id, string action, Func<SqliteConnection,SqliteTransaction,long> change)
    {
        using var db = _database.Open(); using var tx = db.BeginTransaction();
        var result = change(db,tx);
        using var audit = Database.Command(db,"INSERT INTO audit_log(user_id,entity,entity_id,action,created_at) VALUES($user,$entity,$id,$action,$now)",
            ("$user",_session.UserId),("$entity",entity),("$id",id == 0 ? result : id),("$action",action),("$now",Database.Now));
        audit.Transaction = tx; audit.ExecuteNonQuery(); tx.Commit(); return result;
    }
    private static void RequireActiveContact(SqliteConnection db, SqliteTransaction tx, long id)
    {
        using var cmd = Database.Command(db,"SELECT count(*) FROM contacts WHERE id=$id AND archived=0",("$id",id)); cmd.Transaction = tx;
        if ((long)cmd.ExecuteScalar()! != 1) throw new ArgumentException("Restore this contact before adding records.");
    }
    public void AddInteraction(long contactId, string body)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Enter an interaction note.");
        if (body.Length > 20000) throw new ArgumentException("Notes must contain at most 20,000 characters.");
        Write("interactions",0,"Created",(db,tx) =>
        {
            RequireActiveContact(db,tx,contactId);
            using var cmd = Database.Command(db,"INSERT INTO interactions(contact_id,body,created_at,user_id) VALUES($contact,$body,$now,$user) RETURNING id",("$contact",contactId),("$body",body.Trim()),("$now",Database.Now),("$user",_session.UserId));
            cmd.Transaction = tx; return (long)cmd.ExecuteScalar()!;
        });
    }
    public IReadOnlyList<Interaction> Interactions(long contactId)
    {
        using var db = _database.Open(); using var cmd = Database.Command(db,"SELECT i.id,i.body,i.created_at,u.username FROM interactions i JOIN users u ON u.id=i.user_id WHERE contact_id=$id ORDER BY i.created_at DESC,i.id DESC",("$id",contactId));
        using var reader = cmd.ExecuteReader(); var list = new List<Interaction>();
        while (reader.Read()) list.Add(new(reader.GetInt64(0),contactId,reader.GetString(1),DateTimeOffset.Parse(reader.GetString(2)),reader.GetString(3)));
        return list;
    }
    public void AddEmployment(long contactId, long organizationId, string role, DateOnly start, DateOnly? end)
    {
        if (end < start) throw new ArgumentException("Employment end must be on or after its start.");
        Write("employment",0,"Created",(db,tx) =>
        {
            RequireActiveContact(db,tx,contactId);
            using var org = Database.Command(db,"SELECT count(*) FROM organizations WHERE id=$id AND archived=0",("$id",organizationId)); org.Transaction = tx;
            if ((long)org.ExecuteScalar()! != 1) throw new ArgumentException("Select an active organization.");
            using var overlap = Database.Command(db,"SELECT count(*) FROM employment WHERE contact_id=$id AND start_date <= $end AND COALESCE(end_date,'9999-12-31') >= $start",("$id",contactId),("$start",start.ToString("yyyy-MM-dd")),("$end",end?.ToString("yyyy-MM-dd") ?? "9999-12-31")); overlap.Transaction = tx;
            if ((long)overlap.ExecuteScalar()! > 0) throw new ArgumentException("Employment dates overlap an existing entry. End the current placement before adding another.");
            using var cmd = Database.Command(db,"INSERT INTO employment(contact_id,organization_id,role,start_date,end_date) VALUES($contact,$org,$role,$start,$end) RETURNING id",("$contact",contactId),("$org",organizationId),("$role",role.Trim()),("$start",start.ToString("yyyy-MM-dd")),("$end",end?.ToString("yyyy-MM-dd")));
            cmd.Transaction = tx; return (long)cmd.ExecuteScalar()!;
        });
    }
    public void EndEmployment(long employmentId, DateOnly end)
    {
        Write("employment",employmentId,"Ended",(db,tx) =>
        {
            using var cmd = Database.Command(db,"UPDATE employment SET end_date=$end WHERE id=$id AND end_date IS NULL AND start_date<=$end AND contact_id IN (SELECT id FROM contacts WHERE archived=0) RETURNING id",("$end",end.ToString("yyyy-MM-dd")),("$id",employmentId));
            cmd.Transaction = tx; return (long)(cmd.ExecuteScalar() ?? throw new ArgumentException("Select a current placement and a valid end date."));
        });
    }
    public IReadOnlyList<Employment> EmploymentHistory(long contactId)
    {
        using var db = _database.Open(); using var cmd = Database.Command(db,"SELECT e.id,e.organization_id,o.name,e.role,e.start_date,e.end_date FROM employment e JOIN organizations o ON e.organization_id=o.id WHERE contact_id=$id ORDER BY start_date DESC,e.id DESC",("$id",contactId));
        using var r = cmd.ExecuteReader(); var list = new List<Employment>();
        while (r.Read()) list.Add(new(r.GetInt64(0),contactId,r.GetInt64(1),r.GetString(2),r.GetString(3),DateOnly.Parse(r.GetString(4)),r.IsDBNull(5) ? null : DateOnly.Parse(r.GetString(5))));
        return list;
    }
    public void AddContract(long contactId, DateOnly date, decimal value, string location, string description)
    {
        if (value < 0 || value > 1_000_000_000_000m || decimal.Round(value,2) != value) throw new ArgumentException("Enter a contract value from 0 to 1 trillion with up to two decimal places.");
        Write("contracts",0,"Created",(db,tx) =>
        {
            RequireActiveContact(db,tx,contactId);
            using var cmd = Database.Command(db,"INSERT INTO contracts(contact_id,contract_date,value_cents,location,description) VALUES($contact,$date,$value,$location,$description) RETURNING id",("$contact",contactId),("$date",date.ToString("yyyy-MM-dd")),("$value",(long)(value*100)),("$location",location.Trim()),("$description",description.Trim()));
            cmd.Transaction = tx; return (long)cmd.ExecuteScalar()!;
        });
    }
    public IReadOnlyList<Contract> Contracts(long contactId)
    {
        using var db = _database.Open(); using var cmd = Database.Command(db,"SELECT id,contract_date,value_cents,location,description FROM contracts WHERE contact_id=$id ORDER BY contract_date DESC,id DESC",("$id",contactId));
        using var r = cmd.ExecuteReader(); var list = new List<Contract>();
        while (r.Read()) list.Add(new(r.GetInt64(0),contactId,DateOnly.Parse(r.GetString(1)),r.GetInt64(2)/100m,r.GetString(3),r.GetString(4)));
        return list;
    }
    public Dashboard Dashboard()
    {
        using var db = _database.Open();
        int Count(string sql, params (string,object?)[] parameters) { using var cmd = Database.Command(db,sql,parameters); return Convert.ToInt32(cmd.ExecuteScalar()); }
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30).ToString("O");
        var weeks = new List<WeeklyActivity>();
        for (var i = 3; i >= 0; i--)
        {
            var start = DateTimeOffset.UtcNow.Date.AddDays(-7*(i+1)+1); var end = start.AddDays(7);
            weeks.Add(new(start.ToString("MMM d"),Count("SELECT count(*) FROM interactions WHERE created_at >= $start AND created_at < $end",("$start",new DateTimeOffset(start,TimeSpan.Zero).ToString("O")),("$end",new DateTimeOffset(end,TimeSpan.Zero).ToString("O")))));
        }
        return new(Count("SELECT count(*) FROM contacts WHERE archived=0"),Count("SELECT count(*) FROM organizations WHERE archived=0"),
            Count("SELECT count(*) FROM contacts WHERE archived=0 AND status='Active' AND availability='Available'"),Count("SELECT count(*) FROM contacts WHERE archived=1"),
            Count("SELECT count(*) FROM interactions WHERE created_at >= $cutoff",("$cutoff",cutoff)),
            Count("SELECT count(*) FROM contacts c WHERE archived=0 AND status='Active' AND NOT EXISTS(SELECT 1 FROM interactions i WHERE i.contact_id=c.id AND i.created_at >= $cutoff)",("$cutoff",cutoff)),weeks);
    }
}
