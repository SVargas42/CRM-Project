namespace HippoCrm.Desktop.ViewModels;

// Presentation only: no image paths, file pickers or persistence operations.
public sealed class ProfileHeaderViewModel : ViewModelBase
{
    public ProfileHeaderViewModel(MainWindowViewModel vmWorkspace, bool bContact)
    {
        this.vmWorkspace = vmWorkspace; this.bContact = bContact;
    }
    public MainWindowViewModel vmWorkspace { get; }
    public bool bContact { get; }
    public bool bOrganization => !bContact;
    public bool bCanUpload => false;
    public string strPlaceholder => bContact ? "No picture" : "No logo";
    public string strUploadStatus => "Image upload is not connected yet.";
}
