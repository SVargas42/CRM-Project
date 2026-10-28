using HippoCrm.Core;
using Microsoft.Data.Sqlite;

var folder = Path.Combine(Path.GetTempPath(),"HippoCrmTests",Guid.NewGuid().ToString("N"));
var path = Path.Combine(folder,"test.db");
var failed = 0; var passed = 0;
try
{
    var database = new Database(path); var auth = new AuthService(database);
    Check(auth.NeedsSetup,"Fresh database needs administrator setup");
    Reject(() => auth.CreateAdministrator("admin","short"),"Short passwords are rejected");
    var session = auth.CreateAdministrator("admin","A local password 2026!");
    Check(!auth.NeedsSetup,"Administrator setup completes");
    Reject(() => auth.CreateAdministrator("other","Another long password!"),"Setup cannot overwrite an existing administrator");
    Check(auth.SignIn(" ADMIN ","A local password 2026!").UserId == session.UserId,"Usernames are case insensitive");
    Reject(() => auth.SignIn("missing","incorrect"),"Unknown account is rejected");
    var crm = new CrmService(database,session);
    Reject(() => new CrmService(database,new Session(999,"unknown")),"Invalid user identity is rejected");
    var org = new Organization { Name = "Eagle Operations",Services = "Logistics",Tags = "Transport" }; crm.Save(org);
    var contact = new Contact { FirstName = "Alex",LastName = "Rivera",Skills = "Logistics, Security",Availability = Availability.Available,Spouse = "Sam",Twitter = "@alex",Email = "alex@example.test" };
    crm.Save(contact);
    Check(contact.Id > 0 && org.Id > 0,"Contacts and organizations persist");
    var reopened = new CrmService(new Database(path),session);
    Check(reopened.Contacts("rivera","SECURITY",Availability.Available).Single().Spouse == "Sam","Search, skill filters, and personal details survive reopening");
    Check(reopened.Contacts(availability: Availability.Unavailable).Count == 0,"Availability filter excludes other records");
    Check(crm.Contacts("' OR 1=1 --").Count == 0,"Search treats SQL text as literal input");
    contact.Phone = "555-0100"; crm.Save(contact);
    Check(crm.Contacts("555-0100").Single().Id == contact.Id,"Contact updates persist");
    Reject(() => crm.Save(new Contact()),"Blank contact names are rejected");
    Reject(() => crm.Save(new Organization()),"Blank organization names are rejected");
    crm.AddInteraction(contact.Id,"Discussed the next assignment.");
    Check(crm.Interactions(contact.Id).Single().Author == "admin","Notes preserve author and timestamp");
    Reject(() => crm.AddInteraction(contact.Id,"  "),"Blank interactions are rejected");
    var before = crm.Contracts(contact.Id).Count;
    Reject(() => crm.AddContract(contact.Id,new DateOnly(2026,10,1),-1,"Austin","Invalid"),"Negative contract values are rejected");
    Reject(() => crm.AddContract(contact.Id,new DateOnly(2026,10,1),1.001m,"Austin","Invalid"),"Fractional cents are rejected");
    Check(crm.Contracts(contact.Id).Count == before,"Rejected contracts do not persist");
    crm.AddContract(contact.Id,new DateOnly(2026,9,1),10000.01m,"Austin","Security");
    crm.AddContract(contact.Id,new DateOnly(2026,10,1),12345.67m,"Dallas","Logistics");
    Check(crm.Contracts(contact.Id).First().Value == 12345.67m,"Contract amounts remain exact and sort newest first");
    crm.AddEmployment(contact.Id,org.Id,"Specialist",new DateOnly(2026,1,1),null);
    Reject(() => crm.AddEmployment(contact.Id,org.Id,"Conflicting",new DateOnly(2026,2,1),null),"Overlapping employment is rejected");
    var placement = crm.EmploymentHistory(contact.Id).Single();
    Reject(() => crm.EndEmployment(placement.Id,new DateOnly(2025,12,31)),"Employment cannot end before its start");
    crm.EndEmployment(placement.Id,new DateOnly(2026,9,30));
    crm.AddEmployment(contact.Id,org.Id,"Lead",new DateOnly(2026,10,1),null);
    Check(crm.EmploymentHistory(contact.Id).Count == 2,"Organization movements retain prior employment");
    var dashboard = crm.Dashboard();
    Check(dashboard.Contacts == 1 && dashboard.Available == 1 && dashboard.Interactions30Days == 1 && dashboard.Overdue == 0,"Dashboard reflects saved records");
    Check(dashboard.Activity.Sum(w => w.Count) == 1,"Weekly activity includes today's interaction");
    contact.Archived = true; crm.Save(contact);
    Check(crm.Contacts().Count == 0 && crm.Contacts(archived:true).Count == 1,"Archived contacts leave active search and remain accessible");
    Reject(() => crm.AddInteraction(contact.Id,"Blocked"),"Archived contacts cannot receive interactions");
    Reject(() => crm.AddContract(contact.Id,new DateOnly(2026,10,1),1,"Austin","Blocked"),"Archived contacts cannot receive contracts");
    contact.Archived = false; crm.Save(contact);
    Check(crm.Contacts().Count == 1 && crm.Interactions(contact.Id).Count == 1,"Restoration preserves history");
    org.Archived = true; crm.Save(org);
    Check(crm.Organizations().Count == 0 && crm.Organizations(archived:true).Count == 1,"Organization archive and restore lists work");
    Reject(() => crm.AddEmployment(contact.Id,org.Id,"Blocked",new DateOnly(2025,1,1),new DateOnly(2025,2,1)),"Archived organizations cannot receive placements");
    foreach (var table in new[] { "users","contacts","organizations","interactions","employment","contracts","audit_log" })
    {
        Reject(() => Sql($"DELETE FROM {table}"),$"Database rejects deletion from {table}");
    }
    Reject(() => Sql("UPDATE interactions SET body='changed'"),"Existing notes cannot be overwritten");
    Reject(() => Sql("UPDATE audit_log SET action='changed'"),"Audit history cannot be overwritten");
    using (var db = database.Open())
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT count(*) FROM audit_log";
        Check((long)cmd.ExecuteScalar()! >= 10,"Record changes generate audit history");
    }
    for (var i = 0; i < 5; i++) Reject(() => auth.SignIn("admin","incorrect"),$"Failed password attempt {i+1} is rejected");
    Reject(() => auth.SignIn("admin","A local password 2026!"),"Persisted lockout blocks correct password after five failures");
    Sql("UPDATE users SET locked_until='2000-01-01T00:00:00.0000000+00:00'");
    Check(auth.SignIn("admin","A local password 2026!").UserId == session.UserId,"Sign-in recovers when lockout expires");

    void Sql(string sql) { using var db = database.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(folder)) Directory.Delete(folder,true);
}
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(bool condition,string name)
{
    if (condition) { passed++; Console.WriteLine($"PASS {name}"); }
    else { failed++; Console.Error.WriteLine($"FAIL {name}"); }
}
void Reject(Action action,string name)
{
    try { action(); Check(false,name); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or SqliteException) { Check(true,name); }
}
