# JASS DBF Explorer v1.1

A C#/.NET WinForms workspace for investigating legacy DBF files.

## v1.1 — DBF Investigation Release

### Records
- Browse DBF records
- Click column headers to sort ascending/descending
- Search across all columns
- Column-specific filtering
- Contains / Starts with / Equals / Not equals
- Numeric comparisons
- Filtered record counts

### Field Structure
- Field name
- DBF type
- Field length
- Decimal count
- CLR type
- Ordinal

### Statistics
Per-field:
- Row count
- Empty/null count
- Non-empty count
- Unique value count
- Example value

### DBF Header
Reads the DBF header directly and displays:
- DBF version byte
- Version description
- Last update date
- Header length
- Record length
- Header record count
- File size
- Field count
- Code-page byte
- Code-page description
- Memo flag

### Export
- Export all currently visible/filtered records to CSV
- Export all currently visible/filtered records to JSON
- UTF-8 output

## Technology

- C#
- .NET 8
- Windows Forms
- DbfDataReader 1.1.0
- DataTable / DataView
- System.Text.Json

## Build

Use a clean folder:

```powershell
cd C:\Users\singh\JASS_DBF_Explorer_v1_1
dotnet restore
dotnet build
dotnet run
```

## Important

Do not overlay this release on v1.0.x.

## Roadmap

### v1.2 — DBF Intelligence
- More complete code-page handling
- Deleted-record analysis
- Memo-file detection
- Numeric/date statistics
- Data-quality warnings
- Column profiling

### v1.3 — Migration
- DBF → SQLite
- Schema generation
- Preview before migration
- Validation report
- Migration log

### v2.0 — JASS Legacy Modernization Studio

DBF Explorer
→ FoxPro Project Analyzer
→ Schema Mapper
→ Migration Engine
→ Validation
→ Modern Python / C# / SQLite applications

## Status

**JASS DBF Explorer v1.1 — DBF Investigation Release**
