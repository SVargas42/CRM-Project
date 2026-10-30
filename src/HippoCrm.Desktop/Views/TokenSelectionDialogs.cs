using System.ComponentModel;
using Avalonia.Controls;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

internal static class TokenSelectionDialogs
{
    public static async Task ManageTagsAsync(UserControl ctlOwner, MainWindowViewModel vmMain, bool bContact)
    {
        if (!CanEdit(vmMain, bContact)) return;
        var objContact = vmMain.objContact; var objOrganization = vmMain.objOrganization;
        var vmTags = new TagSelectionViewModel(bContact ? vmMain.lstContactTags : vmMain.lstOrganizationTags, vmMain.LoadTagChoicesAsync);
        var lstResult = await ShowAsync(ctlOwner, vmMain, new TagSelectionWindow(vmTags), vmTags);
        if (lstResult is not null) vmMain.ApplyTags(bContact, objContact, objOrganization, lstResult);
    }

    public static async Task ManageSkillsAsync(UserControl ctlOwner, MainWindowViewModel vmMain, bool bContact)
    {
        if (!CanEdit(vmMain, bContact)) return;
        var objContact = vmMain.objContact; var objOrganization = vmMain.objOrganization;
        var vmSkills = new SkillSelectionViewModel(bContact ? vmMain.lstContactSkills : vmMain.lstOrganizationSkills, vmMain.LoadSkillChoicesAsync);
        var lstResult = await ShowAsync(ctlOwner, vmMain, new SkillSelectionWindow(vmSkills), vmSkills);
        if (lstResult is not null) vmMain.ApplySkills(bContact, objContact, objOrganization, lstResult);
    }

    private static bool CanEdit(MainWindowViewModel vmMain, bool bContact) => !vmMain.bBusy && !vmMain.bLocking
        && (bContact ? vmMain.bCanEditContact : vmMain.bCanEditOrganization);

    private static async Task<IReadOnlyList<string>?> ShowAsync(UserControl ctlOwner, MainWindowViewModel vmMain,
        Window dlgSelection, SelectionDialogViewModel vmSelection)
    {
        if (TopLevel.GetTopLevel(ctlOwner) is not Window frmOwner) return null;
        void OnWorkspaceChanged(object? objSender, PropertyChangedEventArgs objArguments)
        {
            if (vmMain.bLocking || vmMain.strPage is "Login" or "Error") { vmSelection.Clear(); dlgSelection.Close(null); }
        }
        vmMain.PropertyChanged += OnWorkspaceChanged;
        try { return await dlgSelection.ShowDialog<IReadOnlyList<string>?>(frmOwner); }
        finally { vmMain.PropertyChanged -= OnWorkspaceChanged; vmSelection.Clear(); }
    }
}
