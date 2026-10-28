using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HippoCrm.Core;
using System.Globalization;

namespace HippoCrm.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}

internal static class Ui
{
    public static TextBlock Text(string value, string? style = null)
    {
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        if (style != null) text.Classes.Add(style);
        return text;
    }
    public static StackPanel Stack(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Row(params Control[] children)
    {
        var panel = Stack(children); panel.Orientation = Orientation.Horizontal; return panel;
    }
    public static Button Button(string label, Action action, bool primary = false)
    {
        var button = new Button { Content = label };
        if (primary) button.Classes.Add("primary");
        button.Click += (_,_) => action(); return button;
    }
    public static TextBox Input(string text = "", bool multiline = false) => new()
    {
        Text = text, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multiline ? 85 : 38
    };
    public static Control Field(string label, Control input)
    {
        input.Tag = label;
        return Stack(Text(label,"muted"),input);
    }
    public static ScrollViewer Scroll(Control content) => new() { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    public static Border Card(Control content) => new()
    {
        Child = content, Padding = new Thickness(20), CornerRadius = new CornerRadius(10),
        BorderBrush = new SolidColorBrush(Color.Parse("#718780"),0.25), BorderThickness = new Thickness(1)
    };
    public static ComboBox Choice<T>(T selected) where T : struct, Enum => new() { ItemsSource = Enum.GetValues<T>(), SelectedItem = selected };
    public static Grid Columns(string definitions, params Control[] children)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(definitions), ColumnSpacing = 20 };
        for (var i = 0; i < children.Length; i++) { Grid.SetColumn(children[i],i); grid.Children.Add(children[i]); }
        return grid;
    }
}

