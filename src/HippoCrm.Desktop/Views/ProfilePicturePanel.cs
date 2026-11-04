using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using HippoCrm.Desktop.ViewModels;

namespace HippoCrm.Desktop.Views;

public sealed class ProfilePicturePanel : UserControl
{
    private bool bCurrentContact;
    public bool bContact { get=>bCurrentContact; set { bCurrentContact=value; Build(); } }
    public ProfilePicturePanel() => Build();
    private void Build()
    {
        var imgPreview=new SecureProfileImage { Width=80,Height=80,Stretch=Avalonia.Media.Stretch.Uniform };
        imgPreview.Bind(SecureProfileImage.arrImageProperty,new Binding(bContact?"arrContactImage":"arrOrganizationImage"));
        Avalonia.Automation.AutomationProperties.SetName(imgPreview,bContact?"Contact photo preview":"Company logo preview");
        var btnChoose=new Button { Name="btnChooseProfilePicture",Content=bContact?"Choose photo…":"Choose logo…",HorizontalAlignment=HorizontalAlignment.Left };
        btnChoose.Bind(IsEnabledProperty,new Binding(bContact?"bCanUploadContactImage":"bCanUploadOrganizationImage"));
        btnChoose.Click+=async(objSender,objArguments)=>
        {
            if(DataContext is not MainWindowViewModel vmWorkspace || TopLevel.GetTopLevel(this) is not Window frmOwner) return;
            await vmWorkspace.ImportProfileImageAsync(bContact,async ctCancellation=>
            {
                if(!frmOwner.StorageProvider.CanOpen) throw new InvalidOperationException("Local picture selection is unavailable.");
                var lstFiles=await frmOwner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                { Title=bContact?"Choose contact photo":"Choose company logo",AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("PNG or JPEG") { Patterns=["*.png","*.jpg","*.jpeg"],MimeTypes=["image/png","image/jpeg"] }] }).WaitAsync(ctCancellation);
                if(lstFiles.Count==0) return null;
                if(!lstFiles[0].Path.IsFile || lstFiles[0].Path.IsUnc || lstFiles[0].TryGetLocalPath() is not { } strPath)
                    throw new ArgumentException("Choose a picture stored in a fully local folder.");
                return await LocalImageImport.ReadAsync(strPath,ctCancellation);
            });
        };
        var txtLinks=new TextBlock { TextWrapping=Avalonia.Media.TextWrapping.Wrap };
        txtLinks.Bind(TextBlock.TextProperty,new Binding(bContact?"strContactLinks":"strOrganizationLinks"));
        var pnlActions=new StackPanel { Spacing=8,Children={btnChoose,new TextBlock { Text="For saved records. PNG/JPEG up to 10 MB. Pictures save immediately; prior versions are retained.",TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=12 },txtLinks} };
        Grid.SetColumn(pnlActions,1);
        Content=new Border { Padding=new Thickness(16),CornerRadius=new CornerRadius(12),Child=new Grid { ColumnDefinitions=new ColumnDefinitions("80,*"),ColumnSpacing=16,Children={imgPreview,pnlActions} } };
        // Bound text stays inside its existing editor column at smaller window sizes.
        pnlActions.MaxWidth=420;
    }
}
