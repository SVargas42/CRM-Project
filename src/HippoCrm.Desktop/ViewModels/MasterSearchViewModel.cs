using System.Windows.Input;
using HippoCrm.Core;
using HippoCrm.Desktop.Utilities;

namespace HippoCrm.Desktop.ViewModels;

// Ownership transfers to the search operation; providers must return fresh projections.
public sealed record MasterSearchSnapshot(IReadOnlyList<Contact> lstContacts, IReadOnlyList<Organization> lstOrganizations) : IDisposable
{
    public void Dispose() { CuiMemoryPolicy.Release(lstContacts); CuiMemoryPolicy.Release(lstOrganizations); }
}
public sealed record MasterSearchFilterOption(string strLabel, string? strValue);
public sealed record MasterSearchResultViewModel(long lngRecordId, string strType, string strName, string strPrimaryInfo, string strStatus);

public sealed class MasterSearchViewModel : ViewModelBase
{
    private readonly Func<Task<MasterSearchSnapshot?>> fnLoad;
    private readonly Func<bool> fnCanRead;
    private readonly Func<CancellationToken, Task<bool>> fnRevalidate;
    private readonly Func<CancellationToken> fnToken;
    private CancellationTokenSource? objSearchCancellation;
    private readonly Action<int>? actPage;
    private int iPageOffset;
    private int iCurrentOrganizationVisibility;
    public int iOrganizationVisibility { get => iCurrentOrganizationVisibility; set { if (value is >= 0 and <= 1 && Set(ref iCurrentOrganizationVisibility, value)) Notify(nameof(strFilterSummary)); } }
    public string strPageScope => $"Page {iPageOffset / CuiMemoryPolicy.iPageSize + 1}: searches up to 128 contacts and 128 organizations; long fields are shortened.";
    private IReadOnlyList<MasterSearchResultViewModel> lstCurrentResults = [];
    private string strCurrentSearchText = "";
    private string strCurrentSearchStatus = "";
    private bool bCurrentSearching;
    private bool bLoaded;
    private int iSearchRevision;
    private MasterSearchFilterOption objCurrentTag = new("Any tag", null);
    private MasterSearchFilterOption objCurrentStatus = new("Any status", null);
    private MasterSearchFilterOption objCurrentSkill = new("Any skill", null);
    private MasterSearchFilterOption objCurrentService = new("Any service", null);
    private OrganizationOptionViewModel objCurrentOrganization = new(null, "Any organization");

    public MasterSearchViewModel(Func<Task<MasterSearchSnapshot?>> fnLoad, Func<bool>? fnCanRead = null,
        Func<CancellationToken, Task<bool>>? fnRevalidate = null, Func<CancellationToken>? fnToken = null, Action<int>? actPage = null)
    {
        this.fnLoad = fnLoad; this.fnCanRead = fnCanRead ?? (() => false);
        this.fnRevalidate = fnRevalidate ?? (ctCancellation => Task.FromResult(false));
        this.fnToken = fnToken ?? (() => new CancellationToken(true));
        this.actPage = actPage;
        cmdNextPage = new AsyncRelayCommand(() => ChangePageAsync(1), () => !bSearching && this.fnCanRead() && actPage is not null);
        cmdPreviousPage = new AsyncRelayCommand(() => ChangePageAsync(-1), () => !bSearching && iPageOffset > 0 && this.fnCanRead() && actPage is not null);
        cmdSearch = new AsyncRelayCommand(SearchAsync, () => !bSearching);
        cmdClearFilters = new RelayCommand(ClearFilters);
    }

