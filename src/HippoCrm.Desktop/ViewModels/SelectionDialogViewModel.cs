using System.Windows.Input;

namespace HippoCrm.Desktop.ViewModels;

public sealed class SelectionValueViewModel : ViewModelBase
{
    private bool bCurrentSelected;
    private readonly Action actChanged;
    public string strValue { get; }
    public bool bSelected { get => bCurrentSelected; set { if (Set(ref bCurrentSelected, value)) actChanged(); } }
    public ICommand cmdRemove { get; }
    public SelectionValueViewModel(string strValue, bool bSelected, Action actChanged)
    {
        this.strValue = strValue; bCurrentSelected = bSelected; this.actChanged = actChanged;
        cmdRemove = new RelayCommand(() => this.bSelected = false);
    }
}

// Shared presentation mechanics only. Each tag/skill window owns a separate instance and catalog loader.
public abstract class SelectionDialogViewModel : ViewModelBase
{
    private readonly Func<Task<IReadOnlyList<string>>> fnLoad;
    private List<SelectionValueViewModel> lstValues = [];
    private string strCurrentSearch = "";
    private string strCurrentStatus = "";
    private int iCurrentFilter;
    private int iCurrentSort;
    private bool bCurrentLoading;
    private int iLoadRevision;
    public abstract string strTitle { get; }
    public abstract string strAddLabel { get; }
    public string strSearch { get => strCurrentSearch; set { if (Set(ref strCurrentSearch, value ?? "")) Filter(); } }
    public string strStatus { get => strCurrentStatus; private set => Set(ref strCurrentStatus, value); }
    public int iSelectionFilter { get => iCurrentFilter; set { if (Set(ref iCurrentFilter, value)) Filter(); } }
    public int iSort { get => iCurrentSort; set { if (Set(ref iCurrentSort, value)) Filter(); } }
    public bool bLoading { get => bCurrentLoading; private set => Set(ref bCurrentLoading, value); }
    public IReadOnlyList<SelectionValueViewModel> lstSelected => lstValues.Where(vmValue => vmValue.bSelected).ToArray();
    public IReadOnlyList<SelectionValueViewModel> lstVisible { get; private set; } = [];
    public ICommand cmdAddValue { get; }

    protected SelectionDialogViewModel(IEnumerable<string> lstSelected, Func<Task<IReadOnlyList<string>>> fnLoad)
    {
        this.fnLoad = fnLoad;
        lstValues = lstSelected.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(strValue => new SelectionValueViewModel(strValue, true, Filter)).ToList();
        cmdAddValue = new RelayCommand(AddValue); Filter();
    }

    public async Task LoadAsync()
    {
        var iRevision = ++iLoadRevision; bLoading = true;
        try
        {
            var lstAvailable = await fnLoad();
            if (iRevision != iLoadRevision) return;
            foreach (var strValue in lstAvailable)
                if (!lstValues.Any(vmValue => string.Equals(vmValue.strValue, strValue, StringComparison.OrdinalIgnoreCase)))
                    lstValues.Add(new SelectionValueViewModel(strValue, false, Filter));
            Filter();
        }
        catch (Exception objException) when (objException is InvalidOperationException or UnauthorizedAccessException or IOException)
        { if (iRevision == iLoadRevision) strStatus = "The available choices could not be loaded. Close this window and try again."; }
        finally { if (iRevision == iLoadRevision) bLoading = false; }
    }

    private void Filter()
    {
        var lstFiltered = lstValues.Where(vmValue => vmValue.strValue.Contains(strSearch.Trim(), StringComparison.OrdinalIgnoreCase)
            && (iSelectionFilter == 0 || (iSelectionFilter == 1 ? vmValue.bSelected : !vmValue.bSelected)));
        lstVisible = (iSort == 1 ? lstFiltered.OrderByDescending(vmValue => vmValue.strValue, StringComparer.OrdinalIgnoreCase)
            : lstFiltered.OrderBy(vmValue => vmValue.strValue, StringComparer.OrdinalIgnoreCase)).ToArray();
        Notify(nameof(lstVisible)); Notify(nameof(lstSelected));
    }

    private void AddValue()
    {
        if (bLoading) return;
        var strValue = strSearch.Trim();
        if (strValue.Length is < 1 or > 64 || strValue.Any(chrValue => char.IsControl(chrValue) || chrValue == ','))
        { strStatus = "Enter one name with 1–64 characters, without commas or control characters."; return; }
        var vmExisting = lstValues.FirstOrDefault(vmValue => string.Equals(vmValue.strValue, strValue, StringComparison.OrdinalIgnoreCase));
        if (vmExisting is null) lstValues.Add(new SelectionValueViewModel(strValue, true, Filter));
        else vmExisting.bSelected = true;
        strStatus = ""; strSearch = ""; Filter();
    }

    public IReadOnlyList<string> SelectedValues() => lstSelected.Select(vmValue => vmValue.strValue).ToArray();
    public void Clear()
    {
        iLoadRevision++; lstValues = []; strSearch = ""; strStatus = ""; bLoading = false; Filter();
    }
}
