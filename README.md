# JASS DBF Explorer v1.3.2

A C#/.NET WinForms desktop workspace for investigating legacy DBF files and migrating them safely to SQLite.

## v1.3 — DBF → SQLite Migration Engine

### Existing DBF intelligence

- DBF records browser
- Sorting
- Search and filtering
- Field structure
- Data-quality profiling
- DBF header analysis
- Deleted-record detection
- Code-page information
- SQLite schema preview
- CSV/JSON export

### New migration engine

The v1.3 migration workflow is:

DBF
→ Analyze
→ Review SQLite mapping
→ Choose destination
→ Create SQLite database
→ Migrate records
→ Validate record counts

### Migration safety

- Original DBF is read-only.
- Source and destination must be different files.
- Existing destination requires explicit confirmation.
- SQLite creation and inserts run inside a transaction.
- Failed migrations attempt rollback and remove the incomplete destination.
- Deleted DBF records are excluded from the normal migration because the reader skips deleted records.
- Migration progress is displayed.
- Migration log is displayed in the application.

### SQLite mapping

Typical mappings:

| DBF | SQLite |
|---|---|
| Character | TEXT |
| Date | TEXT |
| Logical | INTEGER |
| Numeric | NUMERIC |
| Float | REAL |
| Integer | NUMERIC |
| Memo | TEXT* |

Memo fields are flagged for additional review because legacy memo data may be stored in an associated memo file.

### Validation

After migration the application compares:

- Source visible record count
- SQLite destination record count

A matching count is reported as a validation pass.

This is an initial validation layer, not yet a complete row-by-row checksum comparison.

## Build

Use a clean folder:

```powershell
cd C:\Users\singh\JASS_DBF_Explorer_v1_3
dotnet restore
dotnet build
dotnet run
```

## Recommended first test

Use the real legacy DBF that has already been tested with this project, for example:

```text
MAST0705.DBF
```

Open it, review the **SQLite Preview** tab, then choose a destination such as:

```text
MAST0705.sqlite
```

Click:

**Migrate DBF → SQLite**

Then use:

**Validate Destination**

## Important

Do not overwrite the v1.1 or v1.2 project folders.

The source DBF is never modified by the migration engine.

## Roadmap

### v1.4 — Migration Intelligence
- Row-by-row validation
- Source/destination column counts
- Null-count comparison
- Type conversion report
- Migration warnings
- Duplicate/key candidate analysis
- Memo-file support
- Better code-page handling

### v2.0 — JASS Legacy Modernization Studio

DBF Explorer
→ FoxPro Project Analyzer
→ Schema Mapper
→ Migration Engine
→ Validation
→ Modern Python / C# / SQLite applications

## Status

**JASS DBF Explorer v1.3.1 — DBF → SQLite Migration Engine build fix**


## v1.3.1

Maintenance release correcting the v1.3 build issues:
- Migration controls are initialized through the migration-page builder without readonly-field compiler errors.
- SQLite migration parameters use the supported `AddWithValue` API.
- The migration workflow and v1.3 feature set are otherwise unchanged.


## v1.3.2

Build-fix release:
- Corrected the remaining readonly migration-preview fields.
- Added defensive nullable annotations at known validated data-access points.
- No functional change to the DBF → SQLite migration workflow.
