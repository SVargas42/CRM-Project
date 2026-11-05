# D06 CTI/category policy remediation — manual review

Implementation and verification record from the manual-review stage. At that stage no Git commands, commit messages, commits, pushes, dependency updates or OS provisioning changes were performed. The user subsequently authorized a local-only commit on `test-main`. D08 audit scaling and other audit findings remain outside this change.

## Result

Basic CUI and `CUI//SP-CTI` now use explicit persisted category/distribution approvals. CTI accepts distribution statements B, C, D, E and F as independent policies. No letter implies another permission. Unsupported category strings and invalid category/distribution combinations are rejected.

`users.cui_authorized` remains a compatibility/whole-account eligibility gate. It is no longer sufficient authority to read CTI. Basic CUI also has an explicit approval row. Turning global CUI eligibility off retires **all** category approvals; turning it back on restores Basic CUI only.

CTI access requires all of:

1. A valid live issued session, enabled account, and global CUI eligibility.
2. An active exact `(category, distribution statement)` approval, with a local authority reference.
3. A verified recipient type eligible for that statement.
4. An active record grant; CTI administrators also need a record grant.
5. For writes, an appropriate role and writable record grant.

The authorized creator/designator receives a CTI record grant in the same audited transaction. Changing a designation requires administrator authority, current writable record access, target-policy approval and the current record revision. Normal service saves and disconnected EF updates cannot relabel or downgrade a stored record.

## Recipient rules

Recipient affiliation is an administrator-recorded, authority-supported assertion, with an evidence reference. It is not derived from application role or guessed from employer/name. Existing accounts migrate to `Unverified`, preserving Basic CUI access without acquiring CTI.

| Verified recipient type | Eligible statements, subject to separate explicit approval and record grant |
| --- | --- |
| Unverified | None |
| U.S. Government employee | B, C, F |
| U.S. Government contractor | C, F |
| DoD military/civilian employee | B, C, D, E, F |
| U.S. DoD contractor | C, D, F |

F is not general release permission. Its approval reference must document controlling-office/higher-authority direction, and the record grant must identify the particular authorized material. The data owner must confirm that F is permitted for the applicable program. Affiliation changes retire all CTI approvals and advance authorization freshness. Each SQL/service/EF read rechecks eligibility, including cases where an inconsistent approval row is already stored.

