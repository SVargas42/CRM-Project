namespace HippoCrm.Core;

public enum RecordStatus { Active, Inactive, Retired, Deceased }
public enum Availability { Available, Unavailable, Unknown }

public sealed class Organization
{
    public int Revision { get; set; }
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
    public int Revision { get; set; }
    public long Id { get; set; }
    public long? lngOrganizationId { get; set; }
    public List<long> lstOrganizationIds { get; set; } = [];
    public string strTags { get; set; } = "";
    public IReadOnlyList<string> lstTags => (strTags ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public string FirstName { get; set; } = "";
    public string MiddleNames { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public IReadOnlyList<string> lstMiddleNameValues
    {
        get
        {
            var strMiddleNames = MiddleNames ?? "";
            return strMiddleNames.Contains('\r') || strMiddleNames.Contains('\n')
                ? strMiddleNames.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : strMiddleNames.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
    }
    public string DisplayMiddleNames => string.Join(" ", lstMiddleNameValues);
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
    public string DisplayName => string.Join(" ", new[] { Prefix, FirstName, DisplayMiddleNames, LastName, Suffix }
        .Where(strPart => !string.IsNullOrWhiteSpace(strPart)).Select(strPart => strPart.Trim()));
    public override string ToString() => $"{DisplayName}  ·  {Availability}  ·  {Status}";
}

public sealed record Interaction(long Id, long ContactId, string Body, DateTimeOffset CreatedAt, string Author);
public sealed record Employment(long Id, long ContactId, long OrganizationId, string OrganizationName, string Role, DateOnly Start, DateOnly? End);
public sealed record Contract(long Id, long ContactId, DateOnly Date, decimal Value, string Location, string Description);
public sealed record ContactEditorData(bool CanEdit, IReadOnlyList<Organization> Organizations,
    IReadOnlyList<Interaction> Interactions, IReadOnlyList<Employment> Employment, IReadOnlyList<Contract> Contracts);
public sealed record TokenChoiceCatalog(IReadOnlyList<string> Tags, IReadOnlyList<string> Skills);
public sealed record Dashboard(int Contacts, int Organizations, int Available, int Archived, int Interactions30Days, int Overdue, IReadOnlyList<WeeklyActivity> Activity);
public sealed record WeeklyActivity(string Label, int Count);
