using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Windows.Input;
using HippoCrm.Core;
using Microsoft.Data.Sqlite;

namespace HippoCrm.Desktop.ViewModels;

// Application state and workflows live here. This class has no Avalonia/control dependency.
// Commands intentionally remain synchronous until the separately approved Step 3.
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly Database? objDatabase;
    private readonly AuthService? objAuthentication;
    private CrmService? objCrm;
    private Session? objSession;
    private string strCurrentPage = "Login";
    private string strCurrentMessage = "";
    private Contact? objCurrentContact;
    private Organization? objCurrentOrganization;
    private bool bCurrentDark;
    private DateTimeOffset dtLastActivity = DateTimeOffset.UtcNow;

    public string strPage { get => strCurrentPage; private set => Set(ref strCurrentPage, value); }
    public string strMessage { get => strCurrentMessage; private set => Set(ref strCurrentMessage, value); }
    public string strUsername { get; set; } = "";
    public SecureString secPassword { get; private set; } = new();
    public SecureString secConfirmation { get; private set; } = new();
    public bool bCanCreate { get; private set; }
    public bool bCanEditContact { get; private set; }
    public bool bCanEditOrganization { get; private set; }
    public string strSignedInUsername => objSession?.Username ?? "";
    public bool bSetup { get; private set; }
    public bool bDark { get => bCurrentDark; set => Set(ref bCurrentDark, value); }
    public string strSearch { get; set; } = "";
    public string strSkill { get; set; } = "";
    public int iAvailability { get; set; }
    public bool bArchived { get; set; }
    public Dashboard? objDashboard { get; private set; }
    public IReadOnlyList<Contact> lstContacts { get; private set; } = [];
    public IReadOnlyList<Organization> lstOrganizations { get; private set; } = [];
    public IReadOnlyList<Organization> lstEmployers { get; private set; } = [];
    public IReadOnlyList<Interaction> lstInteractions { get; private set; } = [];
    public IReadOnlyList<EmploymentRowViewModel> lstEmployment { get; private set; } = [];
    public IReadOnlyList<Contract> lstContracts { get; private set; } = [];
    public Organization? objEmployer { get; set; }
    public string strNote { get; set; } = "";
    public string strRole { get; set; } = "";
    public string strStartDate { get; set; } = Today();
    public string strEndDate { get; set; } = "";
    public string strContractDate { get; set; } = Today();
    public string strContractValue { get; set; } = "";
    public string strLocation { get; set; } = "";
    public string strDescription { get; set; } = "";
    public Contact? objContact
    {
        get => objCurrentContact;
        set => Run(() =>
        {
            objCurrentContact = value;
            bCanEditContact = value is not null && Crm.CanWriteContact(value.Id);
            ClearHistoryInputs();
            LoadHistory();
            Notify(nameof(objContact));
        });
    }
    public Organization? objOrganization
    {
        get => objCurrentOrganization;
        set => Run(() =>
        {
            objCurrentOrganization = value;
            bCanEditOrganization = value is not null && Crm.CanWriteOrganization(value.Id);
            Notify(nameof(objOrganization));
        });
    }

    public ICommand cmdSignIn { get; }
    public ICommand cmdLock { get; }
    public ICommand cmdOverview { get; }
    public ICommand cmdContacts { get; }
    public ICommand cmdOrganizations { get; }
    public ICommand cmdApplyFilters { get; }
    public ICommand cmdNewContact { get; }
    public ICommand cmdNewOrganization { get; }
    public ICommand cmdSaveContact { get; }
    public ICommand cmdArchiveContact { get; }
    public ICommand cmdSaveOrganization { get; }
    public ICommand cmdArchiveOrganization { get; }
    public ICommand cmdAddInteraction { get; }
    public ICommand cmdAddEmployment { get; }
    public ICommand cmdAddContract { get; }

    public MainWindowViewModel(Database? objDatabase = null)
    {
        cmdSignIn = Command(SignIn);
        cmdLock = Command(Lock);
        cmdOverview = Command(() => Navigate("Overview"));
        cmdContacts = Command(() => Navigate("Contacts"));
        cmdOrganizations = Command(() => Navigate("Organizations"));
        cmdApplyFilters = Command(RefreshLists);
        cmdNewContact = Command(() => objContact = new Contact());
        cmdNewOrganization = Command(() => objOrganization = new Organization());
        cmdSaveContact = Command(() => SaveContact(false));
        cmdArchiveContact = Command(() => SaveContact(true));
        cmdSaveOrganization = Command(() => SaveOrganization(false));
        cmdArchiveOrganization = Command(() => SaveOrganization(true));
        cmdAddInteraction = Command(() =>
        {
            Crm.AddInteraction(SelectedContact().Id, strNote);
            strNote = "";
            LoadHistory(); Notify(nameof(objContact));
            strMessage = "Interaction recorded.";
        });
        cmdAddEmployment = Command(() =>
        {
            if (objEmployer is null) throw new ArgumentException("Select an organization.");
            Crm.AddEmployment(SelectedContact().Id, objEmployer.Id, strRole, ParseDate(strStartDate),
                string.IsNullOrWhiteSpace(strEndDate) ? null : ParseDate(strEndDate));
            strRole = ""; strEndDate = "";
            LoadHistory(); Notify(nameof(objContact));
            strMessage = "Placement added.";
        });
        cmdAddContract = Command(() =>
        {
            if (!decimal.TryParse(strContractValue, NumberStyles.Number, CultureInfo.CurrentCulture, out var decValue))
                throw new ArgumentException("Enter a valid contract value.");
            Crm.AddContract(SelectedContact().Id, ParseDate(strContractDate), decValue, strLocation, strDescription);
            strContractValue = ""; strLocation = ""; strDescription = "";
            LoadHistory(); Notify(nameof(objContact));
            strMessage = "Contract added.";
        });
        try
        {
            this.objDatabase = objDatabase ?? new Database(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BigEagleGroup", "HippoCrm", "crm.db"));
            objAuthentication = new AuthService(this.objDatabase);
            bSetup = objAuthentication.NeedsSetup;
        }
        catch (InvalidOperationException objException) { StartupFailed(objException.Message); }
        catch (Exception) { StartupFailed("The encrypted workspace could not be opened. Check local access and contact your administrator."); }
    }

    private CrmService Crm => objCrm ?? throw new InvalidOperationException("Sign in to continue.");
    private Contact SelectedContact() => objContact ?? throw new ArgumentException("Select a contact.");
    private ICommand Command(Action actExecute) => new RelayCommand(() => Run(actExecute));

    private void StartupFailed(string strError) { strPage = "Error"; strMessage = strError; }

    private void SignIn()
    {
        try
        {
            if (objAuthentication is null || objDatabase is null) throw new InvalidOperationException("The workspace is unavailable.");
            if (bSetup && !PasswordBuffer.Matches(secPassword, secConfirmation)) throw new ArgumentException("Passwords do not match.");
            objSession = bSetup ? objAuthentication.CreateAdministrator(strUsername, secPassword) : objAuthentication.SignIn(strUsername, secPassword);
            bSetup = false;
            objCrm = new CrmService(objDatabase, objSession);
            bCanCreate = objCrm.Role == UserRole.Administrator;
            RecordActivity(); Navigate("Overview");
            Notify(nameof(strSignedInUsername));
        }
        catch
        {
            var objFailedSession = objSession;
            objSession = null; objCrm = null; bCanCreate = false;
            if (objFailedSession is not null)
            {
                try { objAuthentication?.SignOut(objFailedSession); }
                catch (Exception objException) when (objException is InvalidOperationException or UnauthorizedAccessException or IOException or CryptographicException or SqliteException) { }
                finally { objFailedSession.Dispose(); }
            }
            throw;
        }
        finally { ClearPasswords(); }
    }

    private void ClearPasswords()
    {
        secPassword.Dispose(); secConfirmation.Dispose();
        secPassword = new SecureString(); secConfirmation = new SecureString();
        Notify(nameof(secPassword)); Notify(nameof(secConfirmation));
    }

    public void RecordActivity() => dtLastActivity = DateTimeOffset.UtcNow;
    public void CheckInactivity()
    {
        if (objSession is not null && DateTimeOffset.UtcNow - dtLastActivity > TimeSpan.FromMinutes(15))
            Run(() => { Lock(); strMessage = "Session locked after 15 minutes of inactivity."; });
    }

    private void Lock()
    {
        var objPreviousSession = objSession;
        objSession = null; objCrm = null;
        bCanCreate = false; bCanEditContact = false; bCanEditOrganization = false;
        objCurrentContact = null; objCurrentOrganization = null;
        lstContacts = []; lstOrganizations = []; lstEmployers = [];
        lstInteractions = []; lstEmployment = []; lstContracts = []; objDashboard = null;
        objEmployer = null; strSearch = ""; strSkill = "";
        strUsername = "";
        ClearHistoryInputs();
        // Obscure the workspace before any database-dependent setup query can fail.
        bSetup = false;
        strPage = "Login";
        ClearPasswords();
        if (objPreviousSession is not null)
        {
            try { objAuthentication?.SignOut(objPreviousSession); }
            finally { objPreviousSession.Dispose(); }
        }
        if (objAuthentication is not null) bSetup = objAuthentication.NeedsSetup;
        Notify(nameof(bSetup));
    }

    public void Close()
    {
        try { Run(Lock); }
        finally { secPassword.Dispose(); secConfirmation.Dispose(); }
    }

    private void Navigate(string strDestination)
    {
        strSearch = ""; strSkill = ""; bArchived = false; iAvailability = 0;
        objCurrentContact = null; objCurrentOrganization = null;
        if (strDestination == "Overview") objDashboard = Crm.Dashboard();
        else if (strDestination == "Contacts") lstContacts = Crm.Contacts();
        else if (strDestination == "Organizations") lstOrganizations = Crm.Organizations();
        strPage = strDestination;
        Notify(nameof(strPage));
    }

    private void RefreshLists()
    {
        if (strPage == "Contacts")
        {
            lstContacts = Crm.Contacts(strSearch, strSkill,
                iAvailability == 0 ? null : (Availability)(iAvailability - 1), bArchived);
            Notify(nameof(lstContacts));
        }
        else
        {
            lstOrganizations = Crm.Organizations(strSearch, bArchived);
            Notify(nameof(lstOrganizations));
        }
    }

    private void SaveContact(bool bToggleArchive)
    {
        var objRecord = SelectedContact();
        var bOldArchived = objRecord.Archived;
        if (bToggleArchive) objRecord.Archived = !bOldArchived;
        try { Crm.Save(objRecord); }
        catch { objRecord.Archived = bOldArchived; throw; }
        RefreshLists();
        objContact = lstContacts.FirstOrDefault(objItem => objItem.Id == objRecord.Id);
        strMessage = "Contact saved.";
    }

    private void SaveOrganization(bool bToggleArchive)
    {
        var objRecord = objOrganization ?? throw new ArgumentException("Select an organization.");
        var bOldArchived = objRecord.Archived;
        if (bToggleArchive) objRecord.Archived = !bOldArchived;
        try { Crm.Save(objRecord); }
        catch { objRecord.Archived = bOldArchived; throw; }
        RefreshLists();
        objOrganization = lstOrganizations.FirstOrDefault(objItem => objItem.Id == objRecord.Id);
        strMessage = "Organization saved.";
    }

    private void LoadHistory()
    {
        lstInteractions = []; lstEmployment = []; lstContracts = []; lstEmployers = [];
        if (objCurrentContact is { Id: > 0 } objRecord)
        {
            lstInteractions = Crm.Interactions(objRecord.Id);
            lstContracts = Crm.Contracts(objRecord.Id);
            lstEmployers = Crm.Organizations();
            lstEmployment = Crm.EmploymentHistory(objRecord.Id).Select(objPlacement =>
                new EmploymentRowViewModel(objPlacement, Command(() => EndEmployment(objPlacement.Id)))).ToArray();
        }
    }

    private void EndEmployment(long lngEmploymentId)
    {
        var objRow = lstEmployment.Single(objItem => objItem.objEmployment.Id == lngEmploymentId);
        Crm.EndEmployment(lngEmploymentId, ParseDate(objRow.strEndDate));
        LoadHistory(); Notify(nameof(objContact)); strMessage = "Placement ended.";
    }

    private void ClearHistoryInputs()
    {
        objEmployer = null; strNote = ""; strRole = ""; strStartDate = Today(); strEndDate = "";
        strContractDate = Today(); strContractValue = ""; strLocation = ""; strDescription = "";
    }

    private void Run(Action actExecute)
    {
        strMessage = "";
        try { actExecute(); }
        catch (ArgumentException objException) { strMessage = objException.Message; }
        catch (UnauthorizedAccessException)
        {
            // Clear cached data immediately on a denied operation; logging failure must not keep it visible.
            try { Lock(); }
            catch (Exception objException) when (objException is InvalidOperationException or UnauthorizedAccessException or IOException or CryptographicException or SqliteException) { }
            strMessage = "Your access could not be verified. Sign in again.";
        }
        catch (SqliteException) { strMessage = "The local operation could not be completed. Check access and try again."; }
        catch (Exception objException) when (objException is InvalidOperationException or IOException or CryptographicException)
        { strMessage = "The workspace operation could not be completed. Contact your administrator if it persists."; }
    }

    private static string Today() => DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly ParseDate(string strText)
    {
        if (!DateOnly.TryParseExact(strText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtDate))
            throw new ArgumentException("Enter dates as YYYY-MM-DD.");
        return dtDate;
    }
}

public sealed class EmploymentRowViewModel(Employment objPlacement, ICommand cmdEndPlacement)
{
    public Employment objEmployment { get; } = objPlacement;
    public ICommand cmdEnd { get; } = cmdEndPlacement;
    public string strEndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
