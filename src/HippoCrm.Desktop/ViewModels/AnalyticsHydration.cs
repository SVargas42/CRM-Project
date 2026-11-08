using HippoCrm.Core;

namespace HippoCrm.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task<AnalyticsSnapshot?> LoadAnalyticsAsync(CancellationToken ctRequest)
    {
        AnalyticsSnapshot? objStaged = null;
        var objIssuingSession = objSession; var objIssuingLease = objCuiLease; var objIssuingCrm = objCrm;
        try
        {
            if (objIssuingSession is null || objIssuingLease is null || objIssuingCrm is null) return null;
            if (objIssuingLease.NeedsRefresh && !await ValidateCuiScopeAsync(ctRequest)) return null;
            var lngDatabaseEpoch = objIssuingLease.DatabaseEpoch; var lngLocalEpoch = CuiAuthorizationEpoch.Current;
            objStaged = await HydrationReadAsync(objIssuingCrm.Analytics, ctRequest);
            if (!await ValidateCuiScopeAsync(ctRequest)) return null;
            ctRequest.ThrowIfCancellationRequested();
            if (!ReferenceEquals(objSession, objIssuingSession) || !ReferenceEquals(objCuiLease, objIssuingLease)
                || !ReferenceEquals(objCrm, objIssuingCrm) || !objIssuingLease.IsCurrent(objIssuingSession)
                || objIssuingLease.NeedsRefresh || lngDatabaseEpoch != objIssuingLease.DatabaseEpoch
                || lngLocalEpoch != CuiAuthorizationEpoch.Current || !CanReadCuiCache()) return null;
            var objResult = objStaged; objStaged = null; return objResult;
        }
        finally { CuiMemoryPolicy.Release(objStaged); }
    }
}
