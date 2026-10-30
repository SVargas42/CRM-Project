using System.Windows.Input;
using HippoCrm.Core;
using HippoCrm.Desktop.Utilities;

namespace HippoCrm.Desktop.ViewModels;

public sealed record MasterSearchSnapshot(IReadOnlyList<Contact> lstContacts, IReadOnlyList<Organization> lstOrganizations);
public sealed record MasterSearchFilterOption(string strLabel, string? strValue);
public sealed record MasterSearchResultViewModel(long lngRecordId, string strType, string strName, string strPrimaryInfo, string strStatus);

public sealed class MasterSearchViewModel : ViewModelBase
{
    private readonly Func<Task<MasterSearchSnapshot?>> fnLoad;
    private string strCurrentSearchText = "";
    private string strCurrentSearchStatus = "";
    private bool bCurrentSearching;
    private bool bLoaded;
    private int iSearchRevision;
    private MasterSearchFilterOption objCurrentTag = new("Any tag", null);
    private MasterSearchFilterOption objCurrentStatus = new("Any status", null);
    private MasterSearchFilterOption objCurrentSkill = new("Any skill", null);
    private OrganizationOptionViewModel objCurrentOrganization = new(null, "Any organization");

    public MasterSearchViewModel(Func<Task<MasterSearchSnapshot?>> fnLoad)
    {
        this.fnLoad = fnLoad;
        cmdSearch = new AsyncRelayCommand(SearchAsync, () => !bSearching);
        cmdClearFilters = new RelayCommand(ClearFilters);
    }

    public string strSearchText { get => strCurrentSearchText; set => Set(ref strCurrentSearchText, value ?? ""); }
    public string strSearchStatus { get => strCurrentSearchStatus; private set => Set(ref strCurrentSearchStatus, value); }
    public bool bSearching { get => bCurrentSearching; private set => Set(ref bCurrentSearching, value); }
    public MasterSearchFilterOption objSelectedTag { get => objCurrentTag; set => Set(ref objCurrentTag, value ?? lstTags[0]); }
    public MasterSearchFilterOption objSelectedStatus { get => objCurrentStatus; set => Set(ref objCurrentStatus, value ?? lstStatuses[0]); }
    public MasterSearchFilterOption objSelectedSkill { get => objCurrentSkill; set => Set(ref objCurrentSkill, value ?? lstSkills[0]); }
    public OrganizationOptionViewModel objSelectedOrganization { get => objCurrentOrganization; set => Set(ref objCurrentOrganization, value ?? lstOrganizations[0]); }
    public IReadOnlyList<MasterSearchFilterOption> lstTags { get; private set; } = [new("Any tag", null)];
    public IReadOnlyList<MasterSearchFilterOption> lstStatuses { get; } = new[] { new MasterSearchFilterOption("Any status", null) }
        .Concat(Enum.GetValues<RecordStatus>().Select(enStatus => new MasterSearchFilterOption(enStatus.ToString(), enStatus.ToString()))).ToArray();
    public IReadOnlyList<MasterSearchFilterOption> lstSkills { get; private set; } = [new("Any skill", null)];
    public IReadOnlyList<OrganizationOptionViewModel> lstOrganizations { get; private set; } = [new(null, "Any organization")];
    public IReadOnlyList<MasterSearchResultViewModel> lstResults { get; private set; } = [];
    public ICommand cmdSearch { get; }
    public ICommand cmdClearFilters { get; }

