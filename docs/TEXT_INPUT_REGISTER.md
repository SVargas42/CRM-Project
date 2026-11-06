# Text input register

This register tracks every editable text field and autocomplete input in the desktop UI. The XAML controls use `CrmTextBox`; secure password inputs inherit the same geometry while retaining their secure character buffer. Bindings and user-facing purpose are preserved through the control replacement. New fields must be added here when introduced.

| Page or flow | Control | Bound value | Purpose |
|---|---|---|---|
| ContactEditor | `txtFirstName` | `objContact.FirstName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: First name * |
| ContactEditor | `txtLastName` | `objContact.LastName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Last name |
| ContactEditor | `acbOrganizationSearch` | `strOrganizationSearch, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | suggestion search: Search available organizations |
| ContactEditor | `txtNewContactEmail` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Emails |
| ContactEditor | `txtNewContactPhone` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Phones |
| ContactEditor | `txtNewContactAddress` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Addresses |
| ContactEditor | `txtNewEmail` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Emails |
| ContactEditor | `txtNewPhone` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Phones |
| ContactEditor | `txtNewAddress` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Addresses |
| ContactEditor | `txtSpouse` | `objContact.Spouse, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Spouse / partner |
| ContactEditor | `txtChildren` | `objContact.Children, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Children |
| ContactEditor | `txtFamily` | `objContact.Family, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Extended family |
| ContactEditor | `txtFriends` | `objContact.Friends, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Friends |
| ContactEditor | `txtTwitter` | `objContact.Twitter, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Twitter / X |
| ContactEditor | `txtInstagram` | `objContact.Instagram, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Instagram |
| ContactEditor | `txtSnapchat` | `objContact.Snapchat, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Snapchat |
| ContactEditor | `txtFacebook` | `objContact.Facebook, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Facebook |
| ContactEditor | `txtInteractionNote` | `strNote, Mode=TwoWay` | text entry: Interaction note |
| ContactEditor | `txtEmploymentRole` | `strRole, Mode=TwoWay` | text entry: Role |
| ContactEditor | `txtEmploymentStart` | `strStartDate, Mode=TwoWay` | text entry: Start (YYYY-MM-DD) |
| ContactEditor | `txtEmploymentEnd` | `strEndDate, Mode=TwoWay` | text entry: End (optional) |
| ContactEditor | `txtPlacementEndDate` | `strEndDate, Mode=TwoWay` | text entry: Placement end date YYYY-MM-DD |
| ContactEditor | `txtContractDate` | `strContractDate, Mode=TwoWay` | text entry: Date (YYYY-MM-DD) |
| ContactEditor | `txtContractValue` | `strContractValue, Mode=TwoWay` | text entry: Contract value |
| ContactEditor | `txtContractLocation` | `strLocation, Mode=TwoWay` | text entry: Location |
| ContactEditor | `txtContractDescription` | `strDescription, Mode=TwoWay` | text entry: Description |
| Contacts | `txtContactSearch` | `strSearchQuery, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Search contacts |
| Contacts | `txtContactSkill` | `vmWorkspace.strSkill, Mode=TwoWay` | text entry: Skill contains |
| Dashboard | `txtMasterSearch` | `strSearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Master search text |
| EditOrganization | `txtEditOrganizationName` | `vmWorkspace.objOrganization.Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Organization name * |
| EditOrganization | `txtNewEmail` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Emails |
| EditOrganization | `txtNewPhone` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Phones |
| EditOrganization | `txtNewAddress` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Addresses |
| OrganizationEditor | `txtOrganizationName` | `objOrganization.Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Organization name * |
| OrganizationEditor | `txtNewOrganizationEmail` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Emails |
| OrganizationEditor | `txtNewOrganizationPhone` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Phones |
| OrganizationEditor | `txtNewOrganizationAddress` | `strNewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: New Addresses |
| OrganizationProfile | `txtContactSearch` | `strContactSearch, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Search this organization's contacts |
| Organizations | `txtOrganizationLookupSearch` | `strSearchQuery, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Search organizations |
| SkillSelectionWindow | `txtSkillSearch` | `strSearch, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Search skills or Search services (same CrmTextBox, selected by picker context) |
| TagSelectionWindow | `txtTagSearch` | `strSearch, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` | text entry: Search tags |
| Login / setup | `txtUsername` | `MainWindowViewModel.strUsername` | Workspace identity; shown during initial setup or when multiple users require disambiguation. |
| Login / setup | `txtPassword` | `MainWindowViewModel.secPassword (SecureString)` | Secret password input; invokes sign-in on Return. |
| Login / setup | `txtConfirmPassword` | `MainWindowViewModel.secConfirmation (SecureString)` | Secret confirmation input; shown only during initial setup. |
| Organization assignment | `acbOrganizationSearch` | `MainWindowViewModel.strOrganizationSearch; fnPopulateOrganizations; objOrganizationToAdd` | Search and stage an organization to assign to the contact. |
