# JASS DBF Explorer v1.6.0 — Migration Studio

**Legacy DBF data. Finally visible — and safely migratable.**

v1.6.0 extends the v1.5.1 Migration Workbench into a validation-focused Migration Studio.

## Features

- Read-only DBF / FoxPro inspection
- FoxPro `0xF5` compatibility and FPT Memo support
- Field mapping and target field renaming
- Text transformations
- Data quality statistics
- Duplicate-value analysis
- Primary-key candidate detection
- User-selectable primary key
- Migration validation before export
- Blocks SQL/SQLite export when validation fails
- SQLite schema preview with optional primary key
- Validated SQL export
- Validated SQLite database creation
- Original DBF is never modified

## Migration safety

The Migration Studio validates:

- duplicate target field names
- empty migration field selection
- invalid target table names
- primary-key inclusion
- blank primary-key values
- duplicate primary-key values
- zero-row migrations

A validation failure blocks the final migration buttons.

## Tests

```text
14 passed
```

## Run

Windows:

```bat
run_windows.bat
```

Linux:

```bash
./run_linux.sh
```

Install dependencies with `pip install -r requirements.txt`.
