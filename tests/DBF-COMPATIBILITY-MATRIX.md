# DBF Compatibility Matrix — v1.4.2

| Version byte | Dialect | Memo companion | v1.4.2 behavior |
|---|---|---|---|
| `0x02` | FoxBASE / dBase II | None | Header + fixed-width inspection |
| `0x03` | FoxBASE+ / dBase III+ | None | Header + fixed-width inspection |
| `0x30` | Visual FoxPro | `.FPT` | Detects dialect and memo dependency |
| `0x31` | Visual FoxPro + autoincrement | `.FPT` | Detects dialect and memo dependency |
| `0x32` | Visual FoxPro + varchar/varbinary | `.FPT` | Detects dialect and memo dependency |
| `0x83` | FoxBASE+ / dBase III+ with memo | `.DBT` | Detects missing/present memo companion |
| `0x8B` | dBASE IV with memo | `.DBT` | Detects missing/present memo companion |
| `0xCB` | dBASE IV SQL with memo | `.DBT` | Detects missing/present memo companion |
| `0xE5` | HiPer-Six / Clipper SIX with memo | `.SMT` | Detects memo dependency |
| `0xF5` | FoxPro 2.x with memo | `.FPT` | **Regression-tested against real sample.dbf** |
| `0xFB` | FoxBASE | None | Header + fixed-width inspection |

## Real regression sample

`sample.dbf` supplied for testing is a FoxPro 2.x memo DBF (`0xF5`) with:

- 50 declared records
- 10 fields
- 353-byte header
- 179-byte records
- Memo field: `ASD`
- Missing companion `.FPT` in the supplied test input
- 48 active records displayed; 2 physical records are marked deleted

The application does **not** modify the source DBF.
