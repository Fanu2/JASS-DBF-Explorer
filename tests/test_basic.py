from pathlib import Path
import ast

ROOT = Path(__file__).resolve().parents[1]
APP = ROOT / "jass_dbf_explorer" / "app.py"
SAMPLE = ROOT / "sample" / "sample_customers.dbf"

def test_source_parses():
    ast.parse(APP.read_text(encoding="utf-8"))

def test_sample_exists():
    assert SAMPLE.exists()
    assert SAMPLE.stat().st_size > 100

def test_requirements_present():
    text = (ROOT / "requirements.txt").read_text(encoding="utf-8")
    assert "PySide6" in text
    assert "dbfread" in text
