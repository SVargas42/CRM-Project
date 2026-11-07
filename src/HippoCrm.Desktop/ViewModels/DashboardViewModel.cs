using HippoCrm.Core;

namespace HippoCrm.Desktop.ViewModels;

// Dashboard presentation state; Overview uses the workspace's already-authorized totals.
public class DashboardViewModel : ShellPageViewModel
{
    private int iCurrentTab;
    private readonly Func<Dashboard?> fnOverview;
    public MasterSearchViewModel vmMasterSearch { get; }
    public AnalyticsViewModel vmAnalytics { get; }
    public DashboardViewModel(Func<Task<MasterSearchSnapshot?>>? fnLoad = null, Func<Dashboard?>? fnOverview = null,
        Func<bool>? fnCanRead = null, Func<CancellationToken, Task<bool>>? fnRevalidate = null, Func<CancellationToken>? fnToken = null, Action<int>? actPage = null,
        Func<CancellationToken, Task<AnalyticsSnapshot?>>? fnAnalytics = null, Func<RecordKind, long, Task>? fnOpenAnalytics = null)
        : base("Dashboard", "Overview, analytics and master search")
    {
        vmMasterSearch = new MasterSearchViewModel(fnLoad ?? (() => Task.FromResult<MasterSearchSnapshot?>(null)), fnCanRead, fnRevalidate, fnToken, actPage);
        this.fnOverview = fnOverview ?? (() => null);
        vmAnalytics = new AnalyticsViewModel(fnAnalytics ?? (_ => Task.FromResult<AnalyticsSnapshot?>(null)), fnCanRead, fnToken, fnOpenAnalytics);
    }
    public int iSelectedTab
    {
        get => iCurrentTab;
        set
        {
            if (value is >= 0 and <= 2 && Set(ref iCurrentTab, value))
            { if (value == 2) vmMasterSearch.EnsureLoaded(); else if (value == 1) vmAnalytics.EnsureLoaded(); }
        }
    }

    public int iContacts => fnOverview()?.Contacts ?? 0;
    public int iOrganizations => fnOverview()?.Organizations ?? 0;
    public int iAvailable => fnOverview()?.Available ?? 0;
    public int iArchived => fnOverview()?.Archived ?? 0;
    public int iInteractions => fnOverview()?.Interactions30Days ?? 0;
    public int iOverdue => fnOverview()?.Overdue ?? 0;
    public IReadOnlyList<ActivityRowViewModel> lstActivity { get; private set; } = [];
    public bool bHasActivity => lstActivity.Count > 0;

    internal void Refresh()
    {
        var lstWeeks = fnOverview()?.Activity ?? [];
        var iMaximum = Math.Max(1, lstWeeks.Select(objWeek => objWeek.Count).DefaultIfEmpty(0).Max());
        lstActivity = lstWeeks.Select(objWeek => new ActivityRowViewModel(objWeek.Label,
            objWeek.Count, (int)Math.Round(100.0 * objWeek.Count / iMaximum))).ToArray();
        Notify(nameof(iContacts)); Notify(nameof(iOrganizations)); Notify(nameof(iAvailable));
        Notify(nameof(iArchived)); Notify(nameof(iInteractions)); Notify(nameof(iOverdue));
        Notify(nameof(lstActivity)); Notify(nameof(bHasActivity));
    }

    internal void Reset()
    {
        iSelectedTab = 0; vmMasterSearch.Reset(); vmAnalytics.Reset();
    }
}
