using System.Reflection;
using HippoCrm.Core;
using HippoCrm.Desktop.ViewModels;
using Microsoft.Data.Sqlite;

internal static class AnalyticsViewChecks
{
    internal static void Run()
    {
        var iLoads = 0;
        AnalyticsSnapshot Snapshot()
        {
            var objResult = new AnalyticsSnapshot(DateTimeOffset.UtcNow);
            objResult.LastContacted.Add(new(1, "PRIVATE_ANALYTICS_CANARY", RecordStatus.Active, false, "", null));
            return objResult;
        }
        var vmDashboard = new DashboardViewModel(fnCanRead: () => true, fnAnalytics: _ =>
        { iLoads++; return Task.FromResult<AnalyticsSnapshot?>(Snapshot()); });
        using var vmAnalytics = vmDashboard.vmAnalytics;
        Check(iLoads == 0, "Analytics is not loaded with Overview");
        vmDashboard.iSelectedTab = 1; Wait(vmAnalytics.tskLoad);
        Check(iLoads == 1 && vmAnalytics.objSnapshot is not null && vmAnalytics.strLoadedAt.EndsWith(" UTC", StringComparison.Ordinal),
            "First Analytics selection loads one snapshot and shows its UTC timestamp");
        vmAnalytics.strFindSearch = "planning"; vmAnalytics.strFindSearch = "";
        vmDashboard.iSelectedTab = 0; vmDashboard.iSelectedTab = 1;
        Check(iLoads == 1, "Analytics in-memory filters and a cached tab revisit do not reload");
        var objOld = vmAnalytics.objSnapshot!;
        Wait(vmAnalytics.RefreshAsync());
        Check(iLoads == 2 && objOld.LastContacted.Count == 0 && vmAnalytics.objSnapshot is not null,
            "Analytics Refresh releases the previous snapshot and loads again");

        var objCompletion = new TaskCompletionSource<AnalyticsSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var vmDelayed = new AnalyticsViewModel(_ => objCompletion.Task, () => true);
        vmDelayed.EnsureLoaded();
        Check(vmDelayed.bLoading && vmDelayed.objSnapshot is null && !vmDelayed.cmdRefresh.CanExecute(null),
            "Analytics loading is local and Refresh is disabled before private data arrives");
        vmDelayed.Reset();
        var objLate = Snapshot(); objCompletion.SetResult(objLate); Wait(vmDelayed.tskLoad);
        Check(vmDelayed.objSnapshot is null && objLate.LastContacted.Count == 0,
            "Canceled Analytics load cannot publish and releases a late result");
        var iAttempt = 0;
        using var vmFailure = new AnalyticsViewModel(_ => ++iAttempt == 1 ? Task.FromResult<AnalyticsSnapshot?>(Snapshot())
            : Task.FromException<AnalyticsSnapshot?>(new InvalidOperationException("PRIVATE_ANALYTICS_CANARY")), () => true);
        vmFailure.EnsureLoaded(); Wait(vmFailure.tskLoad); objOld = vmFailure.objSnapshot!;
        Wait(vmFailure.RefreshAsync());
        Check(vmFailure.objSnapshot is null && objOld.LastContacted.Count == 0 && vmFailure.bFailed
            && vmFailure.strError == "Analytics could not be loaded. Please reload." && vmFailure.cmdRefresh.CanExecute(null),
            "Analytics failure clears the old snapshot and exposes only a neutral Reload error");
        using var vmDenied = new AnalyticsViewModel(_ => Task.FromResult<AnalyticsSnapshot?>(Snapshot()), () => false);
        vmDenied.EnsureLoaded(); Wait(vmDenied.tskLoad);
        Check(vmDenied.objSnapshot is null && vmDenied.bFailed, "Analytics cannot hydrate when presentation authorization is absent");
    }

    internal static void Workspace(MainWindowViewModel vmMain, Database objDatabase, Action actChangeEpoch)
    {
        var vmDashboard = (DashboardViewModel)vmMain.vmShell.vmCurrentPage;
        Check(vmDashboard.vmAnalytics.objSnapshot is null, "Real Debug workspace leaves Analytics unloaded on sign-in");
        var lngBefore = AuditCount(objDatabase);
        vmDashboard.iSelectedTab = 1; Wait(vmDashboard.vmAnalytics.tskLoad);
        var lngFirst = AuditCount(objDatabase) - lngBefore;
        var objLoaded = vmDashboard.vmAnalytics.objSnapshot;
        Check(objLoaded is not null && !vmMain.bBusy && lngFirst is >= 5 and <= 12,
            "Real Analytics load keeps global busy clear and audited authorization cost bounded");
        lngBefore = AuditCount(objDatabase);
        vmDashboard.vmAnalytics.strFindSearch = "planning"; vmDashboard.vmAnalytics.strFindSearch = "";
        Check(AuditCount(objDatabase) == lngBefore, "Real Analytics in-memory filtering adds no protected reads");
        Wait(vmDashboard.vmAnalytics.RefreshAsync());
        var lngRefresh = AuditCount(objDatabase) - lngBefore;
        Check(lngRefresh is >= 5 and <= 12 && objLoaded!.LastContacted.Count == 0 && vmDashboard.vmAnalytics.objSnapshot is not null,
            "Real Analytics Refresh repeats a bounded audit cost and releases its old snapshot");
        Console.WriteLine($"Analytics AccessVerified counts: first={lngFirst}, refresh={lngRefresh}.");
        objLoaded = vmDashboard.vmAnalytics.objSnapshot;
        actChangeEpoch();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check(vmDashboard.vmAnalytics.objSnapshot is null && objLoaded!.LastContacted.Count == 0,
            "Real permission epoch change drops and releases Analytics immediately");
        Wait(vmMain.ProbeCuiAuthorizationAsync());
        vmDashboard.iSelectedTab = 0;
    }
    internal static AnalyticsSnapshot BeforeLock(MainWindowViewModel vmMain, AnalyticsViewModel vmAnalytics)
    {
        Wait(vmAnalytics.RefreshAsync());
        var objLoaded = vmAnalytics.objSnapshot;
        Check(objLoaded is not null && vmMain.CanReadCui(), "Analytics lock fixture holds a verified snapshot");
        return objLoaded!;
    }
    internal static void AfterLock(AnalyticsViewModel vmAnalytics, AnalyticsSnapshot objLoaded) =>
        Check(vmAnalytics.objSnapshot is null && objLoaded.LastContacted.Count == 0 && objLoaded.PeriodSummaries.Count == 0,
            "Real session lock drops and releases every Analytics result");
    private static long AuditCount(Database objDatabase)
    {
        using var dbConnection = (SqliteConnection)typeof(Database).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(objDatabase, null)!;
        using var cmdCount = dbConnection.CreateCommand(); cmdCount.CommandText = "SELECT count(*) FROM audit_log WHERE action='AccessVerified'";
        return Convert.ToInt64(cmdCount.ExecuteScalar());
    }
    private static void Wait(Task tskWork) => ContactRoutingChecks.WaitForTask(tskWork);
    private static void Check(bool bCondition, string strDescription)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription); Console.WriteLine("PASS " + strDescription); }
}