public sealed partial class MainWindow : Window
{
    private readonly Database? _database;
    private readonly AuthService? _auth;
    private CrmService? _crm;
    private Session? _session;
    private readonly ContentControl _page = new();
    private readonly TextBlock _message = Ui.Text("");
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(30) };
    public MainWindow(Database? database = null)
    {
        Title = "Swollen Hippo • CRM"; Width = 1380; Height = 900; MinWidth = 1080; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BigEagleGroup","HippoCrm");
            _database = database ?? new Database(Path.Combine(directory,"crm.db")); _auth = new AuthService(_database);
            ShowLogin();
        }
        catch (Exception)
        {
            Content = Ui.Card(Ui.Stack(Ui.Text("Unable to open the CRM","title"),Ui.Text("The local data directory could not be opened. Check folder permissions and available disk space, then restart the application.")));
        }
        AddHandler(PointerPressedEvent,(_,_) => _lastActivity = DateTimeOffset.UtcNow,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent,(_,_) => _lastActivity = DateTimeOffset.UtcNow,Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _idle.Tick += (_,_) => { if (_session != null && DateTimeOffset.UtcNow - _lastActivity > TimeSpan.FromMinutes(15)) { ShowLogin(); _message.Text = "Session locked after 15 minutes of inactivity."; } };
        _idle.Start(); Closed += (_,_) => _idle.Stop();
    }
    private void Run(Action action)
    {
        _message.Text = "";
        try { action(); }
        catch (ArgumentException ex) { _message.Text = ex.Message; }
        catch (InvalidOperationException ex) { _message.Text = ex.Message; }
        catch (Microsoft.Data.Sqlite.SqliteException) { _message.Text = "Unable to save this change. Check the record and ensure the data folder is writable."; }
    }
    private void ShowLogin()
    {
        if (_message.Parent is Panel messageParent) messageParent.Children.Remove(_message);
        _session = null; _crm = null; _page.Content = null; _message.Text = "";
        var setup = _auth!.NeedsSetup;
        var username = Ui.Input(); username.Watermark = "Username";
        var password = Ui.Input(); password.PasswordChar = '●'; password.Watermark = "Password";
        var confirm = Ui.Input(); confirm.PasswordChar = '●';
        void Submit() => Run(() =>
        {
            if (setup && password.Text != confirm.Text) throw new ArgumentException("Passwords do not match.");
            _session = setup ? _auth.CreateAdministrator(username.Text ?? "",password.Text ?? "") : _auth.SignIn(username.Text ?? "",password.Text ?? "");
            password.Text = ""; confirm.Text = "";
            _crm = new CrmService(_database!,_session); _lastActivity = DateTimeOffset.UtcNow; ShowShell();
        });
        var form = Ui.Stack(Ui.Text("SWOLLEN HIPPO","muted"),Ui.Text(setup ? "Set up your workspace" : "Welcome back","title"),
            Ui.Text(setup ? "Create the administrator account for this computer. Use at least 12 characters for your password." : "Sign in to your local CRM workspace.","muted"),
            Ui.Field("Username",username),Ui.Field("Password",password));
        if (setup) form.Children.Add(Ui.Field("Confirm password",confirm));
        form.Children.Add(Ui.Button(setup ? "Create workspace" : "Sign in",Submit,true));
        form.Children.Add(_message); form.Children.Add(Ui.Text("LOCAL WORKSPACE  •  OFFLINE","muted"));
        password.KeyDown += (_,e) => { if (!setup && e.Key == Avalonia.Input.Key.Enter) Submit(); };
        confirm.KeyDown += (_,e) => { if (e.Key == Avalonia.Input.Key.Enter) Submit(); };
        var card = Ui.Card(form); card.Width = 490; card.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center; card.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Content = card;
    }
    private void ShowShell()
    {
        if (_message.Parent is Panel messageParent) messageParent.Children.Remove(_message);
        if (_page.Parent is Panel pageParent) pageParent.Children.Remove(_page);
        var nav = Ui.Stack(Ui.Text("SWOLLEN\nHIPPO","section"),Ui.Text("CONTRACTOR RELATIONSHIPS","muted"));
        void Navigation(string name, Action action) { var b = Ui.Button(name,() => Run(action)); b.Classes.Add("nav"); nav.Children.Add(b); }
        Navigation("Overview",ShowDashboard); Navigation("Contacts",ShowContacts); Navigation("Organizations",ShowOrganizations);
        nav.Children.Add(new Separator { Margin = new Thickness(0,24,0,12) });
        nav.Children.Add(Ui.Text("WORKSPACE","muted")); nav.Children.Add(Ui.Text(_session!.Username));
        var theme = new ToggleSwitch { Content = "Dark mode", IsChecked = Application.Current!.RequestedThemeVariant == ThemeVariant.Dark };
        theme.IsCheckedChanged += (_,_) => Application.Current!.RequestedThemeVariant = theme.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light;
        nav.Children.Add(theme); Navigation("Lock workspace",ShowLogin);
        nav.Children.Add(Ui.Text("Big Eagle Group\nLocal data · No cloud sync","muted"));
        foreach (var text in nav.Children.OfType<TextBlock>()) text.Foreground = Brushes.White;
        theme.Foreground = Brushes.White;
        var sidebar = new Border { Background = Brush.Parse("#173E38"), Padding = new Thickness(24,36), Child = nav };
        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(30,24) };
        _message.Margin = new Thickness(0,0,0,12); _message.Foreground = Brush.Parse("#C86A26");
        main.Children.Add(_message); Grid.SetRow(_page,1); main.Children.Add(_page);
        var root = Ui.Columns("235,*",sidebar,main); root.ColumnSpacing = 0; Content = root; ShowDashboard();
    }
    private void ShowDashboard()
    {
        var d = _crm!.Dashboard();
        Control Metric(string label,int value) => Ui.Card(Ui.Stack(Ui.Text(value.ToString("N0"),"title"),Ui.Text(label,"muted")));
        var activity = Ui.Stack(Ui.Text("Interaction activity","section"),Ui.Text("Notes recorded over the last four weeks · UTC","muted"));
        foreach (var week in d.Activity)
        {
            activity.Children.Add(Ui.Columns("90,*,50",Ui.Text(week.Label),new ProgressBar { Minimum = 0, Maximum = Math.Max(1,d.Activity.Max(w => w.Count)),Value = week.Count,Height = 10,VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },Ui.Text(week.Count.ToString())));
        }
        _page.Content = Ui.Scroll(Ui.Stack(Ui.Text("Workspace overview","title"),Ui.Text(DateTime.Now.ToString("dddd, MMMM d, yyyy"),"muted"),
            Ui.Columns("*,*,*",Metric("Contacts",d.Contacts),Metric("Organizations",d.Organizations),Metric("Active & available",d.Available)),
            Ui.Columns("*,*,*",Metric("Interactions · 30 days",d.Interactions30Days),Metric("Need follow-up · 30+ days",d.Overdue),Metric("Archived contacts",d.Archived)),
            Ui.Card(activity),Ui.Card(Ui.Stack(Ui.Text("Keep relationships moving","section"),Ui.Text("Record an interaction after each conversation. Follow-up counts include active contacts with no note in the past 30 days, including contacts with no notes yet.","muted"),Ui.Row(Ui.Button("Open contacts",ShowContacts,true),Ui.Button("Open organizations",ShowOrganizations))))));
    }
    private void ShowOrganizations()
    {
        var search = Ui.Input(); search.Watermark = "Name, service, or tag";
        var archived = new CheckBox { Content = "Archived records" };
        var list = new ListBox { MinHeight = 200 };
        var editor = new ContentControl();
        void Refresh(long? selected = null)
        {
            var items = _crm!.Organizations(search.Text ?? "",archived.IsChecked == true); list.ItemsSource = items;
            if (selected.HasValue) list.SelectedItem = items.FirstOrDefault(x => x.Id == selected);
        }
        void Edit(Organization org)
        {
            var name = Ui.Input(org.Name); var email = Ui.Input(org.Email); var phone = Ui.Input(org.Phone); var address = Ui.Input(org.Address,true);
            var services = Ui.Input(org.Services,true); var tags = Ui.Input(org.Tags); var status = Ui.Choice(org.Status);
            void Save(bool toggleArchive = false) => Run(() =>
            {
                org.Name = name.Text ?? ""; org.Email = email.Text ?? ""; org.Phone = phone.Text ?? ""; org.Address = address.Text ?? "";
                org.Services = services.Text ?? ""; org.Tags = tags.Text ?? ""; org.Status = (RecordStatus)status.SelectedItem!;
                var old = org.Archived;
                if (toggleArchive) org.Archived = !old;
                try { _crm!.Save(org); } catch { org.Archived = old; throw; }
                Refresh(org.Id); _message.Text = "Organization saved.";
                if (list.SelectedItem == null) editor.Content = Ui.Text("Organization saved. Switch the archive filter to view it.","muted");
            });
            var form = Ui.Stack(Ui.Text(org.Id == 0 ? "New organization" : org.Name,"section"),Ui.Field("Organization name *",name),
                Ui.Columns("*,*",Ui.Field("Email",email),Ui.Field("Phone",phone)),Ui.Field("Address / locations",address),Ui.Field("Services",services),Ui.Field("Tags (comma separated)",tags),Ui.Field("Status",status),
                Ui.Row(Ui.Button("Save organization",() => Save(),true)));
            if (org.Id != 0) form.Children.Add(Ui.Button(org.Archived ? "Restore organization" : "Archive organization",() => Save(true)));
            editor.Content = Ui.Scroll(Ui.Card(form));
        }
        list.SelectionChanged += (_,_) => { if (list.SelectedItem is Organization org) Edit(org); };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 12 };
        left.Children.Add(search); Grid.SetRow(archived,1); left.Children.Add(archived);
        var apply = Ui.Button("Apply filters",() => Run(() => Refresh())); Grid.SetRow(apply,2); left.Children.Add(apply); Grid.SetRow(list,3); left.Children.Add(list);
        var body = Ui.Columns("280,*",left,editor);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 20 };
        page.Children.Add(Ui.Stack(Ui.Text("Organizations","title"),Ui.Row(Ui.Text("The companies behind your network.","muted"),Ui.Button("+ Add organization",() => { list.SelectedItem = null; Edit(new()); },true))));
        Grid.SetRow(body,1); page.Children.Add(body); _page.Content = page;
        Refresh(); editor.Content = Ui.Card(Ui.Text("Select an organization or add one to your network.","muted"));
    }
}

