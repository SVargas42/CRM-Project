using System.Globalization;
using HippoCrm.Core;
using HippoCrm.Desktop.Utilities;

namespace HippoCrm.Desktop.ViewModels;

// Display projections own their text, and are cleared along with the snapshot owner.
public sealed class AnalyticsDisplayRow : ViewModelBase
{
    public long lngId { get; }
    public string[] arrCells { get; }
    public string strSummary => string.Join("; ", arrCells);
    public AnalyticsDisplayRow(long lngId, params string[] arrCells) { this.lngId = lngId; this.arrCells = arrCells; }
    public void Release() { Array.Clear(arrCells); Notify(nameof(arrCells)); Notify(nameof(strSummary)); }
}

public sealed class AnalyticsTableViewModel : ViewModelBase
{
    private readonly Func<bool> fnVisible;
    private readonly Action<long?>? actSelect;
    private List<AnalyticsDisplayRow> lstCurrent = [];
    private AnalyticsDisplayRow? objCurrentSelection;
    private string strCurrentCount = "";
    public string strTitle { get; }
    public string[] arrHeaders { get; }
    public bool bSelectable => actSelect is not null;
    public IReadOnlyList<AnalyticsDisplayRow> lstRows => fnVisible() ? lstCurrent : [];
    public string strCount => fnVisible() ? strCurrentCount : "";
    public bool bEmpty => lstRows.Count == 0;
    public AnalyticsDisplayRow? objSelectedRow
    {
        get => fnVisible() ? objCurrentSelection : null;
        set { if (Set(ref objCurrentSelection, value)) actSelect?.Invoke(value?.lngId); }
    }
    public AnalyticsTableViewModel(string strTitle, Func<bool> fnVisible, Action<long?>? actSelect, params string[] arrHeaders)
    { this.strTitle = strTitle; this.fnVisible = fnVisible; this.actSelect = actSelect; this.arrHeaders = arrHeaders; }
    internal void Replace(IEnumerable<AnalyticsDisplayRow> lstRows, int iLoaded, long lngTotal, long? lngSelected = null)
    {
        foreach (var objRow in lstCurrent) objRow.Release();
        lstCurrent.Clear(); lstCurrent = lstRows.ToList();
        strCurrentCount = $"Showing {lstCurrent.Count} of {iLoaded} loaded ({lngTotal} total)";
        objSelectedRow = lngSelected.HasValue ? lstCurrent.FirstOrDefault(objRow => objRow.lngId == lngSelected.Value) : null;
        Notify(nameof(lstRows)); Notify(nameof(strCount)); Notify(nameof(bEmpty));
    }
    internal void Clear() { Replace([], 0, 0); strCurrentCount = ""; Notify(nameof(strCount)); }
}