    public void EnsureLoaded()
    {
        if (!bLoaded && !bSearching) cmdSearch.Execute(null);
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
        if (bSearching) return;
        var iRevision = ++iSearchRevision;
        var strTerm = strSearchText.Trim();
        var strTag = objSelectedTag.strValue; var strStatus = objSelectedStatus.strValue;
        var strSkill = objSelectedSkill.strValue; var lngOrganization = objSelectedOrganization.lngOrganizationId;
        bSearching = true; ((AsyncRelayCommand)cmdSearch).Refresh();
        lstResults = []; Notify(nameof(lstResults)); strSearchStatus = "Searching...";
        try
        {
            var objSnapshot = await fnLoad();
            if (iRevision != iSearchRevision) return;
            if (objSnapshot is null) { strSearchStatus = "Search could not be completed. Try again."; return; }
            var objOutput = await Task.Run(() =>
            {
                var arrTags = objSnapshot.lstContacts.SelectMany(objContact => Tokens(objContact.strTags))
                    .Concat(objSnapshot.lstOrganizations.SelectMany(objOrganization => Tokens(objOrganization.Tags)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var arrSkills = objSnapshot.lstContacts.SelectMany(objContact => Tokens(objContact.Skills))
                    .Concat(objSnapshot.lstOrganizations.SelectMany(objOrganization => Tokens(objOrganization.Services)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                // Drop filter choices that are no longer present in the newly authorized snapshot.
                var strValidTag = arrTags.FirstOrDefault(strItem => string.Equals(strItem, strTag, StringComparison.OrdinalIgnoreCase));
                var strValidSkill = arrSkills.FirstOrDefault(strItem => string.Equals(strItem, strSkill, StringComparison.OrdinalIgnoreCase));
                var lngValidOrganization = objSnapshot.lstOrganizations.Any(objOrganization => objOrganization.Id == lngOrganization) ? lngOrganization : null;
                var strCode = SoundexHelper.GetSoundexCode(strTerm);
                var lstContacts = objSnapshot.lstContacts.Where(objContact =>
                    (strStatus is null || objContact.Status.ToString() == strStatus)
                    && ContainsToken(objContact.strTags, strValidTag) && ContainsToken(objContact.Skills, strValidSkill)
                    && (lngValidOrganization is null || objContact.lstOrganizationIds.Contains(lngValidOrganization.Value))
                    && (LiteralMatch(strTerm, objContact.DisplayName, objContact.Email, objContact.Phone, objContact.Address, objContact.Skills, objContact.strTags)
                        || PhoneticMatch(strCode, objContact.FirstName, objContact.LastName)))
                    .Select(objContact => new MasterSearchResultViewModel(objContact.Id, "Contact", objContact.DisplayName,
                        PrimaryInfo(objContact.Phone, objContact.Email), objContact.Status.ToString()));
                var lstOrganizations = objSnapshot.lstOrganizations.Where(objOrganization =>
                    (strStatus is null || objOrganization.Status.ToString() == strStatus)
                    && ContainsToken(objOrganization.Tags, strValidTag) && ContainsToken(objOrganization.Services, strValidSkill)
                    && (lngValidOrganization is null || objOrganization.Id == lngValidOrganization)
                    && (LiteralMatch(strTerm, objOrganization.Name, objOrganization.Phone, objOrganization.Email, objOrganization.Address, objOrganization.Services, objOrganization.Tags)
                        || PhoneticMatch(strCode, objOrganization.Name)))
                    .Select(objOrganization => new MasterSearchResultViewModel(objOrganization.Id, "Organization", objOrganization.Name,
                        PrimaryInfo(objOrganization.Phone, objOrganization.Email, objOrganization.Services), objOrganization.Status.ToString()));
                var arrResults = lstContacts.Concat(lstOrganizations).OrderBy(vmRow => vmRow.strName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(vmRow => vmRow.strType, StringComparer.Ordinal).ToArray();
                return (arrTags, arrSkills, arrResults, strValidTag, strValidSkill, lngValidOrganization);
            });
            if (iRevision != iSearchRevision) return;
            lstTags = new[] { new MasterSearchFilterOption("Any tag", null) }
                .Concat(objOutput.arrTags.Select(strTagValue => new MasterSearchFilterOption(strTagValue, strTagValue))).ToArray();
            lstSkills = new[] { new MasterSearchFilterOption("Any skill", null) }
                .Concat(objOutput.arrSkills.Select(strSkillValue => new MasterSearchFilterOption(strSkillValue, strSkillValue))).ToArray();
            lstOrganizations = new[] { new OrganizationOptionViewModel(null, "Any organization") }
                .Concat(objSnapshot.lstOrganizations.OrderBy(objOrganization => objOrganization.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(objOrganization => new OrganizationOptionViewModel(objOrganization.Id, objOrganization.Name))).ToArray();
            Notify(nameof(lstTags)); Notify(nameof(lstSkills)); Notify(nameof(lstOrganizations));
            objSelectedTag = lstTags.First(vmOption => vmOption.strValue == objOutput.strValidTag);
            objSelectedSkill = lstSkills.First(vmOption => vmOption.strValue == objOutput.strValidSkill);
            objSelectedOrganization = lstOrganizations.First(vmOption => vmOption.lngOrganizationId == objOutput.lngValidOrganization);
            lstResults = objOutput.arrResults; Notify(nameof(lstResults));
            bLoaded = true; strSearchStatus = $"{lstResults.Count} result(s).";
        }
        catch (Exception objException) when (objException is ArgumentException or InvalidOperationException)
        {
            if (iRevision == iSearchRevision) { lstResults = []; Notify(nameof(lstResults)); strSearchStatus = "Search could not be completed. Try again."; }
        }
        finally
        {
            if (iRevision == iSearchRevision) { bSearching = false; ((AsyncRelayCommand)cmdSearch).Refresh(); }
        }
    }

    private void ClearFilters()
    {
        if (bSearching) return;
        objSelectedTag = lstTags[0]; objSelectedStatus = lstStatuses[0];
        objSelectedSkill = lstSkills[0]; objSelectedOrganization = lstOrganizations[0];
    }

    public void Reset()
    {
        iSearchRevision++; bLoaded = false; bSearching = false; strSearchText = ""; strSearchStatus = "";
        lstTags = [new("Any tag", null)]; lstSkills = [new("Any skill", null)];
        lstOrganizations = [new(null, "Any organization")]; lstResults = [];
        Notify(nameof(lstTags)); Notify(nameof(lstSkills)); Notify(nameof(lstOrganizations)); Notify(nameof(lstResults));
        ClearFilters(); ((AsyncRelayCommand)cmdSearch).Refresh();
    }
}

