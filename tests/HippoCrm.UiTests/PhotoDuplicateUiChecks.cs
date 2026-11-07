using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.Views;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.TestSupport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

internal static class PhotoDuplicateUiChecks
{
    internal static void Run()
    {
#if !DEBUG
        throw new InvalidOperationException("Use Debug for the isolated UI fixture.");
#else
        var iChecks=0;
        void Check(bool bValue,string strMessage) { if(!bValue) throw new InvalidOperationException("FAIL "+strMessage); iChecks++; Console.WriteLine("PASS "+strMessage); }
        void Complete(Task tskTask)=>UiDevelopmentChecks.Complete(tskTask);
        void Execute(System.Windows.Input.ICommand cmdAction)=>UiDevelopmentChecks.Execute(cmdAction);
        void Until(Func<bool> fnReady)
        { var swWait=Stopwatch.StartNew(); while(!fnReady() && swWait.Elapsed<TimeSpan.FromSeconds(15)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); } if(!fnReady()) throw new TimeoutException("UI review not ready"); }
        UiDevelopmentChecks.Invoke("TryEnable",(object)new[]{"--ui-development"});
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory",(Func<Database>)Database.CreateUiDevelopmentWorkspace);
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing=false }).SetupWithoutStarting();
        var vmMain=new MainWindowViewModel(); Complete(vmMain.tskInitialization); var frmMain=new MainWindow(vmMain); frmMain.Show();
        try
        {
            vmMain.strAccount="fixture-ui"; foreach(var chrValue in "FICTIONAL photo UI password!") { vmMain.secPassword.AppendChar(chrValue); vmMain.secConfirmation.AppendChar(chrValue); } Execute(vmMain.cmdSignIn);
            var objDatabase=(Database)typeof(MainWindowViewModel).GetField("objDatabase",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(vmMain)!;
            var objAuth=new AuthService(objDatabase); using var secPassword=TestPasswords.Secret("FICTIONAL photo UI password!"); using var objSession=objAuth.SignIn("fixture-ui",secPassword); var objCrm=new CrmService(objDatabase,objSession);
            var objFirst=new Contact { FirstName="FICTIONAL",LastName="First",Email="duplicate@example.test" }; objCrm.Save(objFirst);
            Execute(vmMain.cmdContacts); var vmContacts=(ContactsPageViewModel)vmMain.vmShell.vmCurrentPage; Complete(vmContacts.tskSearch); Execute(vmContacts.lstRows.Single().cmdViewProfile);
            var strDraft=vmMain.objContact!.FirstName; vmMain.objContact.FirstName="Unsubmitted fictional edit";
            var arrPhoto=Picture(); Complete(vmMain.ImportProfileImageAsync(true,ctCancellation=>Task.FromResult<byte[]?>(arrPhoto)));
            Check(vmMain.bHasContactImage && arrPhoto.All(bytValue=>bytValue==0),"Contact preview loads and imported bytes are cleared");
            Check(vmMain.objContact.FirstName=="Unsubmitted fictional edit","Photo update preserves unfinished profile fields");
            frmMain.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var lstPreviews=frmMain.GetVisualDescendants().OfType<SecureProfileImage>().Where(imgControl=>imgControl.IsEffectivelyVisible).ToArray();
            Check(lstPreviews.Any(imgControl=>imgControl.Source is not null),"Contact read-only preview renders in existing profile header");
            var arrPrevious=vmMain.arrContactImage!;
            Complete(vmMain.ImportProfileImageAsync(true,ctCancellation=>Task.FromResult<byte[]?>(new byte[]{137,80,78,71,13,10,26,10})));
            Check(ReferenceEquals(vmMain.arrContactImage,arrPrevious) && vmMain.bHasContactImage,"Bad image preserves existing preview");
            vmMain.objContact.FirstName=strDraft;
            vmContacts.iSelectedTab=1; Complete(vmContacts.tskTabChange); vmMain.objContact!.FirstName="FICTIONAL second";
            vmMain.vmNewContactEmails.strNewValue="duplicate@example.test"; Execute(vmMain.vmNewContactEmails.cmdAdd);
            vmMain.cmdSaveContact.Execute(null);
            Until(()=>frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Any());
            var frmReview=frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Single(); frmReview.UpdateLayout();
            Check(frmReview.FindControl<ListBox>("lstDuplicateMatches")!.ItemCount==1,"Creation shows permitted duplicate and matching reasons");
            Check(frmReview.FindControl<Button>("btnCancelDuplicate")!.IsFocused,"Review defaults focus to Keep editing");
            var strScreens=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","screenshots"); Directory.CreateDirectory(strScreens);
            using(var bmpScreen=new RenderTargetBitmap(new PixelSize((int)frmReview.Bounds.Width,(int)frmReview.Bounds.Height))) { bmpScreen.Render(frmReview); bmpScreen.Save(Path.Combine(strScreens,"duplicate-review.png")); }
            frmReview.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); Complete(vmMain.tskPendingOperation);
            Check(vmMain.objContact is { Id:0 } && vmMain.objContact.FirstName=="FICTIONAL second","Escape keeps unsaved draft without creating a record");
            vmMain.cmdSaveContact.Execute(null); Until(()=>frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Any());
            frmReview=frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Single(); frmReview.FindControl<Button>("btnIgnoreDuplicate")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Complete(vmMain.tskPendingOperation);
            Check(objCrm.Contacts().Count==2,"Explicit ignore creates a separate contact");
            Check(vmMain.strMessage.Contains("without linking"),"Save message clearly confirms a separate record");
            var objSecond=objCrm.Contacts().Single(objRow=>objRow.Id!=objFirst.Id); Complete(vmMain.SelectContactAsync(objSecond));
            vmMain.cmdSaveContact.Execute(null); Until(()=>frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Any());
            frmReview=frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Single(); frmReview.FindControl<ListBox>("lstDuplicateMatches")!.SelectedIndex=0; Dispatcher.UIThread.RunJobs();
            Check(frmReview.FindControl<Button>("btnLinkDuplicate")!.IsEnabled,"Selecting a writable match enables explicit linking");
            frmReview.FindControl<Button>("btnLinkDuplicate")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Complete(vmMain.tskPendingOperation);
            Check(vmMain.strMessage.Contains("not been combined") && vmMain.strContactLinks.Contains("not been combined"),"Link confirmation explicitly says records were not combined");
            Check(objCrm.LinkedRecords(RecordKind.Contact,objSecond.Id).Count==1 && vmMain.strContactLinks.Contains("FICTIONAL"),"Review and Link retains a visible authorized relationship");
            // Keyboard override uses the platform command modifier, never bare Enter.
            vmMain.cmdSaveContact.Execute(null); Until(()=>frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Any()); frmReview=frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Single();
            var enModifier=OperatingSystem.IsMacOS()?RawInputModifiers.Meta:RawInputModifiers.Control;
            frmReview.KeyPressQwerty(PhysicalKey.Enter,enModifier); Complete(vmMain.tskPendingOperation);
            Check(objCrm.Contacts().Count==2,"Command/Ctrl+Enter confirms override without multiplying existing records");
            var arrCached=vmMain.arrContactImage;
            vmMain.cmdSaveContact.Execute(null); Until(()=>frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Any()); frmReview=frmMain.OwnedWindows.OfType<DuplicateReviewWindow>().Single();
            Execute(vmMain.cmdLock); Complete(vmMain.tskPendingOperation);
            Check(vmMain.strPage=="Login" && !frmReview.IsVisible && vmMain.arrContactImage is null,"Lock obscures review sheet and clears preview references");
            Check(arrCached is null || arrCached.All(bytValue=>bytValue==0),"Lock clears original cached picture buffer");
            foreach(var chrValue in "FICTIONAL photo UI password!") vmMain.secPassword.AppendChar(chrValue); Execute(vmMain.cmdSignIn);
            Execute(vmMain.cmdContacts); Complete(vmMain.SelectContactAsync(objFirst));
            var objLate=new TaskCompletionSource<byte[]?>(); var tskImport=vmMain.ImportProfileImageAsync(true,ctCancellation=>objLate.Task);
            Dispatcher.UIThread.RunJobs(); Execute(vmMain.cmdLock); Complete(tskImport);
            var arrLate=Picture(); objLate.SetResult(arrLate); Until(()=>arrLate.All(bytValue=>bytValue==0));
            Check(vmMain.strPage=="Login" && !vmMain.bHasContactImage,"Late picker response cannot restore a locked preview");
            Check(arrLate.All(bytValue=>bytValue==0),"Late canceled picker payload is cleared");
            foreach(var chrValue in "FICTIONAL photo UI password!") vmMain.secPassword.AppendChar(chrValue); Execute(vmMain.cmdSignIn);
            var objOrg=new Organization { Name="FICTIONAL logo company" }; objCrm.Save(objOrg); Execute(vmMain.cmdOrganizations);
            var vmOrganizations=(OrganizationsPageViewModel)vmMain.vmShell.vmCurrentPage; Complete(vmOrganizations.tskSearch); Execute(vmOrganizations.lstRows.Single().cmdViewProfile);
            var arrLogo=Picture(); Complete(vmMain.ImportProfileImageAsync(false,ctCancellation=>Task.FromResult<byte[]?>(arrLogo)));
            Check(vmMain.bHasOrganizationImage && arrLogo.All(bytValue=>bytValue==0) && vmMain.arrContactImage is null,"Company logo loads independently and clears source/contact preview");
            frmMain.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Check(frmMain.GetVisualDescendants().OfType<SecureProfileImage>().Any(imgControl=>imgControl.IsEffectivelyVisible && imgControl.Source is not null),"Company logo renders in existing organization profile");
            Execute(vmMain.cmdContacts); Check(vmMain.arrOrganizationImage is null,"Navigation clears company logo bytes");
            var strRoot=Path.Combine(Directory.GetCurrentDirectory(),"artifacts","photo-input-fixtures"); Directory.CreateDirectory(strRoot);
            var strInput=Path.Combine(strRoot,"fictional.png"); File.WriteAllBytes(strInput,Picture());
            var arrLocal=LocalImageImport.ReadAsync(strInput).GetAwaiter().GetResult(); Check(arrLocal.Length>0,"Local bounded reader accepts fixture picture"); CryptographicOperations.ZeroMemory(arrLocal);
            var bNetworkDenied=false; try { LocalImageImport.ValidateLocalPath(@"\\invalid-host\share\picture.png"); } catch(ArgumentException) { bNetworkDenied=true; } Check(bNetworkDenied,"UNC source rejected before file access");
            var strCloud=Path.Combine(strRoot,"OneDrive-fixture"); Directory.CreateDirectory(strCloud); File.Copy(strInput,Path.Combine(strCloud,"fictional.png"),true);
            var bCloudDenied=false; try { LocalImageImport.ReadAsync(Path.Combine(strCloud,"fictional.png")).GetAwaiter().GetResult(); } catch(ArgumentException) { bCloudDenied=true; } Check(bCloudDenied,"Cloud-folder source rejected");
            using(var stmLocked=new FileStream(strInput,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) { var bLockedDenied=false; try { LocalImageImport.ReadAsync(strInput).GetAwaiter().GetResult(); } catch(ArgumentException) { bLockedDenied=true; } Check(bLockedDenied,"Locked input file produces safe failure"); }
            using(var objCancellation=new CancellationTokenSource()) { objCancellation.Cancel(); var bCanceled=false; try { LocalImageImport.ReadAsync(strInput,objCancellation.Token).GetAwaiter().GetResult(); } catch(OperationCanceledException) { bCanceled=true; } Check(bCanceled,"Canceled source read fails without retaining data"); }
            Console.WriteLine($"{iChecks} photo/duplicate UI checks passed.");
        }
        finally { Complete(vmMain.CloseAsync()); frmMain.Close(); UiDevelopmentChecks.Invoke("DisposeWorkspace"); }
#endif
    }
    private static byte[] Picture()
    { using var imgPicture=new SixLabors.ImageSharp.Image<Rgba32>(2,2); using var stmPicture=new MemoryStream(); imgPicture.SaveAsPng(stmPicture); return stmPicture.ToArray(); }
}