public sealed partial class MainWindow
{
    private void ShowContacts()
    {
        var search = Ui.Input(); search.Watermark = "Name, email, or phone";
        var skill = Ui.Input(); skill.Watermark = "Skill contains…";
        var availability = new ComboBox { ItemsSource = new[] { "Any availability", "Available", "Unavailable", "Unknown" }, SelectedIndex = 0 };
        var archived = new CheckBox { Content = "Archived records" };
        var list = new ListBox(); var editor = new ContentControl();
        void Refresh(long? selected = null)
        {
            var items = _crm!.Contacts(search.Text ?? "",skill.Text ?? "",availability.SelectedIndex == 0 ? null : Enum.Parse<Availability>((string)availability.SelectedItem!),archived.IsChecked == true);
            list.ItemsSource = items;
            if (selected.HasValue) list.SelectedItem = items.FirstOrDefault(x => x.Id == selected);
        }
        void Edit(Contact contact)
        {
            var inputs = new Dictionary<string,TextBox>();
            Control Input(string label,string property,bool multiline = false)
            {
                var input = Ui.Input((string)typeof(Contact).GetProperty(property)!.GetValue(contact)!,multiline); inputs.Add(property,input); return Ui.Field(label,input);
            }
            var status = Ui.Choice(contact.Status); var available = Ui.Choice(contact.Availability);
            var profile = Ui.Stack(
                Ui.Columns("*,*",Input("First name *",nameof(Contact.FirstName)),Input("Last name",nameof(Contact.LastName))),
                Input("Emails (one per line)",nameof(Contact.Email),true),Input("Phone numbers (one per line)",nameof(Contact.Phone),true),
                Input("Addresses (one per line)",nameof(Contact.Address),true),Input("Skills (comma separated)",nameof(Contact.Skills),true),
                Ui.Columns("*,*",Ui.Field("Status",status),Ui.Field("Contract availability",available)));
            var personal = Ui.Stack(Input("Spouse / partner",nameof(Contact.Spouse)),Input("Children",nameof(Contact.Children),true),Input("Extended family",nameof(Contact.Family),true),Input("Friends",nameof(Contact.Friends),true));
            var social = Ui.Stack(Input("Twitter / X",nameof(Contact.Twitter)),Input("Instagram",nameof(Contact.Instagram)),Input("Snapchat",nameof(Contact.Snapchat)),Input("Facebook",nameof(Contact.Facebook)));
            var tabs = new TabControl();
            var tabItems = new List<TabItem>
            {
                new() { Header = "Profile", Content = Ui.Scroll(profile) },
                new() { Header = "Personal", Content = Ui.Scroll(personal) },
                new() { Header = "Social", Content = Ui.Scroll(social) }
            };
            if (contact.Id != 0)
            {
                tabItems.Add(new() { Header = "Interactions", Content = InteractionPanel(contact) });
                tabItems.Add(new() { Header = "Employment", Content = EmploymentPanel(contact) });
                tabItems.Add(new() { Header = "Contracts", Content = ContractPanel(contact) });
            }
            tabs.ItemsSource = tabItems;
            void Save(bool toggleArchive = false) => Run(() =>
            {
                foreach (var (property,input) in inputs) typeof(Contact).GetProperty(property)!.SetValue(contact,input.Text ?? "");
                contact.Status = (RecordStatus)status.SelectedItem!; contact.Availability = (Availability)available.SelectedItem!;
                var old = contact.Archived;
                if (toggleArchive) contact.Archived = !old;
                try { _crm!.Save(contact); } catch { contact.Archived = old; throw; }
                Refresh(contact.Id); _message.Text = "Contact saved.";
                if (list.SelectedItem == null) editor.Content = Ui.Card(Ui.Text("Contact saved. Adjust the filters to view it.","muted"));
            });
            var actions = Ui.Row(Ui.Button("Save contact",() => Save(),true));
            if (contact.Id != 0) actions.Children.Add(Ui.Button(contact.Archived ? "Restore contact" : "Archive contact",() => Save(true)));
            var form = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 14 };
            form.Children.Add(Ui.Text(contact.Id == 0 ? "New contact" : contact.DisplayName,"section"));
            Grid.SetRow(actions,1); form.Children.Add(actions); Grid.SetRow(tabs,2); form.Children.Add(tabs);
            editor.Content = Ui.Card(form);
        }
        list.SelectionChanged += (_,_) => { if (list.SelectedItem is Contact contact) Run(() => Edit(contact)); };
        var filters = Ui.Stack(search,skill,availability,archived,Ui.Button("Apply filters",() => Run(() => Refresh())));
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 15 };
        left.Children.Add(filters); Grid.SetRow(list,1); left.Children.Add(list);
        var body = Ui.Columns("280,*",left,editor);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 20 };
        page.Children.Add(Ui.Stack(Ui.Text("Contacts","title"),Ui.Row(Ui.Text("Your network, connected.","muted"),Ui.Button("+ Add contact",() => { list.SelectedItem = null; Edit(new()); },true))));
        Grid.SetRow(body,1); page.Children.Add(body); _page.Content = page;
        Refresh(); editor.Content = Ui.Card(Ui.Text("Select a contact or add someone to your network.","muted"));
    }
    private Control InteractionPanel(Contact contact)
    {
        var notes = Ui.Stack(); var input = Ui.Input("",true); input.Watermark = "Record a conversation, meeting, or follow-up…";
        void Refresh()
        {
            notes.Children.Clear();
            foreach (var note in _crm!.Interactions(contact.Id))
                notes.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"{note.CreatedAt.ToLocalTime():MMM d, yyyy · h:mm tt}  /  {note.Author}","muted"),Ui.Text(note.Body))));
            if (notes.Children.Count == 0) notes.Children.Add(Ui.Text("No interactions recorded.","muted"));
        }
        var form = Ui.Stack(Ui.Text("Interaction timeline","section"),Ui.Text("Saved notes are timestamped and retained. Add a new note to record a correction.","muted"));
        if (!contact.Archived)
        {
            form.Children.Add(input);
            form.Children.Add(Ui.Button("Record interaction",() => Run(() => { _crm!.AddInteraction(contact.Id,input.Text ?? ""); input.Text = ""; Refresh(); _message.Text = "Interaction recorded."; }),true));
        }
        form.Children.Add(notes); Refresh(); return Ui.Scroll(form);
    }
    private Control EmploymentPanel(Contact contact)
    {
        var history = Ui.Stack();
        var organizations = new ComboBox { ItemsSource = _crm!.Organizations() };
        var role = Ui.Input(); var start = Ui.Input(DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd")); var end = Ui.Input();
        void Refresh()
        {
            history.Children.Clear();
            foreach (var placement in _crm.EmploymentHistory(contact.Id))
            {
                var card = Ui.Stack(Ui.Text(placement.OrganizationName,"section"),Ui.Text(placement.Role),Ui.Text($"{placement.Start:MMM d, yyyy} → {(placement.End.HasValue ? placement.End.Value.ToString("MMM d, yyyy") : "Current")}","muted"));
                if (!placement.End.HasValue && !contact.Archived)
                {
                    var ended = Ui.Input(DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"));
                    card.Children.Add(Ui.Field("End date (YYYY-MM-DD)",ended));
                    card.Children.Add(Ui.Button("End placement",() => Run(() => { _crm.EndEmployment(placement.Id,ParseDate(ended.Text)); Refresh(); _message.Text = "Placement ended."; })));
                }
                history.Children.Add(Ui.Card(card));
            }
            if (history.Children.Count == 0) history.Children.Add(Ui.Text("No employment history recorded.","muted"));
        }
        var form = Ui.Stack(Ui.Text("Employment history","section"),Ui.Text("Track movement between organizations. End the current placement before recording the next; dates must not overlap.","muted"));
        if (!contact.Archived)
        {
            form.Children.Add(Ui.Field("Organization",organizations)); form.Children.Add(Ui.Field("Role",role));
            form.Children.Add(Ui.Columns("*,*",Ui.Field("Start (YYYY-MM-DD)",start),Ui.Field("End (optional)",end)));
            form.Children.Add(Ui.Button("Add placement",() => Run(() =>
            {
                if (organizations.SelectedItem is not Organization org) throw new ArgumentException("Select an organization. Create it in Organizations if needed.");
                _crm.AddEmployment(contact.Id,org.Id,role.Text ?? "",ParseDate(start.Text),string.IsNullOrWhiteSpace(end.Text) ? null : ParseDate(end.Text));
                role.Text = ""; end.Text = ""; Refresh(); _message.Text = "Placement added.";
            }),true));
        }
        form.Children.Add(history); Refresh(); return Ui.Scroll(form);
    }
    private Control ContractPanel(Contact contact)
    {
        var history = Ui.Stack(); var date = Ui.Input(DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"));
        var value = Ui.Input(); var location = Ui.Input(); var description = Ui.Input("",true);
        void Refresh()
        {
            history.Children.Clear(); var contracts = _crm!.Contracts(contact.Id);
            foreach (var contract in contracts)
                history.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"{contract.Date:MMM d, yyyy}  ·  {contract.Value:N2}","section"),Ui.Text(contract.Location),Ui.Text(contract.Description))));
            if (history.Children.Count == 0) history.Children.Add(Ui.Text("No contracts recorded.","muted"));
        }
        var form = Ui.Stack(Ui.Text("Contract history","section"),Ui.Text("Most recent contracts appear first. Use one agreed currency for all contract values.","muted"));
        if (!contact.Archived)
        {
            form.Children.Add(Ui.Columns("*,*",Ui.Field("Date (YYYY-MM-DD)",date),Ui.Field("Contract value",value)));
            form.Children.Add(Ui.Field("Location",location)); form.Children.Add(Ui.Field("Description",description));
            form.Children.Add(Ui.Button("Add contract",() => Run(() =>
            {
                if (!decimal.TryParse(value.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out var amount)) throw new ArgumentException("Enter a valid contract value.");
                _crm!.AddContract(contact.Id,ParseDate(date.Text),amount,location.Text ?? "",description.Text ?? "");
                value.Text = ""; location.Text = ""; description.Text = ""; Refresh(); _message.Text = "Contract added.";
            }),true));
        }
        form.Children.Add(history); Refresh(); return Ui.Scroll(form);
    }
    private static DateOnly ParseDate(string? text)
    {
        if (!DateOnly.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)) throw new ArgumentException("Enter dates as YYYY-MM-DD.");
        return date;
    }
}

