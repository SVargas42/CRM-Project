using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HippoCrm.Core;
using HippoCrm.Desktop;
using HippoCrm.Desktop.ViewModels;

internal static class UiDevelopmentChecks
{
    private static int iPassed;
    internal static void Run()
    {
#if DEBUG
        RunDebug();
#else
        Check(typeof(App).Assembly.GetType("HippoCrm.Desktop.DebugUiDevelopment") is null, "Release assembly excludes the UI development composition");
        Check(typeof(App).Assembly.GetType("HippoCrm.Desktop.MockFipsValidator") is null, "Release assembly excludes the mock FIPS adapter");
        Check(typeof(App).Assembly.GetType("HippoCrm.Desktop.MockManifestVerifier") is null, "Release assembly excludes the mock manifest adapter");
        Check(typeof(Database).GetMethod("CreateUiDevelopmentWorkspace") is null, "Release Core excludes the UI memory-workspace factory");
        Check(typeof(Database).GetMethod("OpenUiDevelopmentWorkspace") is null, "Release Core excludes the persistent Debug workspace factory");
        Check(!typeof(Database).Assembly.GetTypes().Any(typValue => typValue.Name.StartsWith("UiDevelopment", StringComparison.Ordinal)), "Release Core excludes fixture storage/key/checkpoint types");
        Reject<CryptographicException>(() => StartupSecurity.Verify(), "Release startup retains unprovisioned fail-closed verification");
        Reject<CryptographicException>(() => new App().Initialize(), "Release Avalonia initialization cannot bypass provisioning");
#endif
        Console.WriteLine($"{iPassed} UI development isolation checks passed.");
    }

#if DEBUG
    private static readonly Type typDevelopment = typeof(App).Assembly.GetType("HippoCrm.Desktop.DebugUiDevelopment")!;
    internal static object? Invoke(string strMethod, params object?[] arrArguments)
    {
        try { return typDevelopment.GetMethod(strMethod, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arrArguments); }
        catch (TargetInvocationException objException) when (objException.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(objException.InnerException).Throw(); throw; }
    }
    private static void RunDebug()
    {
        Check(typDevelopment is not null, "Debug contains the opt-in UI composition");
        Reject<CryptographicException>(() => new App().Initialize(), "Debug remains fail closed before explicit opt-in");
        Check(Invoke("TryEnable", (object)new[] { "--ui-development", "--other" }) is false, "Mixed command arguments do not enable the mock");
        Check(Invoke("TryEnable", (object)new[] { "--ui-development" }) is true, "Explicit standalone development flag enables the mock");
        Invoke("ConfigureWorkspaceFactory", (Func<Database>)Database.CreateUiDevelopmentWorkspace);
        Reject<CryptographicException>(() => StartupSecurity.Verify(), "UI mock does not mutate the production startup gate");
        var strSentinelRoot = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "ui-production-sentinel-" + Guid.NewGuid().ToString("N"));
        var strSentinelPath = Path.Combine(strSentinelRoot, "crm.db");
        var strPreviousPath = Environment.GetEnvironmentVariable("HIPPOCRM_DATABASE_PATH");
        Environment.SetEnvironmentVariable("HIPPOCRM_DATABASE_PATH", strSentinelPath);
        AppBuilder.Configure<App>().UseSkia().WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var vmWorkspace = new MainWindowViewModel();
        MainWindow? frmMain = null;
        try
        {
            Complete(vmWorkspace.tskInitialization);
            Check(vmWorkspace.strPage == "Login" && vmWorkspace.bSetup && !vmWorkspace.bBusy, "Actual Avalonia app and workspace reach account setup without provisioning");
            var objDatabase = (Database)typeof(MainWindowViewModel).GetField("objDatabase", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vmWorkspace)!;
            Check(objDatabase.Path == ":memory:" && !Directory.Exists(strSentinelRoot), "Development ignores production workspace configuration and uses memory only");
            Reject<InvalidOperationException>(() => Invoke("Workspace", objDatabase), "Development refuses externally supplied workspaces");
            Reject<CryptographicException>(() => new Database(strSentinelPath), "Normal Database constructors remain gated while the UI mock is active");
            Check(!Directory.Exists(strSentinelRoot), "Rejected normal open creates no workspace directory");
            frmMain = new MainWindow(vmWorkspace); frmMain.Show();
            Check(frmMain.Icon is not null && frmMain.GetLogicalDescendants().OfType<Image>().Any(imgLogo => imgLogo.Name == "imgLoginLogo" && imgLogo.Source is not null), "Bundled logo and window icon load on the setup screen");
            Check(frmMain.Title!.Contains("UI DEVELOPMENT", StringComparison.Ordinal) && frmMain.GetLogicalDescendants().OfType<TextBlock>().Any(txtValue => txtValue.Text?.Contains("NO OPERATIONAL DATA", StringComparison.Ordinal) == true), "Development window is visibly marked on the setup screen");
            var imgLogin = frmMain.GetLogicalDescendants().OfType<Image>().Single(imgLogo => imgLogo.Name == "imgLoginLogo");
            Check(imgLogin.Parent is Border bdrLoginBrand && bdrLoginBrand.Width == 112 && bdrLoginBrand.Padding == new Thickness(0) && bdrLoginBrand.ClipToBounds && imgLogin.RenderTransform is Avalonia.Media.ScaleTransform objLogoScale && objLogoScale.ScaleX == 1.12 && objLogoScale.ScaleY == 1.12 && bdrLoginBrand.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center, "Login logo is larger and centered");
            CaptureBrand("login-light.png", Avalonia.Styling.ThemeVariant.Light);
            CaptureBrand("login-dark.png", Avalonia.Styling.ThemeVariant.Dark);
            vmWorkspace.strUsername = "ui-fixture";
            foreach (var chrValue in "FICTIONAL UI-only password!") { vmWorkspace.secPassword.AppendChar(chrValue); vmWorkspace.secConfirmation.AppendChar(chrValue); }
            Execute(vmWorkspace.cmdSignIn);
            Check(vmWorkspace.strPage == "Overview" && vmWorkspace.bCanCreate, $"Fixture administrator can enter the real UI with CUI-authorized fixture services (page={vmWorkspace.strPage}; create={vmWorkspace.bCanCreate}; message={vmWorkspace.strMessage})");
            var imgSidebar = frmMain.FindControl<Image>("imgSidebarLogo")!;
            Check(imgSidebar.Source is not null && imgSidebar.Parent is Border bdrBrand && bdrBrand.Width == 100 && bdrBrand.Height == 100, "Sidebar logo uses the larger square badge");
            CaptureBrand("workspace-dark.png", Avalonia.Styling.ThemeVariant.Dark);
            CaptureBrand("workspace-light.png", Avalonia.Styling.ThemeVariant.Light);
            var bdrSidebar = frmMain.FindControl<Border>("bdrSidebar")!;
            var ptLogoCenter = imgSidebar.TranslatePoint(new Point(imgSidebar.Bounds.Width / 2, imgSidebar.Bounds.Height / 2), bdrSidebar)!.Value;
            Check(bdrSidebar.Width == 232 && Math.Abs(ptLogoCenter.X - bdrSidebar.Bounds.Width / 2) < 1, "Sidebar logo is centered across the full sidebar rather than the space beside its toggle");
            var btnToggle = frmMain.FindControl<Button>("btnSidebarToggle")!;
            btnToggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(!frmMain.FindControl<StackPanel>("pnlSidebarBrandExpanded")!.IsVisible, "Collapsing the sidebar hides the logo without obstructing navigation");
            btnToggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(frmMain.FindControl<StackPanel>("pnlSidebarBrandExpanded")!.IsVisible, "Expanding the sidebar restores the brand");
            Execute(vmWorkspace.cmdContacts); Execute(vmWorkspace.cmdNewContact);
            vmWorkspace.objContact!.FirstName = "FICTIONAL"; vmWorkspace.objContact.LastName = "UI preview";
            Execute(vmWorkspace.cmdSaveContact);
            Check(vmWorkspace.lstContacts.Any(objValue => objValue.LastName == "UI preview"), $"Contact UI save and lookup work against the memory fixture (page={vmWorkspace.strPage}; id={vmWorkspace.objContact?.Id}; message={vmWorkspace.strMessage})");
            Execute(vmWorkspace.cmdOrganizations); Execute(vmWorkspace.cmdNewOrganization);
            vmWorkspace.objOrganization!.Name = "FICTIONAL UI organization";
            Execute(vmWorkspace.cmdSaveOrganization);
            Check(vmWorkspace.lstOrganizations.Any(objValue => objValue.Name == "FICTIONAL UI organization"), "Organization UI save and lookup work against the memory fixture");
            Execute(vmWorkspace.cmdLock);
            Check(vmWorkspace.strPage == "Login" && vmWorkspace.lstContacts.Count == 0, "Real lock workflow obscures fixture data and revokes the session");
            foreach (var chrValue in "FICTIONAL UI-only password!") vmWorkspace.secPassword.AppendChar(chrValue);
            Execute(vmWorkspace.cmdSignIn);
            Check(vmWorkspace.strPage == "Overview", "Real reauthentication returns to the dashboard");
            Execute(vmWorkspace.cmdContacts);
            Check(vmWorkspace.lstContacts.Any(objValue => objValue.LastName == "UI preview"), "Real reauthentication preserves only the current process's memory fixture");
            Check(!Directory.Exists(strSentinelRoot), "UI create/save/lock/sign-in never creates the configured production workspace");
            var objMemory = typeof(Database).GetField("objUiDevelopment", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(objDatabase)!;
            var objKeys = objMemory.GetType().GetField("objKeys", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(objMemory)!;
            var arrKey = (byte[])objKeys.GetType().GetField("arrKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(objKeys)!;
            Complete(vmWorkspace.CloseAsync());
            Invoke("DisposeWorkspace");
            Check(arrKey.All(bytValue => bytValue == 0), "Fixture disposal clears the original pinned audit key");
            Reject<ObjectDisposedException>(() => objDatabase.ReadDarkTheme(), "Disposed fixture cannot reopen");
            using var objFresh = Database.CreateUiDevelopmentWorkspace();
            Check(new AuthService(objFresh).NeedsSetup, "A fresh fixture contains none of the previous process workspace's accounts");
            void CaptureBrand(string strFile, Avalonia.Styling.ThemeVariant objTheme)
            {
                Application.Current!.RequestedThemeVariant = objTheme;
                frmMain.WindowState = WindowState.Normal; frmMain.Width = 1100; frmMain.Height = 850;
                Dispatcher.UIThread.RunJobs(); frmMain.UpdateLayout();
                var strFolder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "branding-preview"); Directory.CreateDirectory(strFolder);
                using var objBitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1100, 850));
                objBitmap.Render(frmMain); objBitmap.Save(Path.Combine(strFolder, strFile));
            }
        }
        finally
        {
            Complete(vmWorkspace.CloseAsync()); frmMain?.Close(); Invoke("DisposeWorkspace");
            Environment.SetEnvironmentVariable("HIPPOCRM_DATABASE_PATH", strPreviousPath);
        }
    }
    internal static void Execute(System.Windows.Input.ICommand cmdCommand)
    { cmdCommand.Execute(null); if (cmdCommand is AsyncRelayCommand cmdAsync) Complete(cmdAsync.tskExecution); }
    internal static void Complete(Task tskWork)
    {
        var swWait = Stopwatch.StartNew();
        while (!tskWork.IsCompleted && swWait.Elapsed < TimeSpan.FromSeconds(15)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        if (!tskWork.IsCompleted) throw new TimeoutException("UI development check timed out.");
        tskWork.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
#endif
    private static void Check(bool bCondition, string strMessage)
    { if (!bCondition) throw new InvalidOperationException("FAIL " + strMessage); iPassed++; Console.WriteLine("PASS " + strMessage); }
    private static void Reject<T>(Action actCheck, string strMessage) where T : Exception
    { try { actCheck(); } catch (T) { Check(true, strMessage); return; } throw new InvalidOperationException("FAIL " + strMessage); }
}