Authority references: [NARA CTI registry](https://www.archives.gov/cui/registry/category-detail/controlled-technical-info.html), [DoD CTI guidance](https://www.dodcui.mil/Defense/Controlled-Technical-Information/), and [DoDI 5230.24](https://www.esd.whs.mil/Portals/54/Documents/DD/issuances/dodi/523024p.pdf). Application authorization enforces recorded approvals; it cannot authenticate an operator's assertion of legal authority. Full designation and release approval remain the controlling authority's responsibility.

## Stored models and inheritance

Contact, Organization and ProfileImage share `CuiProtectedRecord` fields:

- `strCuiCategory` → `cui_category`.
- `strDistributionStatement` → `distribution_statement`.
- `strControllingOffice` → `controlling_office`.
- `strDeterminationDate` → `determination_date`, ISO `yyyy-MM-dd`.
- `strDistributionNotice` → `distribution_notice`, the complete authority-supplied statement.

The legacy `classification='CUI'` column remains for database compatibility. `strCuiBanner` is the display designation; contact/organization profile headers now display it and the retained distribution notice. The application does not invent a legal statement from its letter. CTI requires nonempty controlling office, a real date, and a bounded notice beginning with its matching statement identifier. These are consistency checks, not semantic validation of the authority's entire notice.

Contact and Organization policies are independent: a relationship does not confer access to the other record. Existing organization masking and related-history grant checks remain. Contact history inherits its contact's required access. Browse/detail/name-only projections preserve their labels; all search, token, dashboard, relationship and history paths apply the category scope.

Images inherit the complete designation of their Contact or Organization. Upload assigns those fields inside the audited transaction. SQLite rejects independent image downgrades or mismatches, including orphan parent references. Authorized parent redesignation propagates to **every retained image version**, including inactive images, in the same transaction. Image reads check parent and image policy before allocating the caller-owned decrypted BLOB; failed returns wipe it. `ProfileImage.Dispose()` wipes successful caller-owned buffers. EF maps image labels, while the existing guarded context prohibits unscoped queries or generic image writes; sanitized `ProfileImageService` remains the upload path.

## Schema version 9

`SchemaManager` adds policy columns to retained tables and recipient fields to users without rebuilding or removing records. `Migration009.sql` creates the exact-policy approval table, classification consistency triggers, no-delete guard and image inheritance/propagation triggers. Fresh schema definitions contain the new fields. Migration and its signed event commit atomically.

Existing approved users receive Basic CUI approval only. No existing CRM record is guessed to be CTI; data owners must explicitly designate actual CTI. Existing images retain their BLOB/status and inherit their actual parent's designation. Invalid/orphan legacy image relationships fail migration rather than silently dropping data. Reopening version 9 does not rerun approval seeding or revive revoked permissions. An older executable does not support version 9; review the deployment/backup procedure before applying a production upgrade.

New audit actions are `CuiPolicyGranted`, `CuiPolicyRevoked`, `CuiDesignationChanged` and `CuiRecipientChanged`. The authenticated database epoch includes all four, and local invalidation occurs only after an audited commit. Policy revocation also revokes the user's existing database sessions. This uses the current freshness/idle behavior; it does not change the deferred stale-working-copy finding or audit scaling.

## Administrative APIs for review

Existing `SetCuiAuthorization` controls global eligibility/Basic approval. New `UserAdministrationService` APIs are:

- `SetCuiRecipientKind(userId, recipientKind, evidenceReference)`.
- `SetCuiPolicyAccess(userId, category, distribution, authorized, approvalReference)`.
- `SetRecordCuiDesignation(recordKind, recordId, expectedRevision, designation)`.

For a new CTI record, initialize its inherited policy fields before calling `CrmService.Save` or the authorized EF gateway; the caller must already hold the target category approval and satisfy the recipient rule. For an existing record, use the explicit designation command and reload the new revision. Administrative approval remains a service API, consistent with the existing account/grant architecture; no new account-administration screen was introduced.

`ProfileImageService` also exposes authorized `ReadContactPicture` and `ReadOrganizationLogo` methods returning disposable BLOB owners. Upload APIs retain their original signatures and inherit the parent policy automatically.

## Changed files

Production policy/data code:

- [CuiPolicy.cs](../src/HippoCrm.Core/CuiPolicy.cs): designation/recipient types, validation, shared policy owner, SQL audience scope, transfer helpers.
- [Models.cs](../src/HippoCrm.Core/Models.cs): inherited Contact/Organization fields and disposable ProfileImage.
- [AccessControl.cs](../src/HippoCrm.Core/AccessControl.cs): live identity audience and per-policy/record checks.
- [CrmService.cs](../src/HippoCrm.Core/CrmService.cs), [CuiLookup.cs](../src/HippoCrm.Core/CuiLookup.cs): scoped service reads/writes, preserved projection labels and policy epochs.
- [UserAdministrationService.cs](../src/HippoCrm.Core/UserAdministrationService.cs): audited approval, recipient and redesignation operations.
- [CrmDbContext.cs](../src/HippoCrm.Core/CrmDbContext.cs): mappings, recipient/category filters and protected policy changes. CTI creator grants use EF's generated-key sidecar before commit/AcceptAllChanges, so grants/audits reference the persisted ID.
- [ProfileImageService.cs](../src/HippoCrm.Core/ProfileImageService.cs), [CuiOwnedData.cs](../src/HippoCrm.Core/CuiOwnedData.cs): inherited image policies, authorized read and buffer disposal.
- [Schema.sql](../src/HippoCrm.Core/Schema.sql), [Migration009.sql](../src/HippoCrm.Core/Migration009.sql), [SchemaManager.cs](../src/HippoCrm.Core/SchemaManager.cs), [Core project](../src/HippoCrm.Core/HippoCrm.Core.csproj): schema upgrade, enforcement and embedded resource.
- [SecurityAudit.cs](../src/HippoCrm.Core/SecurityAudit.cs): defined policy events only; chain algorithm unchanged.
- [ProfileHeader.axaml](../src/HippoCrm.Desktop/Views/ProfileHeader.axaml): actual category/distribution marking.

Tests:

- [CuiCategoryChecks.cs](../tests/HippoCrm.SecurityChecks/CuiCategoryChecks.cs): independent B–F access, recipient exclusions, invalid stored approval, projections, ordinary-save downgrade denial, revocation/audit rollback, migration and authority restrictions.
- [CuiCategoryEfChecks.cs](../tests/HippoCrm.EfPolicyChecks/CuiCategoryEfChecks.cs): actual EF and sanitized image round trips, parent/image policy propagation, denied access, buffer wiping and audit rollback.
- SecurityChecks/EF project files and entry points link/run the new cases. Existing schema-version assertions now expect 9; the name-migration fixture uses captured legacy schema rather than downgrading a current workspace containing newer policy tables.

## Verification

Builds use existing locally restored packages with `--no-restore`, with SDK telemetry/workload update notifications disabled. No feed/dependency changes were made. Builds retain the pre-existing ImageSharp licensing warnings; these are not suppressed by this change.

Observed Release checks: 256 service/security adapter checks, 230 actual EF/in-memory checks, 140 foundation source/convention checks plus their password/session/lease/checkpoint sub-suites, 19 CUI presentation checks, 19 routed input/session checks, 55 startup security-gate checks, and 8 Release development-isolation checks passed. Counts overlap; they are not certified controls. Test evidence is in ignored `artifacts/d06-*` logs.

Final Debug and Release solution builds passed with zero errors (two and four existing ImageSharp warning groups respectively). The Debug CUI presentation suite also passed all 26 checks, including active editing and confirmed revocation. The final Release EF/image rerun passed all 230 checks after the image attribution fields were verified. Production FIPS/hardware-checkpoint acceptance, native macOS execution and disabled-network-adapter tests remain outside this local fixture verification. No claim of C3PAO certification or whole-process CUI memory erasure is made.

The user has approved staging and committing this milestone locally on `test-main`. Pushing to a remote repository remains explicitly prohibited.
