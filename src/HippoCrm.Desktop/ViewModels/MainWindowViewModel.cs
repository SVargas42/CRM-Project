using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Windows.Input;
using HippoCrm.Core;
using Microsoft.Data.Sqlite;
using HippoCrm.Desktop.Utilities;

namespace HippoCrm.Desktop.ViewModels;

// Application state and workflows live here. This class has no Avalonia/control dependency.
// Database work runs on worker threads; awaited results update presentation state on the UI context.
public sealed class MainWindowViewModel : ViewModelBase
{
    private Database? objDatabase;
    private AuthService? objAuthentication;
    private readonly SemaphoreSlim semDatabaseWork = new(1, 1);
    private readonly List<WeakReference<AsyncRelayCommand>> lstAsyncCommands = [];
    private bool bCurrentBusy = true;
    private bool bCurrentLocking;
    private bool bClosed;
    public Task tskInitialization { get; }
    public Task tskPendingOperation { get; private set; } = Task.CompletedTask;
    public Task tskThemeSave { get; private set; } = Task.CompletedTask;
    public bool bBusy => bCurrentBusy;
    public bool bLocking => bCurrentLocking;
    private void SetBusy(bool bValue)
    {
        Set(ref bCurrentBusy, bValue, nameof(bBusy));
        lstAsyncCommands.RemoveAll(wrCommand => !wrCommand.TryGetTarget(out _));
        foreach (var wrCommand in lstAsyncCommands)
            if (wrCommand.TryGetTarget(out var cmdAsync)) cmdAsync.Refresh();
    }
    private CrmService? objCrm;
    private Session? objSession;
    private string strCurrentPage = "Login";
    private string strCurrentMessage = "";
    private Contact? objCurrentContact;
    private Organization? objCurrentOrganization;
    private bool bCurrentDark;
    private bool bCurrentOrganizationLookup;
    private string strCurrentOrganizationSearch = "";
    private OrganizationOptionViewModel? objCurrentOrganizationToAdd;
    private DateTimeOffset dtLastActivity = DateTimeOffset.UtcNow;

    public string strPage { get => strCurrentPage; private set => Set(ref strCurrentPage, value); }
    public string strMessage { get => strCurrentMessage; private set => Set(ref strCurrentMessage, value); }
    public string strUsername { get; set; } = "";
    public SecureString secPassword { get; private set; } = new();
    public SecureString secConfirmation { get; private set; } = new();
    public bool bCanCreate { get; private set; }
    public bool bCanEditContact { get; private set; }
    public bool bSavedContact => objCurrentContact is { Id: > 0 };
    public bool bCanEditContactHistory => bCanEditContact && objCurrentContact is { Archived: false };
    public bool bCanEditOrganization { get; private set; }
    public string strSignedInUsername => objSession?.Username ?? "";
    public bool bSetup { get; private set; }
    public ShellViewModel vmShell { get; }
    public bool bDark
    {
        get => bCurrentDark;
        set
        {
            if (Set(ref bCurrentDark, value) && objDatabase is not null && !bClosed)
                tskThemeSave = SaveThemeAsync(tskThemeSave, value);
        }
    }
    public string strSearch { get; set; } = "";
    public string strSkill { get; set; } = "";
    public int iAvailability { get; set; }
    public bool bArchived { get; set; }
    public Dashboard? objDashboard { get; private set; }
    public IReadOnlyList<Contact> lstContacts { get; private set; } = [];
    public IReadOnlyList<Organization> lstOrganizations { get; private set; } = [];
    public IReadOnlyList<Organization> lstEmployers { get; private set; } = [];
    public IReadOnlyList<OrganizationOptionViewModel> lstContactOrganizations { get; private set; } = [];
    public IReadOnlyList<OrganizationSelectionViewModel> lstOrganizationSelections { get; private set; } = [];
    public IReadOnlyList<OrganizationSelectionViewModel> lstAddedOrganizations => lstOrganizationSelections.Where(vmOption => vmOption.bSelected).ToArray();
    public Task<IEnumerable<object>> tskOrganizationSearch { get; private set; } = Task.FromResult(Enumerable.Empty<object>());
    public Func<string, CancellationToken, Task<IEnumerable<object>>> fnPopulateOrganizations =>
        (strQuery, ctCancellation) => tskOrganizationSearch = PopulateOrganizationsAsync(strQuery, ctCancellation);

