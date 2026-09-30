from pathlib import Path
import struct

def inspect_dbf(path):
    data = Path(path).read_bytes()
    if len(data) < 33:
        raise ValueError("File is too small to be a DBF.")
    version = data[0]
    record_count = struct.unpack_from("<I", data, 4)[0]
    header_len = struct.unpack_from("<H", data, 8)[0]
    record_len = struct.unpack_from("<H", data, 10)[0]
    fields = []
    pos = 32
    while pos + 32 <= len(data) and data[pos] != 0x0D:
        raw = data[pos:pos+32]
        name = raw[:11].split(b"\0",1)[0].decode("ascii", "replace")
        fields.append((name, chr(raw[11]), raw[16], raw[17]))
        pos += 32
    expected_record_len = 1 + sum(f[2] for f in fields)
    expected_header_len = 32 + 32 * len(fields) + 1
    return {
        "version": version, "record_count": record_count,
        "header_length": header_len, "record_length": record_len,
        "expected_header_length": expected_header_len,
        "expected_record_length": expected_record_len,
        "fields": fields, "file_size": len(data),
        "payload_end": header_len + record_count * record_len,
        "terminator_present": data[-1:] == bytes([0x1A]),
    }
