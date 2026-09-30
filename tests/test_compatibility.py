```python
from pathlib import Path

from jass_dbf_explorer.dbf_compat import (
    inspect_dbf,
    read_fixed_width_dbf,
)


# ---------------------------------------------------------------------------
# Repository-local test fixture
# ---------------------------------------------------------------------------
#
# The DBF fixture must be stored at:
#
#     tests/fixtures/sample.dbf
#
# This keeps the tests independent of development-machine paths such as
# /mnt/data/sample.dbf.
# ---------------------------------------------------------------------------

TESTS_DIR = Path(__file__).resolve().parent
FIXTURES_DIR = TESTS_DIR / "fixtures"
SAMPLE = FIXTURES_DIR / "sample.dbf"


def test_real_f5_sample():
    """Validate the FoxPro 2.x DBF structure and metadata."""

    info = inspect_dbf(SAMPLE)

    assert info.version_byte == 0xF5
    assert info.dialect == "FoxPro 2.x with memo"
    assert info.expected_memo_extension == ".FPT"

    assert info.record_count == 50
    assert info.header_length == 353
    assert info.record_length == 179
    assert len(info.fields) == 10

    assert info.memo_fields == ["ASD"]
    assert info.memo_path is None
    assert info.integrity_ok

    assert any(
        "companion .FPT file is missing" in warning
        for warning in info.warnings
    )


def test_fallback_reads_non_memo_fields():
    """Verify DBF reading when the companion FPT memo file is absent."""

    info, columns, rows = read_fixed_width_dbf(SAMPLE)

    assert columns[0] == "CUST_NO"

    assert rows
    assert len(rows[0]) == 10

    assert rows[0][1] == "Kauai Dive Shoppe"
    assert rows[0][-1] == "[Memo: companion file required]"

    # Two records in the source DBF are marked as deleted.
    assert len(rows) == 48


def test_fpt_memo_roundtrip(tmp_path):
    """Verify reading a FoxPro FPT memo field."""

    dbf = tmp_path / "memo.dbf"
    fpt = tmp_path / "memo.fpt"

    # -----------------------------------------------------------------------
    # Create a minimal FoxPro 2.x DBF containing one Memo field.
    #
    # Header length = 65 bytes
    # Record length = 5 bytes
    # -----------------------------------------------------------------------

    header = bytearray(65)

    header[0] = 0xF5
    header[4:8] = (1).to_bytes(4, "little")
    header[8:10] = (65).to_bytes(2, "little")
    header[10:12] = (5).to_bytes(2, "little")

    field = bytearray(32)

    field[0:4] = b"NOTE"
    field[11] = ord("M")
    field[16] = 4

    header[32:64] = field
    header[64] = 0x0D

    # One active record:
    # deletion flag + 4-byte big-endian FPT block pointer.
    record = b" " + (1).to_bytes(4, "big")

    dbf.write_bytes(
        bytes(header)
        + record
        + b"\x1A"
    )

    # -----------------------------------------------------------------------
    # Create the corresponding FoxPro FPT memo file.
    # -----------------------------------------------------------------------

    block_size = 512
    fpt_data = bytearray(block_size * 2)

    fpt_data[6:8] = block_size.to_bytes(2, "big")

    payload = b"Hello from FPT memo"
    start = block_size

    fpt_data[start:start + 4] = (1).to_bytes(4, "big")
    fpt_data[start + 4:start + 8] = len(payload).to_bytes(4, "big")
    fpt_data[
        start + 8:start + 8 + len(payload)
    ] = payload

    fpt.write_bytes(fpt_data)

    # -----------------------------------------------------------------------
    # Read the DBF and verify the decoded memo.
    # -----------------------------------------------------------------------

    info, columns, rows = read_fixed_width_dbf(dbf)

    assert info.memo_path is not None
    assert columns == ["NOTE"]
    assert rows == [["Hello from FPT memo"]]


def test_real_sample_with_matching_fpt_fixture(tmp_path):
    """Verify memo decoding using the repository-local DBF fixture."""

    dbf = tmp_path / "sample.dbf"
    fpt = tmp_path / "sample.fpt"

    # Copy the repository-local fixture into the temporary test directory.
    dbf.write_bytes(SAMPLE.read_bytes())

    # -----------------------------------------------------------------------
    # Create a synthetic FPT file containing memo blocks for the first
    # four records.
    # -----------------------------------------------------------------------

    block_size = 512
    fpt_data = bytearray(block_size * 10)

    fpt_data[6:8] = block_size.to_bytes(2, "big")

    memo_values = (
        (6, "Memo for customer 1"),
        (7, "Memo for customer 2"),
        (8, "Memo for customer 3"),
        (9, "Memo for customer 4"),
    )

    for block, text in memo_values:
        payload = text.encode("utf-8")
        start = block * block_size

        fpt_data[start:start + 4] = (1).to_bytes(4, "big")
        fpt_data[start + 4:start + 8] = len(payload).to_bytes(4, "big")
        fpt_data[
            start + 8:start + 8 + len(payload)
        ] = payload

    fpt.write_bytes(fpt_data)

    # -----------------------------------------------------------------------
    # Read the DBF with its matching FPT file and verify the memo values.
    # -----------------------------------------------------------------------

    info, columns, rows = read_fixed_width_dbf(dbf)

    assert info.memo_path == str(fpt)

    assert rows[0][-1] == "Memo for customer 1"
    assert rows[1][-1] == "Memo for customer 2"
    assert rows[2][-1] == "Memo for customer 3"
    assert rows[3][-1] == "Memo for customer 4"
```