public sealed partial class AnalyticsViewModel
{
    private bool bCurrentInclude;
    private bool bCurrentActive = true, bCurrentInactive, bCurrentRetired, bCurrentDeceased;
    private string strCurrentDays = "30", strCurrentSkill = "";
    private int iCurrentPeriod, iCurrentOrganizationLifecycle;
    private long? lngSelectedOrganization;
    private string strCurrentDaysError = "";
    public AnalyticsTableViewModel vmPeriodDetail { get; private set; } = null!;
    public AnalyticsTableViewModel vmPeriods { get; private set; } = null!;
    public AnalyticsTableViewModel vmContracts { get; private set; } = null!;
    public AnalyticsTableViewModel vmLastContacted { get; private set; } = null!;
    public AnalyticsTableViewModel vmSkills { get; private set; } = null!;
    public AnalyticsTableViewModel vmOrganizations { get; private set; } = null!;
    public AnalyticsTableViewModel vmMembers { get; private set; } = null!;
    public AnalyticsTableViewModel vmMovement { get; private set; } = null!;
    public IReadOnlyList<string> lstPeriods { get; } = ["24 hours", "7 days", "30 days", "6 months", "1 year", "Year to date"];
    public bool bIncludeInactive { get => bCurrentInclude; set { if (Set(ref bCurrentInclude, value)) RebuildTables(); } }
    public bool bActive { get => bCurrentActive; set { if (Set(ref bCurrentActive, value)) RebuildTables(); } }
    public bool bInactive { get => bCurrentInactive; set { if (Set(ref bCurrentInactive, value)) RebuildTables(); } }
    public bool bRetired { get => bCurrentRetired; set { if (Set(ref bCurrentRetired, value)) RebuildTables(); } }
    public bool bDeceased { get => bCurrentDeceased; set { if (Set(ref bCurrentDeceased, value)) RebuildTables(); } }
    public int iPeriod { get => iCurrentPeriod; set { if (value is >= 0 and < 6 && Set(ref iCurrentPeriod, value)) RebuildTables(); } }
    public int iOrganizationLifecycle { get => iCurrentOrganizationLifecycle; set { if (value is 0 or 1 && Set(ref iCurrentOrganizationLifecycle, value)) { lngSelectedOrganization = null; RebuildTables(); } } }
    public string strDays { get => objSnapshot is null ? "" : strCurrentDays; set { if (Set(ref strCurrentDays, value ?? "")) RebuildTables(); } }
    public string strSkillSearch { get => strCurrentSkill; set { if (Set(ref strCurrentSkill, value ?? "")) RebuildTables(); } }
    public string strDaysError => strCurrentDaysError;
    public bool bDaysError => strDaysError.Length > 0;
    public bool bNoSkills => vmSkills.bEmpty;
    public bool bNoMovement => vmMovement.bEmpty;
    public bool bSelectOrganization => !lngSelectedOrganization.HasValue;
    public bool bNoMembers => objSnapshot is not null && lngSelectedOrganization.HasValue && vmMembers.bEmpty;
    public string strShownContactStatuses => objSnapshot is null ? "" : "Showing contacts with status: "
        + (new[] { RecordStatus.Active, RecordStatus.Inactive, RecordStatus.Retired, RecordStatus.Deceased }.Where(Status).ToArray() is { Length: > 0 } arrStatuses
            ? string.Join(", ", arrStatuses) : "None");
    public string strActiveContacts => ContactCount(RecordStatus.Active);
    public string strInactiveContacts => ContactCount(RecordStatus.Inactive);
    public string strRetiredContacts => ContactCount(RecordStatus.Retired);
    public string strDeceasedContacts => ContactCount(RecordStatus.Deceased);
    public string strActiveOrganizations => OrganizationCount(false);
    public string strArchivedOrganizations => OrganizationCount(true);
    private string ContactCount(RecordStatus enStatus) => objSnapshot is null ? "" : Number(objSnapshot.ContactTotals
        .Where(objTotal => objTotal.enStatus == enStatus && (bIncludeInactive || (enStatus == RecordStatus.Active && !objTotal.bArchived)))
        .Sum(objTotal => objTotal.lngCount));
    private string OrganizationCount(bool bArchived) => objSnapshot is null ? "" : Number(objSnapshot.OrganizationTotals
        .Where(objTotal => objTotal.bArchived == bArchived && (bIncludeInactive || !bArchived)).Sum(objTotal => objTotal.lngCount));
    private bool Status(RecordStatus enStatus) => enStatus switch
    { RecordStatus.Active => bActive, RecordStatus.Inactive => bInactive, RecordStatus.Retired => bRetired, RecordStatus.Deceased => bDeceased, _ => false };
    private void InitializeTables()
    {
        bool Visible() => objSnapshot is not null;
        var arrPeriodHeaders = new[] { "Period", "Notes now / previous (change)", "Contacts now / previous (change)", "Organizations now / previous (change)", "Notes/day now", "Notes/day previous" };
        vmPeriodDetail = new("Selected period", Visible, null, arrPeriodHeaders);
        vmPeriods = new("Period comparison", Visible, null, arrPeriodHeaders);
        vmContracts = new("Latest contract per contact", Visible, null, "Name", "Organizations", "Date (UTC)", "Value (USD)", "Location", "Current contract");
        vmLastContacted = new("Last contacted", Visible, null, "Name", "Status", "Organizations", "Last note (UTC)", "Days since");
        vmSkills = new("Skill roster", Visible, null, "Name", "Status", "Organizations", "Skills");
        vmOrganizations = new("Organization roster", Visible, lngId =>
        { if (lngSelectedOrganization != lngId) { lngSelectedOrganization = lngId; RebuildMembers(); } }, "Organization", "Active contact count");
        vmMembers = new("Organization members", Visible, null, "Name", "Status");
        vmMovement = new("Movement history", Visible, null, "Contact", "Organization", "Role", "Start (UTC)", "End (UTC)");
    }
    private IEnumerable<AnalyticsTableViewModel> Tables() => [vmPeriodDetail, vmPeriods, vmContracts, vmLastContacted, vmSkills, vmOrganizations, vmMembers, vmMovement];
    private void ClearTables()
    {
        lngSelectedOrganization = null;
        foreach (var vmTable in Tables()) vmTable.Clear();
        // User-entered search text may itself contain CUI; clear without starting another load.
        strCurrentSkill = ""; strCurrentDays = "30"; strCurrentDaysError = "";
        Notify(nameof(strSkillSearch)); Notify(nameof(strDays)); Notify(nameof(strDaysError)); Notify(nameof(bDaysError));
        NotifyPresentation();
    }
    private void RebuildTables()
    {
        var objData = objSnapshot;
        if (objData is null) { ClearTables(); return; }
        var lstPeriods = this.lstPeriods.Select((strName, iIndex) => PeriodRow(objData, strName, iIndex)).ToArray();
        vmPeriods.Replace(lstPeriods, 6, 6);
        // Separate row ownership: clearing one table must not clear another table's cells.
        vmPeriodDetail.Replace([PeriodRow(objData, this.lstPeriods[iPeriod], iPeriod)], 6, 6);
        vmContracts.Replace(objData.LatestContracts.Where(objRow => Status(objRow.enStatus))
            .Select(objRow => new AnalyticsDisplayRow(objRow.lngId, objRow.strName, objRow.strOrganizations, Date(objRow.dtDate),
                (objRow.lngValueCents / 100m).ToString("C2", CultureInfo.GetCultureInfo("en-US")), objRow.strLocation, "Not tracked yet")), objData.LatestContracts.Count, objData.LatestContractsTotal);
        var bValidDays = strDays.Length is >= 1 and <= 4 && strDays.All(chrValue => chrValue is >= '0' and <= '9')
            && int.TryParse(strDays, NumberStyles.None, CultureInfo.InvariantCulture, out var iDays) && iDays is >= 1 and <= 3650;
        var iThreshold = bValidDays ? int.Parse(strDays, CultureInfo.InvariantCulture) : 0;
        strCurrentDaysError = bValidDays ? "" : "Enter a whole number from 1 to 3650.";
        Notify(nameof(strDaysError)); Notify(nameof(bDaysError));
        vmLastContacted.Replace(objData.LastContacted.Where(objRow => bValidDays && Status(objRow.enStatus)
                && (!objRow.dtLastNoteUtc.HasValue || (objData.AsOfUtc - objRow.dtLastNoteUtc.Value).TotalDays >= iThreshold))
            .Select(objRow => new AnalyticsDisplayRow(objRow.lngId, objRow.strName, StatusText(objRow.enStatus, objRow.bArchived), objRow.strOrganizations,
                objRow.dtLastNoteUtc?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "Never",
                objRow.dtLastNoteUtc.HasValue ? Number((long)Math.Max(0, Math.Floor((objData.AsOfUtc - objRow.dtLastNoteUtc.Value).TotalDays))) : "n/a")), objData.LastContacted.Count, objData.LastContactedTotal);
        vmSkills.Replace(objData.SkillRoster.Where(objRow => Status(objRow.enStatus) && SkillMatches(objRow.strSkills))
            .Select(objRow => new AnalyticsDisplayRow(objRow.lngId, objRow.strName, StatusText(objRow.enStatus, objRow.bArchived), objRow.strOrganizations, objRow.strSkills)), objData.SkillRoster.Count, objData.SkillRosterTotal);
        var lstOrganizations = objData.OrganizationRoster.Where(objRow => objRow.bArchived == (iOrganizationLifecycle == 1)).ToArray();
        if (!lstOrganizations.Any(objRow => objRow.lngId == lngSelectedOrganization)) lngSelectedOrganization = null;
        vmOrganizations.Replace(lstOrganizations.Select(objRow => new AnalyticsDisplayRow(objRow.lngId,
            objRow.strName + (objRow.bArchived ? " (Archived)" : ""), Number(objRow.lngActiveContacts))), objData.OrganizationRoster.Count, objData.OrganizationRosterTotal, lngSelectedOrganization);
        RebuildMembers();
        vmMovement.Replace(objData.Movement.Where(objRow => Status(objRow.enStatus)).Select(objRow => new AnalyticsDisplayRow(objRow.lngContactId,
            objRow.strName, objRow.strOrganization + (objRow.bOrganizationArchived ? " (Archived)" : ""), objRow.strRole, Date(objRow.dtStart), objRow.dtEnd.HasValue ? Date(objRow.dtEnd.Value) : "Current")), objData.Movement.Count, objData.MovementTotal);
        NotifyPresentation();
    }
    private void RebuildMembers()
    {
        var objData = objSnapshot;
        vmMembers.Replace(objData?.OrganizationMembers.Where(objRow => objRow.lngOrganizationId == lngSelectedOrganization && Status(objRow.enStatus))
            .Select(objRow => new AnalyticsDisplayRow(objRow.lngContactId, objRow.strName, StatusText(objRow.enStatus, objRow.bArchived))) ?? [],
            objData?.OrganizationMembers.Count ?? 0, objData?.OrganizationMembersTotal ?? 0);
        Notify(nameof(bSelectOrganization));
        Notify(nameof(bNoMembers));
    }
    private bool SkillMatches(string strSkills)
    {
        var strTerm = strSkillSearch.Trim();
        if (strTerm.Length == 0) return true;
        if (strSkills.Contains(strTerm, StringComparison.OrdinalIgnoreCase)) return true;
        var strCode = SoundexHelper.GetSoundexCode(strTerm);
        // Soundex operates on each comma/space-delimited word, never a complete skills field.
        return strCode.Length > 0 && strSkills.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(strWord => SoundexHelper.GetSoundexCode(strWord) == strCode);
    }
    private AnalyticsDisplayRow PeriodRow(AnalyticsSnapshot objData, string strPeriod, int iIndex)
    {
        var objNow = objData.PeriodSummaries.FirstOrDefault(objRow => objRow.strPeriod == strPeriod && objRow.bActiveOnly == !bIncludeInactive && !objRow.bPrevious);
        var objPrevious = objData.PeriodSummaries.FirstOrDefault(objRow => objRow.strPeriod == strPeriod && objRow.bActiveOnly == !bIncludeInactive && objRow.bPrevious);
        return new(iIndex, strPeriod, Comparison(objNow?.lngNotes ?? 0, objPrevious?.lngNotes ?? 0),
            Comparison(objNow?.lngContacts ?? 0, objPrevious?.lngContacts ?? 0), Comparison(objNow?.lngOrganizations ?? 0, objPrevious?.lngOrganizations ?? 0), Average(objNow), Average(objPrevious));
    }
    private static string Comparison(long lngNow, long lngPrevious) => $"{Number(lngNow)} / {Number(lngPrevious)} ({(lngPrevious == 0 ? "n/a" : ((lngNow - (decimal)lngPrevious) * 100m / lngPrevious).ToString("0.0", CultureInfo.InvariantCulture) + "%")})";
    private static string Average(AnalyticsPeriodSummary? objPeriod) => objPeriod is null || (objPeriod.dtEndUtc - objPeriod.dtStartUtc).TotalDays <= 0
        ? "n/a" : (objPeriod.lngNotes / (objPeriod.dtEndUtc - objPeriod.dtStartUtc).TotalDays).ToString("0.00", CultureInfo.InvariantCulture);
    private static string Date(DateOnly dtValue) => dtValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Number(long lngValue) => lngValue.ToString(CultureInfo.InvariantCulture);
    private static string StatusText(RecordStatus enStatus, bool bArchived) => enStatus + (bArchived ? " (Archived)" : "");
    private void NotifyPresentation()
    {
        foreach (var strProperty in new[] { nameof(strActiveContacts), nameof(strInactiveContacts), nameof(strRetiredContacts), nameof(strDeceasedContacts),
            nameof(strActiveOrganizations), nameof(strArchivedOrganizations), nameof(bNoSkills), nameof(bNoMovement), nameof(bSelectOrganization),
            nameof(bNoMembers), nameof(strShownContactStatuses), nameof(strDays) }) Notify(strProperty);
    }
}
