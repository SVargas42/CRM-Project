using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Data;
using HippoCrm.Desktop.ViewModels;
using HippoCrm.Core;
using HippoCrm.Desktop;

internal static class EnterSubmitChecks
{
    internal static void Run()
    {
#if DEBUG
        UiDevelopmentChecks.Invoke("TryEnable", (object)new[] { "--ui-development" });
        using var objDatabase = Database.CreateUiDevelopmentWorkspace();
        UiDevelopmentChecks.Invoke("ConfigureWorkspaceFactory", (Func<Database>)(() => objDatabase));
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var txtInput = new TextBox();
        var btnDefault = new Button { IsDefault = true, Content = "Save" };
        var pnlForm = new StackPanel(); pnlForm.Children.Add(txtInput); pnlForm.Children.Add(btnDefault);
        var frmWindow = new Window { Content = pnlForm };
        var cmdAdd = new ProbeCommand(); var iDefaults = 0; var iChecks = 0;
        btnDefault.Click += (_, _) => iDefaults++;
        EnterSubmit.SetCommand(txtInput, cmdAdd); EnterSubmit.SetCommandParameter(txtInput, "fixture");
        frmWindow.Show(); txtInput.Focus(); Dispatcher.UIThread.RunJobs();
        try
        {
            Down(); Down(); Check(cmdAdd.iCalls == 1 && iDefaults == 0 && Equals(cmdAdd.objParameter, "fixture"), "Enter executes the bound Add once and consumes default submission and key repeat");
            Up(); Down(); Up(); Check(cmdAdd.iCalls == 2, "Releasing Enter allows the next deliberate submission");
            cmdAdd.bAllowed = false; Down(); Up(); Check(cmdAdd.iCalls == 2 && iDefaults == 0, "Failed command validation leaves the form unsaved");
            cmdAdd.bAllowed = true; txtInput.IsReadOnly = true; Down(); Up(); Check(cmdAdd.iCalls == 2, "Read-only input cannot submit"); txtInput.IsReadOnly = false;
            txtInput.AcceptsReturn = true; Down(); Up(); Check(cmdAdd.iCalls == 2, "Multiline input retains Enter without invoking Add"); txtInput.AcceptsReturn = false;
            frmWindow.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control); frmWindow.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Check(cmdAdd.iCalls == 2, "Modified Enter does not invoke Add");
            var cmdReplacement = new ProbeCommand(); EnterSubmit.SetCommand(txtInput, cmdReplacement); Down(); Up(); Check(cmdReplacement.iCalls == 1 && cmdAdd.iCalls == 2, "Rebinding invokes only the replacement command");
            EnterSubmit.SetCommand(txtInput, null); Down(); Up(); Check(cmdReplacement.iCalls == 1, "Removing the behavior detaches Add handling");
            using var vmEmails = new MultiValueFieldViewModel("", ContactDetailsValidation.Email);
            txtInput.DataContext = vmEmails;
            txtInput.Bind(TextBox.TextProperty, new Binding("strNewValue", BindingMode.TwoWay));
            EnterSubmit.SetCommand(txtInput, vmEmails.cmdAdd);
            txtInput.Text = "invalid email"; Down(); Up();
            Check(vmEmails.lstValues.Count == 0 && vmEmails.bHasError && vmEmails.strNewValue == "invalid email", "Enter retains invalid email and displays existing field validation");
            txtInput.Text = "person@example.test"; Down(); Up();
            Check(vmEmails.lstValues.Count == 1 && !vmEmails.bHasError && txtInput.Text == "", "Enter adds a valid email through the existing bound field and clears its draft");
            txtInput.Text = "person@example.test"; Down(); Up();
            Check(vmEmails.lstValues.Count == 1, "Repeated email entry does not create a duplicate");
            using var vmPhones = new MultiValueFieldViewModel("", ContactDetailsValidation.Phone);
            txtInput.DataContext = vmPhones; EnterSubmit.SetCommand(txtInput, vmPhones.cmdAdd);
            txtInput.Text = "+44 20 7946 0958"; Down(); Up();
            Check(vmPhones.lstValues.Count == 1 && !vmPhones.bHasError, "International phone entry uses the existing validator");
            txtInput.Text = "nonsense"; Down(); Up();
            Check(vmPhones.lstValues.Count == 1 && vmPhones.bHasError, "Malformed phone entry cannot bypass validation with Enter");
            using var vmAddresses = new MultiValueFieldViewModel("");
            txtInput.DataContext = vmAddresses; EnterSubmit.SetCommand(txtInput, vmAddresses.cmdAdd);
            txtInput.Text = "10 Fictional Road"; Down(); Up();
            Check(vmAddresses.lstValues.Count == 1 && txtInput.Text == "", "Address submission follows the same Add workflow");
            var bVisible = true; var vmBar = new AnalyticsBarRow("Fictional organization", 2, 4, () => bVisible);
            Check(vmBar.dblPercent == 50 && vmBar.strCount == "2", "Chart bars scale exact counts without dividing by zero");
            bVisible = false; Check(vmBar.strLabel == "" && vmBar.strAccessible == "" && vmBar.dblPercent == 0, "Chart getters obscure retained data while access is unavailable");
            vmBar.Release(); bVisible = true;
            Check(vmBar.strLabel == "" && vmBar.strCount == "" && vmBar.dblPercent == 0, "Released chart rows cannot expose prior labels or counts");
            Console.WriteLine($"{iChecks} Enter submission and chart privacy checks passed.");
        }
        finally { frmWindow.Close(); }
        void Down() { frmWindow.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); }
        void Up() { frmWindow.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Dispatcher.UIThread.RunJobs(); }
        void Check(bool bCondition, string strMessage) { if (!bCondition) throw new InvalidOperationException(strMessage); iChecks++; Console.WriteLine("PASS " + strMessage); }
#else
        throw new InvalidOperationException("Use the isolated Debug keyboard fixture.");
#endif
    }
    private sealed class ProbeCommand : ICommand
    {
        internal int iCalls; internal bool bAllowed = true; internal object? objParameter;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? objValue) => bAllowed;
        public void Execute(object? objValue) { iCalls++; objParameter = objValue; }
    }
}
