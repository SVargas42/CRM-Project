namespace HippoCrm.Desktop.ViewModels;

public sealed class SkillSelectionViewModel(IEnumerable<string> lstSelectedSkills, Func<Task<IReadOnlyList<string>>> fnLoadSkills)
    : SelectionDialogViewModel(lstSelectedSkills, fnLoadSkills)
{
    public override string strTitle => "Add Skills";
    public override string strAddLabel => "Add skill";
}
