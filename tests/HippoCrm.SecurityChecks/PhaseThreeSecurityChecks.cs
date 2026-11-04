using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class PhaseThreeSecurityChecks
{
    internal static void Run()
    {
        using var objDatabase = new Database();
        var objClock = new ManualSessionClock();
        var objAuth = new AuthService(objDatabase, objClock);
        using var secPassword = TestPasswords.Secret("FICTIONAL Phase Three password!");
        using var objAdmin = objAuth.CreateAdministrator("phase-three", secPassword);
        var objUsers = new UserAdministrationService(objDatabase, objAdmin);
        objUsers.SetCuiAuthorization(objAdmin.UserId, true);
        var objCrm = new CrmService(objDatabase, objAdmin);
        objCrm.Save(new Contact { FirstName = "FICTIONAL current" });
        objClock.lngTicks += Session.InactivityLimit.Ticks; // Leave the timer queued to exercise the service boundary itself.
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => objCrm.Contacts(), "Service rejects a session at the exact inactivity deadline");
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => objCrm.Save(new Contact { FirstName = "Rejected" }), "Expired session cannot mutate through a previously created service");
        SecurityPolicyChecks.Reject<UnauthorizedAccessException>(() => objUsers.Users(), "User administration rejects the same expired session");
        SecurityPolicyChecks.Assert(!objAdmin.TryRecordActivity(), "Late input cannot revive a session rejected by the service");
        objAuth.SignOut(objAdmin);
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM sessions WHERE status='Active'") == 0,
            "Expired-session cleanup revokes the database credential");

        using var objActive = objAuth.SignIn("phase-three", secPassword);
        var objActiveCrm = new CrmService(objDatabase, objActive);
        var lngBefore = SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM contacts");
        objDatabase.objCheckpoint.iFailWrite = objDatabase.objCheckpoint.iWrites + 1;
        SecurityPolicyChecks.Reject<IOException>(() => objActiveCrm.Save(new Contact { FirstName = "Prepare failure" }), "A trusted prepare failure blocks the business transaction");
        SecurityPolicyChecks.Assert(SecurityPolicyChecks.Scalar(objDatabase, "SELECT count(*) FROM contacts") == lngBefore, "Prepare failure rolls back the business mutation and audit together");
        objDatabase.objCheckpoint.iFailWrite = objDatabase.objCheckpoint.iWrites + 2;
        SecurityPolicyChecks.Reject<IOException>(() => objActiveCrm.Save(new Contact { FirstName = "Confirm interruption" }), "A failed confirmation is not reported as success");
        SecurityPolicyChecks.Assert(objDatabase.objCheckpoint.Read().objPrepared is not null, "Confirmation failure preserves its durable prepared audit head");
        SecurityPolicyChecks.Assert(objActiveCrm.Contacts().Count == lngBefore + 1 && objDatabase.objCheckpoint.Read().objPrepared is null,
            "Next protected operation verifies committed history and automatically completes confirmation");
        objDatabase.objCheckpoint.bDenyRead = true;
        SecurityPolicyChecks.Reject<IOException>(() => objActiveCrm.Contacts(), "Unavailable checkpoint state blocks service reads");
        objDatabase.objCheckpoint.bDenyRead = false;
    }
}
