namespace HippoCrm.Core;

public enum RecordStatus { Active, Inactive, Retired, Deceased }
public enum Availability { Available, Unavailable, Unknown }

public sealed class Organization
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Services { get; set; } = "";
    public string Tags { get; set; } = "";
    public RecordStatus Status { get; set; }
    public bool Archived { get; set; }
    public override string ToString() => Name;
}

public sealed class Contact
{
    public long Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string Skills { get; set; } = "";
    public string Spouse { get; set; } = "";
    public string Children { get; set; } = "";
    public string Family { get; set; } = "";
    public string Friends { get; set; } = "";
    public string Twitter { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string Snapchat { get; set; } = "";
    public string Facebook { get; set; } = "";
    public RecordStatus Status { get; set; }
    public Availability Availability { get; set; } = Availability.Unknown;
    public bool Archived { get; set; }
    public string DisplayName => $"{FirstName} {LastName}".Trim();
    public override string ToString() => $"{DisplayName}  ·  {Availability}  ·  {Status}";
}

public sealed record Interaction(long Id, long ContactId, string Body, DateTimeOffset CreatedAt, string Author);
public sealed record Employment(long Id, long ContactId, long OrganizationId, string OrganizationName, string Role, DateOnly Start, DateOnly? End);
public sealed record Contract(long Id, long ContactId, DateOnly Date, decimal Value, string Location, string Description);
public sealed record Dashboard(int Contacts, int Organizations, int Available, int Archived, int Interactions30Days, int Overdue, IReadOnlyList<WeeklyActivity> Activity);
public sealed record WeeklyActivity(string Label, int Count);
public sealed record Session(long UserId, string Username);
