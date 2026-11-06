namespace HippoCrm.Desktop.ViewModels;

public sealed class SkillSelectionViewModel(IEnumerable<string> lstSelectedSkills, Func<Task<IReadOnlyList<string>>> fnLoadSkills)
    : SelectionDialogViewModel(lstSelectedSkills, fnLoadSkills)
{
    public override string strValueKind => "Skill";
    public override string strTitle => "Add Skills";
    public override string strAddLabel => "Add skill";
}

// Shares selection mechanics and the existing window, with service-only labels/catalog.
public sealed class ServiceSelectionViewModel(IEnumerable<string> lstSelectedServices, Func<Task<IReadOnlyList<string>>> fnLoadServices)
    : SelectionDialogViewModel(lstSelectedServices, fnLoadServices)
{
    public override string strValueKind => "Service";
    public override string strTitle => "Add Services";
    public override string strAddLabel => "Add service";
}