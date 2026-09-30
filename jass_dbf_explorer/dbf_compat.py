from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from pathlib import Path
from typing import Any
import struct

VERSION_INFO = {
    0x02: ("FoxBASE / dBase II", "None"),
    0x03: ("FoxBASE+ / dBase III+", "None"),
    0x30: ("Visual FoxPro", ".FPT"),
    0x31: ("Visual FoxPro + autoincrement", ".FPT"),
    0x32: ("Visual FoxPro + varchar/varbinary", ".FPT"),
    0x43: ("dBASE IV SQL table", "None"),
    0x63: ("dBASE IV SQL system", "None"),
    0x83: ("FoxBASE+ / dBase III+ with memo", ".DBT"),
    0x8B: ("dBASE IV with memo", ".DBT"),
    0xCB: ("dBASE IV SQL with memo", ".DBT"),
    0xE5: ("HiPer-Six / Clipper SIX with memo", ".SMT"),
    0xF5: ("FoxPro 2.x with memo", ".FPT"),
    0xFB: ("FoxBASE", "None"),
}

@dataclass
class DBFField:
    name: str
    type: str
    length: int
    decimal_count: int

@dataclass
class DBFInspection:
    path: str
    version_byte: int
    dialect: str
    expected_memo_extension: str
    last_update: str
    record_count: int
    header_length: int
    record_length: int
    codepage_mark: int
    table_flags: int
    fields: list[DBFField]
    memo_fields: list[str]
    memo_path: str | None
    warnings: list[str]
    integrity_ok: bool


def _text(raw: bytes) -> str:
    return raw.split(b"\x00", 1)[0].decode("latin-1", errors="replace").strip()


def inspect_dbf(path: str | Path) -> DBFInspection:
    p = Path(path)
    data = p.read_bytes()
    if len(data) < 33:
        raise ValueError("File is too small to be a DBF file.")
    version = data[0]
    dialect, memo_ext = VERSION_INFO.get(version, (f"Unknown / unsupported DBF type 0x{version:02X}", "Unknown"))
    record_count = int.from_bytes(data[4:8], "little")
    header_length = int.from_bytes(data[8:10], "little")
    record_length = int.from_bytes(data[10:12], "little")
    codepage = data[29]
    table_flags = data[28]
    y, m, d = data[1], data[2], data[3]
    last_update = f"20{y:02d}-{m:02d}-{d:02d}" if 1 <= m <= 12 and 1 <= d <= 31 else f"raw {y:02X}-{m:02X}-{d:02X}"

    if header_length < 33 or header_length > len(data):
        raise ValueError(f"Invalid DBF header length: {header_length}")
    if record_length < 1:
        raise ValueError("Invalid DBF record length.")

    fields: list[DBFField] = []
    offset = 32
    while offset + 32 <= len(data) and offset < header_length:
        if data[offset] == 0x0D:
            break
        raw_name = data[offset:offset + 11]
        name = raw_name.split(b"\x00", 1)[0].decode("ascii", errors="replace").strip() or f"FIELD_{len(fields)+1}"
        ftype = chr(data[offset + 11])
        flen = data[offset + 16]
        dec = data[offset + 17]
        fields.append(DBFField(name, ftype, flen, dec))
        offset += 32

    memo_fields = [f.name for f in fields if f.type.upper() == "M"]
    memo_path = None
    warnings: list[str] = []
    if memo_fields:
        if memo_ext in (".DBT", ".FPT", ".SMT"):
            candidate = p.with_suffix(memo_ext.lower())
            if not candidate.exists():
                candidate_upper = p.with_suffix(memo_ext)
                candidate = candidate_upper if candidate_upper.exists() else candidate
            if candidate.exists():
                memo_path = str(candidate)
            else:
                warnings.append(f"Memo field(s) detected ({', '.join(memo_fields)}), but companion {memo_ext} file is missing.")
        else:
            warnings.append(f"Memo field(s) detected ({', '.join(memo_fields)}), but the required memo format is unknown.")

    expected_end = header_length + record_count * record_length
    integrity_ok = expected_end <= len(data) and data[header_length:header_length + 1] != b""
    if expected_end > len(data):
        warnings.append(f"File is shorter than header-declared record payload: expected at least {expected_end:,} bytes, found {len(data):,}.")
        integrity_ok = False
    if len(data) > 0 and data[-1] != 0x1A:
        warnings.append("EOF marker 0x1A is absent; this is permitted by some writers but worth noting.")
    if version not in VERSION_INFO:
        warnings.append(f"Unrecognized DBF version byte 0x{version:02X}; the generic parser will still inspect fixed-width fields.")

    return DBFInspection(
        str(p), version, dialect, memo_ext, last_update, record_count,
        header_length, record_length, codepage, table_flags, fields,
        memo_fields, memo_path, warnings, integrity_ok,
    )