    public string strSearchText { get => fnCanRead() ? strCurrentSearchText : ""; set { if (Set(ref strCurrentSearchText, value ?? "")) Notify(nameof(strFilterSummary)); } }
    public string strSearchStatus { get => strCurrentSearchStatus; private set => Set(ref strCurrentSearchStatus, value); }
    public bool bSearching { get => bCurrentSearching; private set => Set(ref bCurrentSearching, value); }
    public MasterSearchFilterOption objSelectedTag { get => fnCanRead() ? objCurrentTag : new("Any tag", null); set { if (Set(ref objCurrentTag, value ?? new MasterSearchFilterOption("Any tag", null))) Notify(nameof(strFilterSummary)); } }
    public MasterSearchFilterOption objSelectedStatus { get => objCurrentStatus; set { if (Set(ref objCurrentStatus, value ?? lstStatuses[0])) Notify(nameof(strFilterSummary)); } }
    public MasterSearchFilterOption objSelectedSkill { get => fnCanRead() ? objCurrentSkill : new("Any skill", null); set { if (Set(ref objCurrentSkill, value ?? new MasterSearchFilterOption("Any skill", null))) Notify(nameof(strFilterSummary)); } }
    public MasterSearchFilterOption objSelectedService { get => fnCanRead() ? objCurrentService : new("Any service", null); set { if (Set(ref objCurrentService, value ?? new MasterSearchFilterOption("Any service", null))) Notify(nameof(strFilterSummary)); } }
    public OrganizationOptionViewModel objSelectedOrganization { get => fnCanRead() ? objCurrentOrganization : new(null, "Any organization"); set { if (Set(ref objCurrentOrganization, value ?? new OrganizationOptionViewModel(null, "Any organization"))) Notify(nameof(strFilterSummary)); } }
    public string strFilterSummary => ContactsPageViewModel.BuildFilterSummary(
        ("Search", strSearchText.Trim()), ("Tag", objSelectedTag.strValue ?? ""),
        ("Contact status", objSelectedStatus.strValue ?? ""), ("Contact skill", objSelectedSkill.strValue ?? ""),
        ("Organization service", objSelectedService.strValue ?? ""),
        ("Organization", objSelectedOrganization.lngOrganizationId is null ? "" : objSelectedOrganization.strName),
        ("Organization lifecycle", iOrganizationVisibility == 1 ? "Archived" : "Active"));
    private IReadOnlyList<MasterSearchFilterOption> lstCachedTags = [new("Any tag", null)];
    public IReadOnlyList<MasterSearchFilterOption> lstTags { get => fnCanRead() ? lstCachedTags : []; private set => lstCachedTags = value; }
    public IReadOnlyList<MasterSearchFilterOption> lstStatuses { get; } = new[] { new MasterSearchFilterOption("Any status", null) }
        .Concat(Enum.GetValues<RecordStatus>().Select(enStatus => new MasterSearchFilterOption(enStatus.ToString(), enStatus.ToString()))).ToArray();
    private IReadOnlyList<MasterSearchFilterOption> lstCachedSkills = [new("Any skill", null)];
    public IReadOnlyList<MasterSearchFilterOption> lstSkills { get => fnCanRead() ? lstCachedSkills : []; private set => lstCachedSkills = value; }
    private IReadOnlyList<MasterSearchFilterOption> lstCachedServices = [new("Any service", null)];
    public IReadOnlyList<MasterSearchFilterOption> lstServices { get => fnCanRead() ? lstCachedServices : []; private set => lstCachedServices = value; }
    private IReadOnlyList<OrganizationOptionViewModel> lstCachedOrganizations = [new(null, "Any organization")];
    public IReadOnlyList<OrganizationOptionViewModel> lstOrganizations { get => fnCanRead() ? lstCachedOrganizations : []; private set => lstCachedOrganizations = value; }
    public IReadOnlyList<MasterSearchResultViewModel> lstResults { get => fnCanRead() ? lstCurrentResults : []; private set => lstCurrentResults = value; }
    public ICommand cmdSearch { get; }
    public ICommand cmdNextPage { get; }
    public ICommand cmdPreviousPage { get; }
    public ICommand cmdClearFilters { get; }

    public void EnsureLoaded()
    {
        if (!bLoaded && !bSearching) cmdSearch.Execute(null);
    }
    private async Task ChangePageAsync(int iDirection)
    {
        if (!fnCanRead()) return;
        iPageOffset = checked(Math.Max(0, iPageOffset + iDirection * CuiMemoryPolicy.iPageSize));
        actPage?.Invoke(iPageOffset); Notify(nameof(strPageScope));
        await SearchAsync();
    }

