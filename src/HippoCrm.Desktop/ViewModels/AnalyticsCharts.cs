using System.Globalization;
using HippoCrm.Core;
namespace HippoCrm.Desktop.ViewModels;

public sealed class AnalyticsBarRow : ViewModelBase
{
    private string strLabelValue;
    private long lngCountValue;
    private long lngMaximum;
    private bool bReleased;
    private bool bVisible=>!bReleased&&fnVisible();
    private readonly Func<bool> fnVisible;
    public string strLabel=>bVisible?strLabelValue:"";
    public string strCount=>bVisible?lngCountValue.ToString(CultureInfo.InvariantCulture):"";
    public double dblPercent=>bVisible?100.0*lngCountValue/Math.Max(1,lngMaximum):0;
    public string strAccessible=>bVisible?strLabel+": "+strCount:"";
    public AnalyticsBarRow(string strLabel,long lngCount,long lngMaximum,Func<bool> fnVisible)
    { strLabelValue=strLabel;lngCountValue=lngCount;this.lngMaximum=Math.Max(1,lngMaximum);this.fnVisible=fnVisible; }
    public void Release()
    { bReleased=true;strLabelValue="";lngCountValue=0;lngMaximum=0;foreach(var strName in new[]{nameof(strLabel),nameof(strCount),nameof(dblPercent),nameof(strAccessible)}) Notify(strName); }
}
public sealed partial class AnalyticsViewModel
{
    private readonly List<AnalyticsBarRow> lstCurrentActivity=[];
    private readonly List<AnalyticsBarRow> lstCurrentOrganizations=[];
    private readonly List<AnalyticsBarRow> lstCurrentLifecycle=[];
    public IReadOnlyList<AnalyticsBarRow> lstActivityBars=>bHasData?lstCurrentActivity:[];
    public IReadOnlyList<AnalyticsBarRow> lstOrganizationBars=>bHasData?lstCurrentOrganizations:[];
    public IReadOnlyList<AnalyticsBarRow> lstLifecycleBars=>bHasData?lstCurrentLifecycle:[];
    public string strChartScope=>bHasData?(bIncludeInactive?"All authorized lifecycle states, including archived records":"Active, unarchived contacts only"):"";
    private void ClearCharts()
    {
        foreach(var vmRow in lstCurrentActivity.Concat(lstCurrentOrganizations).Concat(lstCurrentLifecycle)) vmRow.Release();
        lstCurrentActivity.Clear();lstCurrentOrganizations.Clear();lstCurrentLifecycle.Clear();
        NotifyCharts();
    }
    private void RebuildCharts(AnalyticsSnapshot objData)
    {
        ClearCharts(); bool Visible()=>bHasData;
        var arrDays=objData.DailyActivity.Where(objRow=>objRow.bActiveOnly==!bIncludeInactive).OrderBy(objRow=>objRow.dtDay).ToArray();
        var lngDayMaximum=arrDays.Select(objRow=>objRow.lngNotes).DefaultIfEmpty(1).Max();
        lstCurrentActivity.AddRange(arrDays.Select(objRow=>new AnalyticsBarRow(objRow.dtDay.ToString("MMM dd",CultureInfo.InvariantCulture),objRow.lngNotes,lngDayMaximum,Visible)));
        var arrOrganizations=objData.OrganizationRoster.Where(objRow=>bIncludeInactive||!objRow.bArchived).OrderByDescending(objRow=>objRow.lngActiveContacts).ThenBy(objRow=>objRow.strName,StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        var lngOrgMaximum=arrOrganizations.Select(objRow=>objRow.lngActiveContacts).DefaultIfEmpty(1).Max();
        lstCurrentOrganizations.AddRange(arrOrganizations.Select(objRow=>new AnalyticsBarRow(objRow.strName+(objRow.bArchived?" (archived)":""),objRow.lngActiveContacts,lngOrgMaximum,Visible)));
        var arrTotals=Enum.GetValues<RecordStatus>().Select(enStatus=>(enStatus,lngCount:objData.ContactTotals.Where(objRow=>objRow.enStatus==enStatus && (bIncludeInactive||(!objRow.bArchived&&enStatus==RecordStatus.Active))).Sum(objRow=>objRow.lngCount))).ToArray();
        var lngTotalMaximum=arrTotals.Select(objRow=>objRow.lngCount).DefaultIfEmpty(1).Max();
        lstCurrentLifecycle.AddRange(arrTotals.Select(objRow=>new AnalyticsBarRow(objRow.enStatus.ToString(),objRow.lngCount,lngTotalMaximum,Visible)));
        NotifyCharts();
    }
    private void NotifyCharts()
    { foreach(var strName in new[]{nameof(lstActivityBars),nameof(lstOrganizationBars),nameof(lstLifecycleBars),nameof(strChartScope)}) Notify(strName); }
}