def _read_fpt_header(path: Path) -> int:
    data = path.read_bytes()
    if len(data) < 512:
        raise ValueError("FPT memo file is too small.")
    block_size = int.from_bytes(data[6:8], "big")
    return block_size or 512


def _read_fpt_memo(path: Path, block_number: int) -> str:
    if block_number <= 0:
        return ""
    data = path.read_bytes()
    block_size = _read_fpt_header(path)
    start = block_number * block_size
    if start + 8 > len(data):
        return f"[Memo block {block_number} outside .FPT file]"
    memo_type = int.from_bytes(data[start:start + 4], "big")
    memo_length = int.from_bytes(data[start + 4:start + 8], "big")
    payload = data[start + 8:start + 8 + memo_length]
    if len(payload) < memo_length:
        payload = payload.rstrip(b"\x00")
    return payload.decode("utf-8", errors="replace").rstrip("\x00")


def _memo_value(raw: bytes, memo_path: str | None) -> str:
    if not raw.strip(b"\x00 \t\r\n"):
        return ""
    # Some FoxPro writers store the block number as a null-padded ASCII
    # integer (e.g. b"6\x00\x00..."). Detect that before binary decoding.
    text = raw.rstrip(b"\x00 \t\r\n").decode("ascii", errors="ignore").strip()
    if text.isdigit() and memo_path:
        try:
            return _read_fpt_memo(Path(memo_path), int(text))
        except Exception as exc:
            return f"[Memo read error: {exc}]"
    # Other FoxPro writers use a 4-byte binary block number.
    if len(raw) >= 4:
        block = int.from_bytes(raw[:4], "big")
        if block > 0 and memo_path:
            try:
                return _read_fpt_memo(Path(memo_path), block)
            except Exception as exc:
                return f"[Memo read error: {exc}]"
    return "[Memo: companion file required]"


def _decode_field(raw: bytes, field: DBFField, memo_path: str | None = None) -> Any:
    s = raw.decode("latin-1", errors="replace").strip()
    t = field.type.upper()
    if t == "C":
        return s
    if t in ("N", "F"):
        if not s:
            return None
        try:
            return float(s) if field.decimal_count else int(s)
        except ValueError:
            return s
    if t == "D":
        if len(s) == 8 and s.isdigit():
            try:
                return date(int(s[:4]), int(s[4:6]), int(s[6:]))
            except ValueError:
                pass
        return s
    if t == "L":
        if s.upper() in ("Y", "T"): return True
        if s.upper() in ("N", "F"): return False
        return None if not s else s
    if t == "M":
        return _memo_value(raw, memo_path)
    return s

def read_fixed_width_dbf(path: str | Path) -> tuple[DBFInspection, list[str], list[list[Any]]]:
    p = Path(path)
    data = p.read_bytes()
    info = inspect_dbf(p)
    columns = [f.name for f in info.fields]
    rows: list[list[Any]] = []
    for i in range(info.record_count):
        start = info.header_length + i * info.record_length
        rec = data[start:start + info.record_length]
        if len(rec) < info.record_length:
            break
        if rec[:1] == b"*":
            continue
        pos = 1
        row = []
        for field in info.fields:
            raw = rec[pos:pos + field.length]
            row.append(_decode_field(raw, field, memo_path=info.memo_path))
            pos += field.length
        rows.append(row)
    return info, columns, rows
