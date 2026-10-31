namespace HippoCrm.Desktop.ViewModels;

public sealed class TagSelectionViewModel(IEnumerable<string> lstSelectedTags, Func<Task<IReadOnlyList<string>>> fnLoadTags)
    : SelectionDialogViewModel(lstSelectedTags, fnLoadTags)
{
    public override string strTitle => "Add Tags";
    public override string strAddLabel => "Add tag";
}