    private async Task<IEnumerable<object>> PopulateOrganizationsAsync(string strQuery, CancellationToken ctCancellation)
    {
        if (!bOrganizationLookup || !bCanEditContact || objCurrentContact is null) return [];
        var objSearchContact = objCurrentContact;
        var setAssigned = objSearchContact.lstOrganizationIds.ToHashSet();
        var arrAvailable = lstContactOrganizations.Where(vmOption => vmOption.lngOrganizationId.HasValue
            && !setAssigned.Contains(vmOption.lngOrganizationId.Value)).ToArray();
        var strTerm = (strQuery ?? "").Trim();
        var arrMatches = await Task.Run(() =>
        {
            var strSearchCode = SoundexHelper.GetSoundexCode(strTerm);
            return arrAvailable.Where(vmOption => vmOption.strName.Contains(strTerm, StringComparison.OrdinalIgnoreCase)
                || (strSearchCode.Length != 0 && SoundexHelper.GetSoundexCode(vmOption.strName) == strSearchCode))
                .Cast<object>().ToArray();
        }, ctCancellation);
        ctCancellation.ThrowIfCancellationRequested();
        // Drop results belonging to a lookup that was closed, locked, or switched to another contact.
        return bOrganizationLookup && !bLocking && ReferenceEquals(objSearchContact, objCurrentContact)
            ? arrMatches : [];
    }
    public IReadOnlyList<OrganizationOptionViewModel> lstAvailableOrganizations
    {
        get
        {
            var strTerm = strOrganizationSearch.Trim();
            var strSearchCode = SoundexHelper.GetSoundexCode(strTerm);
            return lstContactOrganizations.Where(vmOption => vmOption.lngOrganizationId.HasValue
                && objCurrentContact?.lstOrganizationIds.Contains(vmOption.lngOrganizationId.Value) != true
                && (vmOption.strName.Contains(strTerm, StringComparison.OrdinalIgnoreCase)
                    || (strSearchCode.Length != 0 && SoundexHelper.GetSoundexCode(vmOption.strName) == strSearchCode))).ToArray();
        }
    }
    public bool bOrganizationLookup { get => bCurrentOrganizationLookup; private set => Set(ref bCurrentOrganizationLookup, value); }
    public string strOrganizationSearch
    {
        get => strCurrentOrganizationSearch;
        set
        {
            if (!Set(ref strCurrentOrganizationSearch, value ?? "")) return;
            // AutoCompleteBox updates the text when a suggestion is selected; retain that staged selection.
            if (!string.Equals(objOrganizationToAdd?.strName, strOrganizationSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                objOrganizationToAdd = null;
            Notify(nameof(lstAvailableOrganizations));
        }
    }
    public OrganizationOptionViewModel? objOrganizationToAdd
    {
        get => objCurrentOrganizationToAdd;
        set { if (Set(ref objCurrentOrganizationToAdd, value)) Notify(nameof(bCanAddOrganization)); }
    }
    public bool bCanAddOrganization => bCanEditContact && objOrganizationToAdd is not null
        && lstAvailableOrganizations.Contains(objOrganizationToAdd);
    public OrganizationOptionViewModel? objContactOrganization
    {
        get => lstContactOrganizations.FirstOrDefault(vmOption => vmOption.lngOrganizationId == objCurrentContact?.lngOrganizationId);
        set
        {
            if (objCurrentContact is null || !bCanEditContact || value is null || !lstContactOrganizations.Contains(value)) return;
            objCurrentContact.lngOrganizationId = value.lngOrganizationId;
            objCurrentContact.lstOrganizationIds = value.lngOrganizationId is { } lngOrganization ? [lngOrganization] : [];
            RefreshOrganizationSelections();
            Notify(nameof(objContactOrganization)); Notify(nameof(strContactOrganizationName));
        }
    }
    public string strContactOrganizationName => objCurrentContact is null ? "No organization assigned" : ContactOrganizationNames(objCurrentContact);
    public string ContactOrganizationNames(Contact objContact)
    {
        var lstAssigned = objContact.lstOrganizationIds;
        return lstAssigned.Count == 0 ? "No organization assigned" : string.Join("; ", lstAssigned.Select(lngOrganization =>
            lstContactOrganizations.FirstOrDefault(vmOption => vmOption.lngOrganizationId == lngOrganization)?.strName ?? "Organization unavailable"));
    }

    private void RefreshOrganizationSelections()
    {
        lstOrganizationSelections = lstContactOrganizations.Where(vmOption => vmOption.lngOrganizationId.HasValue)
            .Select(vmOption => new OrganizationSelectionViewModel(vmOption.lngOrganizationId!.Value, vmOption.strName,
                objCurrentContact?.lstOrganizationIds.Contains(vmOption.lngOrganizationId.Value) == true,
                bCanEditContact, SelectContactOrganization)).ToArray();
        Notify(nameof(lstOrganizationSelections));
        bOrganizationLookup = false; strOrganizationSearch = ""; objOrganizationToAdd = null;
        Notify(nameof(lstAddedOrganizations)); Notify(nameof(lstAvailableOrganizations)); Notify(nameof(bCanAddOrganization));
    }

    private void SelectContactOrganization(long lngOrganization, bool bSelected)
    {
        if (objCurrentContact is null || !bCanEditContact) return;
        if (bSelected && !objCurrentContact.lstOrganizationIds.Contains(lngOrganization)) objCurrentContact.lstOrganizationIds.Add(lngOrganization);
        else if (!bSelected) objCurrentContact.lstOrganizationIds.Remove(lngOrganization);
        objCurrentContact.lngOrganizationId = objCurrentContact.lstOrganizationIds.Count == 0 ? null : objCurrentContact.lstOrganizationIds[0];
        Notify(nameof(objContactOrganization)); Notify(nameof(strContactOrganizationName));
        objOrganizationToAdd = null;
        Notify(nameof(lstAddedOrganizations)); Notify(nameof(lstAvailableOrganizations)); Notify(nameof(bCanAddOrganization));
    }

    private void ShowOrganizationLookup()
    {
        if (!bCanEditContact) return;
        strOrganizationSearch = ""; objOrganizationToAdd = null; bOrganizationLookup = true;
    }

    private void AddContactOrganization()
    {
        if (!bCanAddOrganization || objOrganizationToAdd?.lngOrganizationId is not { } lngOrganization) return;
        var vmSelection = lstOrganizationSelections.FirstOrDefault(vmOption => vmOption.lngOrganizationId == lngOrganization);
        if (vmSelection is null) return;
        vmSelection.bSelected = true;
        bOrganizationLookup = false;
    }
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
        set => _ = SelectContactAsync(value);
    }
    public Organization? objOrganization
    {
        get => objCurrentOrganization;
        set => _ = ExecuteOperationAsync(() => SelectOrganizationCoreAsync(value));
    }

    public ICommand cmdSignIn { get; }
    public ICommand cmdLock { get; }
    public ICommand cmdOverview { get; }
    public ICommand cmdContacts { get; }
    public ICommand cmdOrganizations { get; }
    public ICommand cmdApplyFilters { get; }
    public ICommand cmdShowOrganizationLookup { get; }
    public ICommand cmdAddContactOrganization { get; }
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
        cmdSignIn = Command(SignInAsync);
        cmdLock = new AsyncRelayCommand(LockAsync);
        cmdOverview = Command(() => NavigateAsync("Overview"));
        cmdContacts = Command(() => NavigateAsync("Contacts"));
        cmdOrganizations = Command(() => NavigateAsync("Organizations"));
        cmdApplyFilters = Command(RefreshListsAsync);
        cmdShowOrganizationLookup = new RelayCommand(ShowOrganizationLookup);
        cmdAddContactOrganization = new RelayCommand(AddContactOrganization);
        cmdNewContact = Command(() => SelectContactCoreAsync(new Contact()));
        cmdNewOrganization = Command(() => SelectOrganizationCoreAsync(new Organization()));
        cmdSaveContact = Command(() => SaveContactAsync(false));
        cmdArchiveContact = Command(() => SaveContactAsync(true));
        cmdSaveOrganization = Command(() => SaveOrganizationAsync(false));
        cmdArchiveOrganization = Command(() => SaveOrganizationAsync(true));
        cmdAddInteraction = Command(async () =>
        {
            var lngContact = SelectedContact().Id; var strBody = strNote;
            await WorkAsync(() => { Crm.AddInteraction(lngContact, strBody); return true; });
            strNote = ""; Notify(nameof(strNote));
            await LoadHistoryAsync(); Notify(nameof(objContact)); strMessage = "Interaction recorded.";
        });
        cmdAddEmployment = Command(async () =>
        {
            var lngContact = SelectedContact().Id;
            var lngEmployer = objEmployer?.Id ?? throw new ArgumentException("Select an organization.");
            var strPlacementRole = strRole; var dtStart = ParseDate(strStartDate);
            var dtEnd = string.IsNullOrWhiteSpace(strEndDate) ? (DateOnly?)null : ParseDate(strEndDate);
            await WorkAsync(() => { Crm.AddEmployment(lngContact, lngEmployer, strPlacementRole, dtStart, dtEnd); return true; });
            strRole = ""; strEndDate = ""; Notify(nameof(strRole)); Notify(nameof(strEndDate));
            await LoadHistoryAsync(); Notify(nameof(objContact)); strMessage = "Placement added.";
        });
        cmdAddContract = Command(async () =>
        {
            if (!decimal.TryParse(strContractValue, NumberStyles.Number, CultureInfo.CurrentCulture, out var decValue))
                throw new ArgumentException("Enter a valid contract value.");
            var lngContact = SelectedContact().Id; var dtDate = ParseDate(strContractDate);
            var strPlace = strLocation; var strDetails = strDescription;
            await WorkAsync(() => { Crm.AddContract(lngContact, dtDate, decValue, strPlace, strDetails); return true; });
            strContractValue = ""; strLocation = ""; strDescription = "";
            Notify(nameof(strContractValue)); Notify(nameof(strLocation)); Notify(nameof(strDescription));
            await LoadHistoryAsync(); Notify(nameof(objContact)); strMessage = "Contract added.";
        });
        vmShell = new ShellViewModel(this);
        tskInitialization = InitializeAsync(objDatabase);
        tskPendingOperation = tskInitialization;
    }

