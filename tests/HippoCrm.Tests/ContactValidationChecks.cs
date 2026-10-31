using HippoCrm.Core;
using HippoCrm.TestSupport;

internal static class ContactValidationChecks
{
    internal static void Run()
    {
        var iChecks = 0;
        foreach (var (strInput, strExpected) in new[]
        {
            (" user+team@EXAMPLE.COM ", "user+team@example.com"),
            ("first.last@example.co.uk", "first.last@example.co.uk"),
            ("ana@bücher.de", "ana@xn--bcher-kva.de"),
            ("élise@example.fr", "élise@example.fr")
        }) Check(ContactDetailsValidation.Email(strInput) == strExpected, "Accepted email format: " + strInput.Trim());
        foreach (var strInvalid in new[] { "", "not-an-email", "a@@example.com", "a@localhost", "a b@example.com",
            "a..b@example.com", ".a@example.com", "a.@example.com", "a@-example.com", "a@example-.com",
            "a@example..com", "Person <a@example.com>", "a@example.com\nBcc: other@example.com", "a@123.456", new string('a', 65) + "@example.com" })
            Reject(() => ContactDetailsValidation.Email(strInvalid), "Malformed email is rejected");
        foreach (var (strInput, strExpected) in new[]
        {
            ("2025550100", "+1 (202) 555-0100"), ("1-202-555-0100", "+1 (202) 555-0100"),
            ("+1 (416) 555-0100 x42", "+1 (416) 555-0100 ext. 42"),
            ("+44 20 7946 0958", "+442079460958"), ("+33 1 42 68 53 00", "+33142685300"),
            ("+49 30 123456 ext. 123", "+4930123456 ext. 123"), ("+81 3 1234 5678", "+81312345678"),
            ("+91 98765 43210", "+919876543210"), ("+971 4 123 4567", "+97141234567"),
            ("+٩٧١ ٤ ١٢٣ ٤٥٦٧", "+97141234567"),
            ("+55 11 91234 5678", "+5511912345678"), ("+61 2 9374 4000", "+61293744000"),
            ("+64 9 123 4567", "+6491234567"), ("+358 9 1234567", "+35891234567"),
            ("+7 495 123 4567", "+74951234567"), ("+44 20 7946 0958#004", "+442079460958 ext. 004")
        }) Check(ContactDetailsValidation.Phone(strInput) == strExpected, "Accepted international/US phone format");
        foreach (var strInvalid in new[] { "", "555-0100", "nonsense", "0000000000", "1111111111", "+0 123456789",
            "+44 123", "+4412345678901234", "++442079460958", "44+2079460958", "+44 (20 79460958",
            "202-ABC-0100", "2025550100 ext. abc", "2025550100 ext. 1234567", "2025550100\n2025550101" })
            Reject(() => ContactDetailsValidation.Phone(strInvalid), "Malformed phone is rejected");
        Check(ContactDetailsValidation.Emails(" ") == "" && ContactDetailsValidation.Phones(null) == "", "Optional blank details remain supported");
        Check(ContactDetailsValidation.Phones("2025550100\n+1 (202) 555-0100") == "+1 (202) 555-0100", "Equivalent phone formats deduplicate after normalization");
        Check(ContactDetailsValidation.Emails("a@EXAMPLE.COM\r\na@example.com") == "a@example.com", "Email domains normalize before deduplication");
        Reject(() => ContactDetailsValidation.Emails("ok@example.com\nbad-email"), "A bad entry rejects the entire email list");
        Reject(() => ContactDetailsValidation.Phones("+442079460958\ninvalid"), "A bad entry rejects the entire phone list");

        var strDirectory = Path.Combine(Path.GetTempPath(), "HippoCrmContactValidation", Guid.NewGuid().ToString("N"));
        using var objKeys = new EphemeralKeyStore();
        var objDatabase = new Database(Path.Combine(strDirectory, "validation.db"), objKeys);
        var objAuth = new AuthService(objDatabase);
        using var objSession = objAuth.CreateAdministratorForTest("fixture", "Fictional validation password!");
        var objCrm = new CrmService(objDatabase, objSession);
        try
        {
            Reject(() => objCrm.Save(new Contact { FirstName = "Invalid fixture", Email = "nonsense" }), "Direct contact saves cannot bypass email validation");
            Reject(() => objCrm.Save(new Organization { Name = "Invalid fixture", Phone = "nonsense" }), "Direct organization saves cannot bypass phone validation");
            Reject(() => objCrm.Save(new Contact { FirstName = "Invalid fixture", Phone = "+0123456789" }), "Direct contact saves reject malformed international numbers");
            Reject(() => objCrm.Save(new Organization { Name = "Invalid fixture", Email = "a@localhost" }), "Direct organization saves reject incomplete email domains");
            Check(objCrm.Contacts().Count == 0 && objCrm.Organizations().Count == 0, "Rejected creates leave no records behind");
            var objContact = new Contact { FirstName = "Global fixture", Email = "ana@BÜCHER.DE", Phone = "+44 20 7946 0958 ext. 12" };
            objCrm.Save(objContact);
            var objOrganization = new Organization { Name = "Global fixture", Email = "office@example.jp", Phone = "+81 3 1234 5678" };
            objCrm.Save(objOrganization);
            var objReopened = new CrmService(new Database(objDatabase.Path, objKeys), objSession);
            Check(objReopened.Contacts().Single().Email == "ana@xn--bcher-kva.de"
                && objReopened.Contacts().Single().Phone == "+442079460958 ext. 12", "Normalized international contact details survive encrypted reopening");
            Check(objReopened.Organizations().Single().Phone == "+81312345678", "International organization details survive encrypted reopening");
            objContact.Email = "bad update";
            Reject(() => objCrm.Save(objContact), "Malformed contact update is rejected");
            Check(objCrm.Contacts().Single().Email == "ana@xn--bcher-kva.de", "Rejected contact update preserves the saved email");
            objOrganization.Phone = "bad update";
            Reject(() => objCrm.Save(objOrganization), "Malformed organization update is rejected");
            Check(objCrm.Organizations().Single().Phone == "+81312345678", "Rejected organization update preserves the saved phone");
        }
        finally
        {
            objAuth.SignOut(objSession);
            if (Directory.Exists(strDirectory)) Directory.Delete(strDirectory, true);
        }
        Console.WriteLine($"{iChecks} contact-format and persistence checks passed.");
        void Check(bool bCondition, string strDescription)
        {
            if (!bCondition) throw new InvalidOperationException("FAIL " + strDescription);
            iChecks++; Console.WriteLine("PASS " + strDescription);
        }
        void Reject(Action actOperation, string strDescription)
        {
            try { actOperation(); } catch (ArgumentException) { Check(true, strDescription); return; }
            throw new InvalidOperationException("FAIL " + strDescription);
        }
    }
}
