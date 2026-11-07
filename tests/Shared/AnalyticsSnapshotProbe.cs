using HippoCrm.Core;

// TEST ONLY: referenced solely by the two isolated policy-runner projects.
internal static class AnalyticsSnapshotProbe
{
    internal static Action<AnalyticsSnapshot>? actCapture;
    internal static void Capture(AnalyticsSnapshot objSnapshot) => actCapture?.Invoke(objSnapshot);
}
