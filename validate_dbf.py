from tests.dbf_validator import inspect_dbf
import sys
if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: python validate_dbf.py path/to/file.dbf")
    print(inspect_dbf(sys.argv[1]))