    private async Task InitializeAsync(Database? objProvidedDatabase)
    {
        try
        {
            var objStartup = await WorkAsync(() =>
            {
                var objLocalDatabase = objProvidedDatabase ?? new Database(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BigEagleGroup", "HippoCrm", "crm.db"));
                var objLocalAuth = new AuthService(objLocalDatabase);
                return (objLocalDatabase, objLocalAuth, bSetup: objLocalAuth.NeedsSetup, bDark: objLocalDatabase.ReadDarkTheme());
            });
            objDatabase = objStartup.objLocalDatabase; objAuthentication = objStartup.objLocalAuth;
            Set(ref bCurrentDark, objStartup.bDark, nameof(bDark));
            bSetup = objStartup.bSetup; Notify(nameof(bSetup));
        }
        catch (Exception objException) when (objException is InvalidOperationException or IOException or CryptographicException or SqliteException or UnauthorizedAccessException)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"CRM initialization: {objException.GetType().Name}: {objException.Message}");
            if (objException is SqliteException objSqlite)
                System.Diagnostics.Debug.WriteLine($"SQLite code={objSqlite.SqliteErrorCode}; extended={objSqlite.SqliteExtendedErrorCode}");
#endif
            StartupFailed("The encrypted workspace could not be opened. Check local access and contact your administrator.");
        }
        finally { SetBusy(false); }
    }

    private async Task<T> WorkAsync<T>(Func<T> fnWork)
    {
        // One local database operation at a time, including preference writes.
        await semDatabaseWork.WaitAsync();
        try { return await Task.Run(fnWork); }
        finally { semDatabaseWork.Release(); }
    }

    private async Task SaveThemeAsync(Task tskPrevious, bool bSavedDark)
    {
        await tskPrevious;
        try { await WorkAsync(() => { objDatabase!.SaveDarkTheme(bSavedDark); return true; }); }
        catch (Exception objException) when (objException is IOException or SqliteException or UnauthorizedAccessException or CryptographicException or InvalidOperationException)
        { strMessage = "The theme preference could not be saved locally. Check local access and try again."; }
    }

    public Task SelectContactAsync(Contact? objValue) => ExecuteOperationAsync(() => SelectContactCoreAsync(objValue));

    private async Task SelectContactCoreAsync(Contact? objValue)
    {
        var bAllowed = objValue is not null && await WorkAsync(() => Crm.CanWriteContact(objValue.Id));
        var lstOptions = objValue is null ? [] : await WorkAsync(() => OrganizationOptions());
        objCurrentContact = objValue; bCanEditContact = bAllowed; ClearHistoryInputs();
        await LoadHistoryAsync();
        lstContactOrganizations = lstOptions; RefreshOrganizationSelections();
        Notify(nameof(lstContactOrganizations)); Notify(nameof(objContactOrganization));
        Notify(nameof(strContactOrganizationName)); Notify(nameof(bCanEditContact));
        Notify(nameof(bSavedContact)); Notify(nameof(bCanEditContactHistory)); Notify(nameof(objContact));
    }

    private OrganizationOptionViewModel[] OrganizationOptions() =>
        new[] { new OrganizationOptionViewModel(null, "No organization assigned") }
            .Concat(Crm.Organizations().Concat(Crm.Organizations(bArchived: true)).OrderBy(objItem => objItem.Name)
                .Select(objItem => new OrganizationOptionViewModel(objItem.Id, objItem.Name))).ToArray();

    private async Task SelectOrganizationCoreAsync(Organization? objValue)
    {
        var bAllowed = objValue is not null && await WorkAsync(() => Crm.CanWriteOrganization(objValue.Id));
        objCurrentOrganization = objValue; bCanEditOrganization = bAllowed;
        Notify(nameof(bCanEditOrganization)); Notify(nameof(objOrganization));
    }

    private CrmService Crm => objCrm ?? throw new InvalidOperationException("Sign in to continue.");
    private Contact SelectedContact() => objContact ?? throw new ArgumentException("Select a contact.");
    private ICommand Command(Func<Task> fnExecute)
    {
        var cmdAsync = new AsyncRelayCommand(() => ExecuteOperationAsync(fnExecute), () => !bBusy && !bLocking && !bClosed);
        lstAsyncCommands.Add(new WeakReference<AsyncRelayCommand>(cmdAsync)); return cmdAsync;
    }

    private Task ExecuteOperationAsync(Func<Task> fnExecute)
    {
        if (bBusy || bLocking || bClosed) return Task.CompletedTask;
        SetBusy(true);
        return tskPendingOperation = RunAsync(fnExecute);
    }

    private void StartupFailed(string strError) { strPage = "Error"; strMessage = strError; }

    private async Task SignInAsync()
    {
        try
        {
            if (objAuthentication is null || objDatabase is null) throw new InvalidOperationException("The workspace is unavailable.");
            using var secAttempt = secPassword.Copy();
            using var secConfirm = secConfirmation.Copy();
            var bCreate = bSetup; var strAttemptUsername = strUsername;
            var objSignedIn = await WorkAsync(() =>
            {
                if (bCreate && !PasswordBuffer.Matches(secAttempt, secConfirm)) throw new ArgumentException("Passwords do not match.");
                var objNewSession = bCreate ? objAuthentication.CreateAdministrator(strAttemptUsername, secAttempt)
                    : objAuthentication.SignIn(strAttemptUsername, secAttempt);
                try
                {
                    var objNewCrm = new CrmService(objDatabase, objNewSession);
                    return (objNewSession, objNewCrm, bCreateAllowed: objNewCrm.Role == UserRole.Administrator);
                }
                catch
                {
                    try { objAuthentication.SignOut(objNewSession); }
                    finally { objNewSession.Dispose(); }
                    throw;
                }
            });
            objSession = objSignedIn.objNewSession; objCrm = objSignedIn.objNewCrm;
            bCanCreate = objSignedIn.bCreateAllowed; bSetup = false;
            RecordActivity(); await NavigateAsync("Overview"); Notify(nameof(strSignedInUsername));
        }
        catch
        {
            var objFailedSession = objSession; objSession = null; objCrm = null; bCanCreate = false;
            if (objFailedSession is not null)
            {
                try { await WorkAsync(() => { objAuthentication?.SignOut(objFailedSession); return true; }); }
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
            _ = LockAsync();
    }

    private Task? tskLockOperation;
    private Task? tskClosing;
    private Task LockAsync()
    {
        if (bLocking) return tskLockOperation ?? Task.CompletedTask;
        // Hide the workspace immediately, even when a previously started database command is still completing.
        Set(ref bCurrentLocking, true, nameof(bLocking));
        return tskLockOperation = FinishLockAsync();
    }

    private async Task FinishLockAsync()
    {
        try
        {
            await tskInitialization;
            await tskPendingOperation;
            SetBusy(true);
            await LockCoreAsync();
        }
        catch (Exception objException) when (objException is InvalidOperationException or UnauthorizedAccessException or IOException or CryptographicException or SqliteException)
        { strMessage = "The session was locked. Check local access before signing in again."; }
        finally
        {
            Set(ref bCurrentLocking, false, nameof(bLocking));
            SetBusy(false);
        }
    }

    private async Task LockCoreAsync()
    {
        var objPreviousSession = objSession;
        objSession = null; objCrm = null;
        bCanCreate = false; bCanEditContact = false; bCanEditOrganization = false;
        objCurrentContact = null; objCurrentOrganization = null;
        lstContacts = []; lstOrganizations = []; lstEmployers = [];
        lstContactOrganizations = [];
        lstOrganizationSelections = []; Notify(nameof(lstOrganizationSelections));
        bOrganizationLookup = false; strOrganizationSearch = ""; objOrganizationToAdd = null;
        Notify(nameof(lstAddedOrganizations)); Notify(nameof(lstAvailableOrganizations)); Notify(nameof(bCanAddOrganization));
        Notify(nameof(lstContactOrganizations)); Notify(nameof(objContactOrganization)); Notify(nameof(strContactOrganizationName));
        lstInteractions = []; lstEmployment = []; lstContracts = []; objDashboard = null;
        objEmployer = null; strSearch = ""; strSkill = ""; strUsername = "";
        ClearHistoryInputs(); bSetup = false; strPage = "Login";
        Notify(nameof(objContact)); Notify(nameof(objOrganization));
        Notify(nameof(lstContacts)); Notify(nameof(lstOrganizations));
        Notify(nameof(lstInteractions)); Notify(nameof(lstEmployment)); Notify(nameof(lstContracts)); Notify(nameof(lstEmployers));
        Notify(nameof(bSavedContact)); Notify(nameof(bCanEditContactHistory));
        Notify(nameof(bCanCreate)); Notify(nameof(bCanEditContact)); Notify(nameof(bCanEditOrganization));
        ClearPasswords();
        if (objPreviousSession is not null)
        {
            try { await WorkAsync(() => { objAuthentication?.SignOut(objPreviousSession); return true; }); }
            finally { objPreviousSession.Dispose(); }
        }
        if (objAuthentication is not null) bSetup = await WorkAsync(() => objAuthentication.NeedsSetup);
        Notify(nameof(bSetup));
    }

    public Task CloseAsync() => tskClosing ??= FinishCloseAsync();
    public void Close() => _ = CloseAsync();
    private async Task FinishCloseAsync()
    {
        await LockAsync();
        await tskThemeSave;
        bClosed = true;
        secPassword.Dispose(); secConfirmation.Dispose();
    }

    private async Task NavigateAsync(string strDestination)
    {
        strSearch = ""; strSkill = ""; bArchived = false; iAvailability = 0;
        objCurrentContact = null; objCurrentOrganization = null;
        if (strDestination == "Overview") objDashboard = await WorkAsync(() => Crm.Dashboard());
        else if (strDestination == "Contacts")
        {
            lstContactOrganizations = await WorkAsync(OrganizationOptions);
            Notify(nameof(lstContactOrganizations));
            lstContacts = await WorkAsync(() => Crm.Contacts());
        }
        else if (strDestination == "Organizations") lstOrganizations = await WorkAsync(() => Crm.Organizations());
        strPage = strDestination;
        Notify(nameof(strPage)); Notify(nameof(lstContacts)); Notify(nameof(lstOrganizations));
        Notify(nameof(objContact)); Notify(nameof(objOrganization));
        Notify(nameof(strSearch)); Notify(nameof(strSkill)); Notify(nameof(bArchived)); Notify(nameof(iAvailability));
    }

    private async Task RefreshListsAsync()
    {
        var strFilter = strSearch; var strSkillFilter = strSkill; var bArchiveFilter = bArchived;
        var enAvailability = iAvailability == 0 ? (Availability?)null : (Availability)(iAvailability - 1);
        if (strPage == "Contacts")
        {
            lstContacts = await WorkAsync(() => Crm.Contacts(strFilter, strSkillFilter, enAvailability, bArchiveFilter));
            Notify(nameof(lstContacts));
        }
        else
        {
            lstOrganizations = await WorkAsync(() => Crm.Organizations(strFilter, bArchiveFilter));
            Notify(nameof(lstOrganizations));
        }
    }

    private static T CopyRecord<T>(T objSource) where T : new()
    {
        var objCopy = new T();
        foreach (var objProperty in typeof(T).GetProperties().Where(objProperty => objProperty.CanWrite))
        {
            var objValue = objProperty.GetValue(objSource);
            objProperty.SetValue(objCopy, objValue is List<long> lstIds ? lstIds.ToList() : objValue);
        }
        return objCopy;
    }

    private async Task SaveContactAsync(bool bToggleArchive)
    {
        // Background validation/save operates on a snapshot, never on the bound edit buffer.
        var objRecord = CopyRecord(SelectedContact());
        if (bToggleArchive) objRecord.Archived = !objRecord.Archived;
        var lngSavedId = await WorkAsync(() => Crm.Save(objRecord));
        await RefreshListsAsync();
        await SelectContactCoreAsync(lstContacts.FirstOrDefault(objItem => objItem.Id == lngSavedId));
        strMessage = "Contact saved.";
    }

    private async Task SaveOrganizationAsync(bool bToggleArchive)
    {
        var objRecord = CopyRecord(objOrganization ?? throw new ArgumentException("Select an organization."));
        if (bToggleArchive) objRecord.Archived = !objRecord.Archived;
        var lngSavedId = await WorkAsync(() => Crm.Save(objRecord));
        await RefreshListsAsync();
        await SelectOrganizationCoreAsync(lstOrganizations.FirstOrDefault(objItem => objItem.Id == lngSavedId));
        strMessage = "Organization saved.";
    }

    private async Task LoadHistoryAsync()
    {
        lstInteractions = []; lstEmployment = []; lstContracts = []; lstEmployers = [];
        if (objCurrentContact is { Id: > 0 } objRecord)
        {
            var objHistory = await WorkAsync(() =>
                (lstNotes: Crm.Interactions(objRecord.Id), lstAgreements: Crm.Contracts(objRecord.Id),
                 lstCompanies: Crm.Organizations(), lstPlacements: Crm.EmploymentHistory(objRecord.Id)));
            lstInteractions = objHistory.lstNotes; lstContracts = objHistory.lstAgreements; lstEmployers = objHistory.lstCompanies;
            lstEmployment = objHistory.lstPlacements.Select(objPlacement =>
                new EmploymentRowViewModel(objPlacement, Command(() => EndEmploymentAsync(objPlacement.Id)))).ToArray();
        }
        Notify(nameof(lstInteractions)); Notify(nameof(lstContracts)); Notify(nameof(lstEmployers)); Notify(nameof(lstEmployment));
    }

    private async Task EndEmploymentAsync(long lngEmploymentId)
    {
        var dtEnd = ParseDate(lstEmployment.Single(objItem => objItem.objEmployment.Id == lngEmploymentId).strEndDate);
        await WorkAsync(() => { Crm.EndEmployment(lngEmploymentId, dtEnd); return true; });
        await LoadHistoryAsync(); Notify(nameof(objContact)); strMessage = "Placement ended.";
    }

    private void ClearHistoryInputs()
    {
        objEmployer = null; strNote = ""; strRole = ""; strStartDate = Today(); strEndDate = "";
        strContractDate = Today(); strContractValue = ""; strLocation = ""; strDescription = "";
    }

    private async Task RunAsync(Func<Task> fnExecute)
    {
        strMessage = "";
        try { await fnExecute(); }
        catch (ArgumentException objException) { strMessage = objException.Message; }
        catch (UnauthorizedAccessException)
        {
            try { await LockCoreAsync(); }
            catch (Exception objException) when (objException is InvalidOperationException or UnauthorizedAccessException or IOException or CryptographicException or SqliteException) { }
            strMessage = "Your access could not be verified. Sign in again.";
        }
        catch (SqliteException) { strMessage = "The local operation could not be completed. Check access and try again."; }
        catch (Exception objException) when (objException is InvalidOperationException or IOException or CryptographicException)
        { strMessage = "The workspace operation could not be completed. Contact your administrator if it persists."; }
        catch (Exception objException)
        {
            System.Diagnostics.Debug.WriteLine($"CRM operation failed: {objException.GetType().Name}");
            strMessage = "The operation could not be completed. Contact your administrator if it persists.";
        }
        finally { SetBusy(false); }
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

public sealed record OrganizationOptionViewModel(long? lngOrganizationId, string strName);

public sealed class OrganizationSelectionViewModel(long lngOrganizationId, string strName, bool bInitiallySelected,
    bool bCanEdit, Action<long, bool> actSelectionChanged) : ViewModelBase
{
    private bool bCurrentSelected = bInitiallySelected;
    public long lngOrganizationId { get; } = lngOrganizationId;
    public string strName { get; } = strName;
    public bool bCanEdit { get; } = bCanEdit;
    public ICommand cmdRemove => new RelayCommand(() => bSelected = false);
    public bool bSelected
    {
        get => bCurrentSelected;
        set { if (bCanEdit && Set(ref bCurrentSelected, value)) actSelectionChanged(lngOrganizationId, value); }
    }
}

public sealed record ContactTagViewModel(string strName);

// Shell presentation models remain in this source unit for the existing linked-source security tests.
public abstract class ShellPageViewModel(string strTitle, string strSummary) : ViewModelBase
{
    public string strTitle { get; } = strTitle;
    public string strSummary { get; } = strSummary;
}

public sealed class ContactsPageViewModel : ShellPageViewModel
{
    private ContactProfileViewModel? vmCurrentProfile;
    private int iCurrentTab;
    private string strCurrentSearchQuery = "";
    private int iSearchRevision;
    private (ContactLookupRowViewModel vmRow, string strName, string strEmail, string strPhone, string strTags,
        string strFirstName, string strLastName)[] arrSearchRows = [];
    public Task tskSearch { get; private set; } = Task.CompletedTask;
    public string strSearchQuery
    {
        get => strCurrentSearchQuery;
        set
        {
            if (Set(ref strCurrentSearchQuery, value ?? "")) tskSearch = FilterRowsAsync();
        }
    }
    public MainWindowViewModel vmWorkspace { get; }
    public ContactProfileViewModel? vmProfile => vmCurrentProfile;
    public bool bProfile => vmCurrentProfile is not null;
    public IReadOnlyList<ContactLookupRowViewModel> lstRows { get; private set; } = [];
    public int iSelectedTab
    {
        get => iCurrentTab;
        set
        {
            if (value is < 0 or > 1 || (value == 1 && !vmWorkspace.bCanCreate)) return;
            if (Set(ref iCurrentTab, value) && value == 1) vmWorkspace.cmdNewContact.Execute(null);
        }
    }

    public ContactsPageViewModel(MainWindowViewModel vmWorkspace)
        : base("Contacts", "Find contacts, create records and view profiles")
    {
        this.vmWorkspace = vmWorkspace;
        vmWorkspace.PropertyChanged += OnWorkspaceChanged;
    }

    private void OnWorkspaceChanged(object? objSender, PropertyChangedEventArgs objArguments)
    {
        if (objArguments.PropertyName is nameof(MainWindowViewModel.lstContacts) or nameof(MainWindowViewModel.lstContactOrganizations))
            RefreshRows();
        if (objArguments.PropertyName == nameof(MainWindowViewModel.strPage))
        {
            vmCurrentProfile = null; iCurrentTab = 0;
            Notify(nameof(vmProfile)); Notify(nameof(bProfile)); Notify(nameof(iSelectedTab));
            // Invalidate pending results before leaving the route or locking the workspace.
            iSearchRevision++; arrSearchRows = []; lstRows = [];
            Set(ref strCurrentSearchQuery, "", nameof(strSearchQuery)); Notify(nameof(lstRows));
            if (vmWorkspace.strPage == "Contacts") RefreshRows();
        }
        // A successful save turns the draft into a persisted record. Open its profile.
        if (objArguments.PropertyName == nameof(MainWindowViewModel.objContact)
            && iCurrentTab == 1 && vmWorkspace.objContact is { Id: > 0 })
            ShowProfile(vmWorkspace.objContact);
        else if (objArguments.PropertyName == nameof(MainWindowViewModel.objContact) && bProfile)
        {
            if (vmWorkspace.objContact is { Id: > 0 } objContact)
            {
                // History notifications for the same contact must not recreate the view or reset its tab.
                if (!ReferenceEquals(vmCurrentProfile!.objContact, objContact)) ShowProfile(objContact);
            }
            else Back();
        }
    }

    private void RefreshRows()
    {
        var arrRows = vmWorkspace.lstContacts.Select(objContact => new ContactLookupRowViewModel(
            objContact,
            vmWorkspace.ContactOrganizationNames(objContact),
            new AsyncRelayCommand(() => OpenProfileAsync(objContact)))).ToArray();
        // Snapshot searchable strings on the UI context so workers never read live edit buffers.
        arrSearchRows = arrRows.Select(vmRow => (vmRow, vmRow.strName, vmRow.strEmail, vmRow.strPhone, vmRow.strAllTags,
            vmRow.strFirstName, vmRow.strLastName)).ToArray();
        tskSearch = FilterRowsAsync();
    }

    private async Task FilterRowsAsync()
    {
        var iRevision = ++iSearchRevision;
        var strQuery = strSearchQuery.Trim();
        var arrSnapshot = arrSearchRows;
        var arrFiltered = await Task.Run(() =>
        {
            var strSearchCode = SoundexHelper.GetSoundexCode(strQuery);
            return arrSnapshot.Where(objRow => strQuery.Length == 0
                || objRow.strName.Contains(strQuery, StringComparison.OrdinalIgnoreCase)
                || objRow.strEmail.Contains(strQuery, StringComparison.OrdinalIgnoreCase)
                || objRow.strPhone.Contains(strQuery, StringComparison.OrdinalIgnoreCase)
                || objRow.strTags.Contains(strQuery, StringComparison.OrdinalIgnoreCase)
                || (strSearchCode.Length != 0 && (SoundexHelper.GetSoundexCode(objRow.strFirstName) == strSearchCode
                    || SoundexHelper.GetSoundexCode(objRow.strLastName) == strSearchCode)))
                .Select(objRow => objRow.vmRow).ToArray();
        });
        // A slower, older keystroke must never overwrite newer results or repopulate a locked view.
        if (iRevision != iSearchRevision || vmWorkspace.strPage != "Contacts" || vmWorkspace.bLocking) return;
        lstRows = arrFiltered;
        Notify(nameof(lstRows));
    }

    private async Task OpenProfileAsync(Contact objContact)
    {
        // Only route records from the current authorized lookup results.
        if (vmWorkspace.bBusy || vmWorkspace.bLocking || !vmWorkspace.lstContacts.Contains(objContact) || vmWorkspace.strPage != "Contacts") return;
        await vmWorkspace.SelectContactAsync(objContact);
        if (!vmWorkspace.bLocking && vmWorkspace.strPage == "Contacts" && ReferenceEquals(vmWorkspace.objContact, objContact)) ShowProfile(objContact);
    }

    private void ShowProfile(Contact objContact)
    {
        iCurrentTab = 0;
        vmCurrentProfile = new ContactProfileViewModel(vmWorkspace, objContact, new RelayCommand(Back));
        Notify(nameof(iSelectedTab)); Notify(nameof(vmProfile)); Notify(nameof(bProfile));
    }

    private void Back()
    {
        vmCurrentProfile = null;
        Notify(nameof(vmProfile)); Notify(nameof(bProfile));
    }
}

public sealed class ContactLookupRowViewModel(Contact objContact, string strOrganization, ICommand cmdViewProfile)
{
    public string strFirstName => objContact.FirstName;
    public string strLastName => objContact.LastName;
    public string strName => objContact.DisplayName;
    public string strStatus => objContact.Status.ToString();
    public string strOrganization { get; } = strOrganization;
    public string strEmail => objContact.Email;
    public string strPhone => objContact.Phone;
    public IReadOnlyList<ContactTagViewModel> lstTags => objContact.lstTags.Select(strTag => new ContactTagViewModel(strTag)).ToArray();
    public string strAllTags => string.Join(", ", objContact.lstTags);
    public ICommand cmdViewProfile { get; } = cmdViewProfile;
}

public sealed class ContactProfileViewModel(MainWindowViewModel vmWorkspace, Contact objContact, ICommand cmdBack)
{
    public MainWindowViewModel vmWorkspace { get; } = vmWorkspace;
    public Contact objContact { get; } = objContact;
    public ICommand cmdBack { get; } = cmdBack;
}
public sealed class OrganizationsPageViewModel() : ShellPageViewModel("Organizations", "Organization profiles, services and relationships");
public sealed class AuditLogsPageViewModel() : ShellPageViewModel("Audit Logs", "Local security event review")
{
    // No existing authorized audit-reader API is exposed to this frontend. Do not fabricate records.
    public string strAvailability => "Audit log review is unavailable in this workspace.";
}

public sealed record ActivityRowViewModel(string strPeriod, int iInteractions, int iPercent);

public sealed class DashboardPageViewModel(MainWindowViewModel vmWorkspace)
    : ShellPageViewModel("Dashboard", "Authorized workspace totals and weekly interaction activity")
{
    public int iContacts => vmWorkspace.objDashboard?.Contacts ?? 0;
    public int iOrganizations => vmWorkspace.objDashboard?.Organizations ?? 0;
    public int iAvailable => vmWorkspace.objDashboard?.Available ?? 0;
    public int iArchived => vmWorkspace.objDashboard?.Archived ?? 0;
    public int iInteractions => vmWorkspace.objDashboard?.Interactions30Days ?? 0;
    public int iOverdue => vmWorkspace.objDashboard?.Overdue ?? 0;
    public IReadOnlyList<ActivityRowViewModel> lstActivity { get; private set; } = [];
    public bool bHasActivity => lstActivity.Count > 0;

    internal void Refresh()
    {
        var lstWeeks = vmWorkspace.objDashboard?.Activity ?? [];
        var iMaximum = Math.Max(1, lstWeeks.Select(objWeek => objWeek.Count).DefaultIfEmpty(0).Max());
        lstActivity = lstWeeks.Select(objWeek => new ActivityRowViewModel(objWeek.Label,
            objWeek.Count, (int)Math.Round(100.0 * objWeek.Count / iMaximum))).ToArray();
        Notify(nameof(iContacts)); Notify(nameof(iOrganizations)); Notify(nameof(iAvailable));
        Notify(nameof(iArchived)); Notify(nameof(iInteractions)); Notify(nameof(iOverdue));
        Notify(nameof(lstActivity)); Notify(nameof(bHasActivity));
    }
}

// Routes presentation only. Existing workspace commands retain all authorization and persistence.
public sealed class ShellViewModel : ViewModelBase
{
    private readonly MainWindowViewModel vmWorkspace;
    private readonly DashboardPageViewModel vmDashboard;
    private readonly ContactsPageViewModel vmContacts;
    private readonly OrganizationsPageViewModel vmOrganizations = new();
    private readonly AuditLogsPageViewModel vmAuditLogs = new();
    private ShellPageViewModel vmCurrent;

    public ShellViewModel(MainWindowViewModel vmWorkspace)
    {
        this.vmWorkspace = vmWorkspace;
        vmContacts = new ContactsPageViewModel(vmWorkspace);
        vmDashboard = new DashboardPageViewModel(vmWorkspace);
        vmCurrent = vmDashboard;
        cmdDashboard = vmWorkspace.cmdOverview;
        cmdContacts = vmWorkspace.cmdContacts;
        cmdOrganizations = vmWorkspace.cmdOrganizations;
        cmdAuditLogs = new RelayCommand(() => { if (bCanReviewAudit) Select(vmAuditLogs); });
        cmdLock = vmWorkspace.cmdLock;
        vmWorkspace.PropertyChanged += OnWorkspaceChanged;
    }

    public ShellPageViewModel vmCurrentPage => vmCurrent;
    public string strPageTitle => vmCurrent.strTitle;
    public string strPageSummary => vmCurrent.strSummary;
    public string strUsername => vmWorkspace.strSignedInUsername;
    public string strMessage => vmWorkspace.strMessage;
    public bool bDashboard => ReferenceEquals(vmCurrent, vmDashboard);
    public bool bContacts => ReferenceEquals(vmCurrent, vmContacts);
    public bool bOrganizations => ReferenceEquals(vmCurrent, vmOrganizations);
    public bool bAuditLogs => ReferenceEquals(vmCurrent, vmAuditLogs);
    public bool bCanReviewAudit => vmWorkspace.bCanCreate;
    public bool bDarkMode { get => vmWorkspace.bDark; set => vmWorkspace.bDark = value; }
    public bool bBusy => vmWorkspace.bBusy;
    public ICommand cmdDashboard { get; }
    public ICommand cmdContacts { get; }
    public ICommand cmdOrganizations { get; }
    public ICommand cmdAuditLogs { get; }
    public ICommand cmdLock { get; }

    public void SelectContact(Contact? objContact)
    {
        // A new draft has no corresponding list row; coercing that selection to null must not erase it.
        if (!bContacts || (objContact is null && vmWorkspace.objContact is { Id: 0 })) return;
        if (!ReferenceEquals(objContact, vmWorkspace.objContact)) vmWorkspace.objContact = objContact;
    }

    public void SelectOrganization(Organization? objOrganization)
    {
        if (!bOrganizations || (objOrganization is null && vmWorkspace.objOrganization is { Id: 0 })) return;
        if (!ReferenceEquals(objOrganization, vmWorkspace.objOrganization)) vmWorkspace.objOrganization = objOrganization;
    }

    private void OnWorkspaceChanged(object? objSender, PropertyChangedEventArgs objArguments)
    {
        if (objArguments.PropertyName is nameof(MainWindowViewModel.strPage) or nameof(MainWindowViewModel.objDashboard))
            vmDashboard.Refresh();
        if (objArguments.PropertyName == nameof(MainWindowViewModel.strPage))
            Select(vmWorkspace.strPage switch
            {
                "Contacts" => vmContacts,
                "Organizations" => vmOrganizations,
                _ => vmDashboard
            });
        Notify(nameof(strUsername)); Notify(nameof(strMessage));
        Notify(nameof(bCanReviewAudit)); Notify(nameof(bDarkMode));
        Notify(nameof(bBusy));
    }

    private void Select(ShellPageViewModel vmPage)
    {
        if (!ReferenceEquals(vmCurrent, vmPage))
        {
            vmCurrent = vmPage;
            Notify(nameof(vmCurrentPage));
        }
        Notify(nameof(strPageTitle)); Notify(nameof(strPageSummary));
        Notify(nameof(bDashboard)); Notify(nameof(bContacts));
        Notify(nameof(bOrganizations)); Notify(nameof(bAuditLogs));
    }
}
