using System.Reflection;
using System.Runtime.InteropServices;
using HippoCrm.Core;
using HippoCrm.Desktop.ViewModels;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

internal static class FoundationChecks
{
    private static int iPassed;
    public static int Main(string[] arrArguments)
    {
        try { return Run(arrArguments); }
        catch (Exception objException) { Console.Error.WriteLine(objException.Message); return 1; }
    }
    private static int Run(string[] arrArguments)
    {
        ValidateSources(arrArguments.Length == 1 ? Path.GetFullPath(arrArguments[0]) : Directory.GetCurrentDirectory());
        PasswordMemoryChecks.Run();
        var vmProbe = new ProbeViewModel();
        var iNotifications = 0;
        vmProbe.PropertyChanged += (objSender, objArguments) =>
        {
            if (objArguments.PropertyName == nameof(vmProbe.strValue)) iNotifications++;
        };
        vmProbe.strValue = "fictional";
        Assert(iNotifications == 1, "Changed properties notify bindings");
        vmProbe.strValue = "fictional";
        Assert(iNotifications == 1, "Unchanged properties do not notify again");
        var bExecuted = false;
        var cmdProbe = new RelayCommand(() => bExecuted = true);
        Assert(cmdProbe.CanExecute(null), "Synchronous foundation command is executable");
        cmdProbe.Execute(null);
        Assert(bExecuted, "Command dispatch executes the workflow");
        var typCredential = typeof(WindowsCredentialKeyStore).GetNestedType("NativeCredential", BindingFlags.NonPublic)!;
        Assert(Marshal.SizeOf(typCredential) == (IntPtr.Size == 8 ? 80 : 52), "Native credential structure has the Windows ABI size");
        Assert(Marshal.OffsetOf(typCredential, "ptrCredentialBlob").ToInt32() == (IntPtr.Size == 8 ? 40 : 28), "Credential secret pointer has the Windows ABI offset");
        var mthTarget = typeof(WindowsCredentialKeyStore).GetMethod("Target", BindingFlags.NonPublic | BindingFlags.Static)!;
        var strFirstTarget = (string)mthTarget.Invoke(null, [Path.GetFullPath("artifacts/foundation-checks/fictional.db")])!;
        var strSameTarget = (string)mthTarget.Invoke(null, [Path.GetFullPath("artifacts/foundation-checks/FICTIONAL.db")])!;
        var strOtherTarget = (string)mthTarget.Invoke(null, [Path.GetFullPath("artifacts/foundation-checks/other.db")])!;
        Assert(strFirstTarget == strSameTarget, "Windows path casing does not create a different key target");
        Assert(strFirstTarget != strOtherTarget, "Different database paths receive distinct key targets");
        Console.WriteLine($"{iPassed} foundation checks passed; no credential-store writes performed.");
        return 0;
    }

    private static void ValidateSources(string strRepository)
    {
        var arrPaths = new[]
        {
            "CRM.cs", "src/HippoCrm.Desktop/MainWindow.cs", "src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs",
            "src/HippoCrm.Desktop/ViewModels/ViewModelBase.cs", "src/HippoCrm.Core/Database.cs",
            "src/HippoCrm.Core/WindowsCredentialKeyStore.cs", "tests/Shared/EphemeralKeyStore.cs",
            "tests/HippoCrm.Tests/EncryptionChecks.cs", "tests/HippoCrm.FoundationChecks/Program.cs",
            "src/HippoCrm.Core/AuthService.cs", "src/HippoCrm.Core/CrmService.cs", "src/HippoCrm.Core/AccessControl.cs",
            "src/HippoCrm.Core/SecurityAudit.cs", "src/HippoCrm.Core/UserAdministrationService.cs", "src/HippoCrm.Core/PasswordBuffer.cs",
            "src/HippoCrm.Core/Session.cs", "src/HippoCrm.Core/SchemaManager.cs", "src/HippoCrm.Desktop/SecurePasswordInput.cs",
            "tests/Shared/TestPasswords.cs", "tests/HippoCrm.SecurityChecks/WindowsSqliteAdapter.cs", "tests/HippoCrm.SecurityChecks/TestDatabase.cs",
            "tests/HippoCrm.SecurityChecks/Program.cs", "tests/HippoCrm.SecurityChecks/SecurityPolicyChecks.cs",
            "tests/HippoCrm.FoundationChecks/PasswordMemoryChecks.cs", "tests/HippoCrm.SecurityChecks/ViewModelSecurityChecks.cs"
        };
        var rgxHungarian = new Regex("^(str|i|lng|ui|b|byt|arr|lst|obj|vm|cmd|act|fn|dt|dec|gch|ptr|spn|mtx|stm|rdr|db|csb|tx|ft|typ|mth|tmr|ctr|ctl|txt|btn|tgl|chk|cbo|tab|pnl|grd|bdr|rgx|sec|chr|en|dic)[A-Z][A-Za-z0-9]*$");
        foreach (var strRelativePath in arrPaths)
        {
            var strSource = File.ReadAllText(Path.Combine(strRepository, strRelativePath));
            var objTree = CSharpSyntaxTree.ParseText(strSource);
            Assert(!objTree.GetDiagnostics().Any(objDiagnostic => objDiagnostic.Severity == DiagnosticSeverity.Error), "C# syntax: " + strRelativePath);
            var lstIdentifiers = objTree.GetRoot().DescendantNodes().Select(objNode => objNode switch
            {
                // Interface event names are fixed by .NET; they are not application fields/variables.
                VariableDeclaratorSyntax objVariable when objVariable.Parent?.Parent is not EventFieldDeclarationSyntax => objVariable.Identifier.ValueText,
                ParameterSyntax objParameter => objParameter.Identifier.ValueText,
                SingleVariableDesignationSyntax objDesignation => objDesignation.Identifier.ValueText,
                CatchDeclarationSyntax objCatch => objCatch.Identifier.ValueText,
                ForEachStatementSyntax objLoop => objLoop.Identifier.ValueText,
                _ => ""
            }).Where(strIdentifier => strIdentifier.Length > 0);
            foreach (var strIdentifier in lstIdentifiers)
                if (!rgxHungarian.IsMatch(strIdentifier))
                    throw new InvalidOperationException($"Hungarian notation: {strRelativePath}: {strIdentifier}");
            Assert(true, "Hungarian declarations: " + strRelativePath);
        }
        var strViewSource = File.ReadAllText(Path.Combine(strRepository, "src/HippoCrm.Desktop/MainWindow.cs"));
        Assert(!Regex.IsMatch(strViewSource, @"new\s+(Database|AuthService|CrmService)\s*\(|\.Open\s*\(|CommandText"), "View does not create services or perform SQL operations");
        var strViewModelSource = File.ReadAllText(Path.Combine(strRepository, "src/HippoCrm.Desktop/ViewModels/MainWindowViewModel.cs"));
        Assert(!Regex.IsMatch(strViewModelSource, @"using\s+Avalonia|\b(TextBox|Window|Control|Button|DispatcherTimer)\b"), "View model has no Avalonia UI types");
    }
    private static void Assert(bool bCondition, string strDescription)
    {
        if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
        iPassed++; Console.WriteLine("PASS " + strDescription);
    }
    private sealed class ProbeViewModel : ViewModelBase
    {
        private string strCurrentValue = "";
        public string strValue { get => strCurrentValue; set => Set(ref strCurrentValue, value); }
    }
}