    private static string[] Tokens(string? strValue) => (strValue ?? "")
        .Split([',', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool ContainsToken(string? strValue, string? strSelected) => strSelected is null
        || Tokens(strValue).Contains(strSelected, StringComparer.OrdinalIgnoreCase);
    private static bool LiteralMatch(string strTerm, params string[] arrFields) =>
        strTerm.Length == 0 || arrFields.Any(strField => (strField ?? "").Contains(strTerm, StringComparison.OrdinalIgnoreCase));
    private static bool PhoneticMatch(string strCode, params string[] arrNames) =>
        strCode.Length != 0 && arrNames.Any(strName => SoundexHelper.GetSoundexCode(strName) == strCode);
    private static string PrimaryInfo(params string[] arrFields) => string.Join(" · ", arrFields.Where(strField => !string.IsNullOrWhiteSpace(strField)));

    private async Task SearchAsync()
    {
        if (bSearching || !fnCanRead()) return;
        objSearchCancellation?.Cancel(); objSearchCancellation?.Dispose();
        objSearchCancellation = CancellationTokenSource.CreateLinkedTokenSource(fnToken());
        var ctCancellation = objSearchCancellation.Token;
        var iRevision = ++iSearchRevision;
        var bOrganizationsArchived = iOrganizationVisibility == 1;
        var strTerm = strSearchText.Trim();
        var strTag = objSelectedTag.strValue; var strStatus = objSelectedStatus.strValue;
        var strService = objSelectedService.strValue;
        var strSkill = objSelectedSkill.strValue; var lngOrganization = objSelectedOrganization.lngOrganizationId;
        bSearching = true; ((AsyncRelayCommand)cmdSearch).Refresh();
        lstResults = []; Notify(nameof(lstResults)); strSearchStatus = "Searching...";
        MasterSearchSnapshot? objSnapshot = null;
        try
        {
            var tskLoad = fnLoad();
            try { objSnapshot = await tskLoad.WaitAsync(ctCancellation); }
            catch (OperationCanceledException) { _ = DisposeLateSnapshotAsync(tskLoad); throw; }
            if (iRevision != iSearchRevision) return;
            if (objSnapshot is null) { strSearchStatus = "Search could not be completed. Try again."; return; }
            var arrVisibleOrganizations = objSnapshot.lstOrganizations.Where(objOrganization => objOrganization.Archived == bOrganizationsArchived).ToArray();
            var objOutput = await Task.Run(() =>
            {
                ctCancellation.ThrowIfCancellationRequested();
                var arrTags = objSnapshot.lstContacts.SelectMany(objContact => Tokens(objContact.strTags))
                    .Concat(arrVisibleOrganizations.SelectMany(objOrganization => Tokens(objOrganization.Tags)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var arrSkills = objSnapshot.lstContacts.SelectMany(objContact => Tokens(objContact.Skills))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                // Each record type keeps its own token filter; shared tags still apply to both.
                var arrServices = arrVisibleOrganizations.SelectMany(objOrganization => Tokens(objOrganization.Services))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                // Drop filter choices that are no longer present in the newly authorized snapshot.
                var strValidTag = arrTags.FirstOrDefault(strItem => string.Equals(strItem, strTag, StringComparison.OrdinalIgnoreCase));
                var strValidSkill = arrSkills.FirstOrDefault(strItem => string.Equals(strItem, strSkill, StringComparison.OrdinalIgnoreCase));
                var strValidService = arrServices.FirstOrDefault(strItem => string.Equals(strItem, strService, StringComparison.OrdinalIgnoreCase));
                var lngValidOrganization = arrVisibleOrganizations.Any(objOrganization => objOrganization.Id == lngOrganization) ? lngOrganization : null;
                var strCode = SoundexHelper.GetSoundexCode(strTerm);
                var lstContacts = objSnapshot.lstContacts.Take(CuiMemoryPolicy.iPageSize).Where(objContact =>
                    (strStatus is null || objContact.Status.ToString() == strStatus)
                    && ContainsToken(objContact.strTags, strValidTag) && ContainsToken(objContact.Skills, strValidSkill)
                    && (lngValidOrganization is null || objContact.lstOrganizationIds.Contains(lngValidOrganization.Value))
                    && (LiteralMatch(strTerm, objContact.DisplayName, objContact.Email, objContact.Phone, objContact.Address, objContact.Skills, objContact.strTags, objContact.SocialMedia)
                        || PhoneticMatch(strCode, objContact.Prefix, objContact.FirstName, objContact.MiddleNames, objContact.LastName, objContact.Suffix)))
                    .Select(objContact => new MasterSearchResultViewModel(objContact.Id, "Contact", objContact.DisplayName,
                        PrimaryInfo(objContact.Phone, objContact.Email), objContact.Status.ToString()));
                var lstOrganizations = arrVisibleOrganizations.Take(CuiMemoryPolicy.iPageSize).Where(objOrganization =>
                    objOrganization.Archived == bOrganizationsArchived
                    && ContainsToken(objOrganization.Tags, strValidTag) && ContainsToken(objOrganization.Services, strValidService)
                    && (lngValidOrganization is null || objOrganization.Id == lngValidOrganization)
                    && (LiteralMatch(strTerm, objOrganization.Name, objOrganization.Phone, objOrganization.Email, objOrganization.Address, objOrganization.Services, objOrganization.Tags)
                        || PhoneticMatch(strCode, objOrganization.Name)))
                    .Select(objOrganization => new MasterSearchResultViewModel(objOrganization.Id, "Organization", objOrganization.Name,
                        PrimaryInfo(objOrganization.Phone, objOrganization.Email, objOrganization.Services), objOrganization.Archived ? "Archived" : "Active"));
                var arrResults = lstContacts.Concat(lstOrganizations).OrderBy(vmRow => vmRow.strName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(vmRow => vmRow.strType, StringComparer.Ordinal).ToArray();
                ctCancellation.ThrowIfCancellationRequested();
                return (arrTags, arrSkills, arrServices, arrResults, strValidTag, strValidSkill, strValidService, lngValidOrganization);
            }, ctCancellation);
            if (iRevision != iSearchRevision || !fnCanRead() || !await fnRevalidate(ctCancellation)) return;
            ctCancellation.ThrowIfCancellationRequested();
            if (iRevision != iSearchRevision || !fnCanRead()) return;
            lstTags = new[] { new MasterSearchFilterOption("Any tag", null) }
                .Concat(objOutput.arrTags.Select(strTagValue => new MasterSearchFilterOption(strTagValue, strTagValue))).ToArray();
            lstSkills = new[] { new MasterSearchFilterOption("Any skill", null) }
                .Concat(objOutput.arrSkills.Select(strSkillValue => new MasterSearchFilterOption(strSkillValue, strSkillValue))).ToArray();
            lstServices = new[] { new MasterSearchFilterOption("Any service", null) }
                .Concat(objOutput.arrServices.Select(strServiceValue => new MasterSearchFilterOption(strServiceValue, strServiceValue))).ToArray();
            lstOrganizations = new[] { new OrganizationOptionViewModel(null, "Any organization") }
                .Concat(arrVisibleOrganizations.OrderBy(objOrganization => objOrganization.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(objOrganization => new OrganizationOptionViewModel(objOrganization.Id, objOrganization.Name))).ToArray();
            Notify(nameof(lstTags)); Notify(nameof(lstSkills)); Notify(nameof(lstServices)); Notify(nameof(lstOrganizations));
            objSelectedTag = lstTags.First(vmOption => vmOption.strValue == objOutput.strValidTag);
            objSelectedSkill = lstSkills.First(vmOption => vmOption.strValue == objOutput.strValidSkill);
            objSelectedService = lstServices.First(vmOption => vmOption.strValue == objOutput.strValidService);
            objSelectedOrganization = lstOrganizations.First(vmOption => vmOption.lngOrganizationId == objOutput.lngValidOrganization);
            lstResults = objOutput.arrResults; Notify(nameof(lstResults));
            bLoaded = true; strSearchStatus = $"{lstResults.Count} result(s).";
        }
        catch (OperationCanceledException) { }
        catch (Exception objException) when (objException is ArgumentException or InvalidOperationException)
        {
            if (iRevision == iSearchRevision) { lstResults = []; Notify(nameof(lstResults)); strSearchStatus = "Search could not be completed. Try again."; }
        }
        finally
        {
            objSnapshot?.Dispose();
            if (iRevision == iSearchRevision) { bSearching = false; ((AsyncRelayCommand)cmdSearch).Refresh(); }
            ((AsyncRelayCommand)cmdNextPage).Refresh(); ((AsyncRelayCommand)cmdPreviousPage).Refresh();
        }
    }
    private static async Task DisposeLateSnapshotAsync(Task<MasterSearchSnapshot?> tskLoad)
    {
        try { (await tskLoad.ConfigureAwait(false))?.Dispose(); }
        catch (Exception) { /* Canceled search cannot publish or retain a late result. */ }
    }

    private void ClearFilters()
    {
        if (bSearching) return;
        iOrganizationVisibility = 0;
        objSelectedTag = new MasterSearchFilterOption("Any tag", null); objSelectedStatus = lstStatuses[0];
        objSelectedService = new MasterSearchFilterOption("Any service", null);
        objSelectedSkill = new MasterSearchFilterOption("Any skill", null); objSelectedOrganization = new OrganizationOptionViewModel(null, "Any organization");
    }

    public void Reset()
    {
        objSearchCancellation?.Cancel();
        iOrganizationVisibility = 0;
        iPageOffset = 0; actPage?.Invoke(0); Notify(nameof(strPageScope));
        iSearchRevision++; bLoaded = false; bSearching = false; strSearchText = ""; strSearchStatus = "";
        lstTags = [new("Any tag", null)]; lstSkills = [new("Any skill", null)]; lstServices = [new("Any service", null)];
        lstOrganizations = [new(null, "Any organization")]; lstResults = [];
        Notify(nameof(lstTags)); Notify(nameof(lstSkills)); Notify(nameof(lstServices)); Notify(nameof(lstOrganizations)); Notify(nameof(lstResults));
        ClearFilters(); ((AsyncRelayCommand)cmdSearch).Refresh();
    }
    public void InvalidateCache()
    {
        objSearchCancellation?.Cancel(); iSearchRevision++; bLoaded = false; bSearching = false;
        lstTags = [new("Any tag", null)]; lstSkills = [new("Any skill", null)]; lstServices = [new("Any service", null)];
        lstOrganizations = [new(null, "Any organization")]; lstResults = [];
        Notify(nameof(lstTags)); Notify(nameof(lstSkills)); Notify(nameof(lstServices)); Notify(nameof(lstOrganizations)); Notify(nameof(lstResults));
        ((AsyncRelayCommand)cmdSearch).Refresh();
    }
}
