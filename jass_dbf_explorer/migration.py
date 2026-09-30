from __future__ import annotations
import re
import sqlite3
from pathlib import Path
from typing import Any
from collections import Counter


def sqlite_type(field_type: str, length: int = 0, decimals: int = 0) -> str:
    t = field_type.upper()
    if t in {'N', 'F', 'I', 'B', 'Y'}:
        return 'REAL' if decimals else 'INTEGER'
    if t == 'D': return 'TEXT'
    if t == 'L': return 'INTEGER'
    if t == 'M': return 'TEXT'
    if t in {'C', 'V', 'Q', 'W', 'T', 'G'}: return 'TEXT'
    return 'TEXT'


def safe_identifier(name: str, fallback: str = 'table_name') -> str:
    value = re.sub(r'[^A-Za-z0-9_]+', '_', name).strip('_') or fallback
    if value[0].isdigit(): value = '_' + value
    return value.lower()


def quote_ident(name: str) -> str:
    return '"' + str(name).replace('"', '""') + '"'


def build_create_sql(table_name: str, fields, primary_key: str | None = None) -> str:
    table = safe_identifier(table_name)
    definitions = [f'    {quote_ident(f.name)} {sqlite_type(f.type, f.length, f.decimal_count)}' for f in fields]
    if primary_key:
        definitions.append(f'    PRIMARY KEY ({quote_ident(primary_key)})')
    return f'CREATE TABLE {quote_ident(table)} (\n' + ',\n'.join(definitions) + '\n);'


def build_insert_sql(table_name: str, columns: list[str], rows: list[list[Any]]) -> str:
    table = safe_identifier(table_name)
    cols = ', '.join(quote_ident(c) for c in columns)
    statements = []
    for row in rows:
        vals = []
        for v in row:
            if v is None or v == '': vals.append('NULL')
            elif isinstance(v, bool): vals.append('1' if v else '0')
            elif isinstance(v, (int, float)): vals.append(str(v))
            else: vals.append("'" + str(v).replace("'", "''") + "'")
        statements.append(f'INSERT INTO {quote_ident(table)} ({cols}) VALUES (' + ', '.join(vals) + ');')
    return '\n'.join(statements)


def export_sqlite(path: str | Path, table_name: str, fields, columns: list[str], rows: list[list[Any]], primary_key: str | None = None) -> int:
    target = Path(path)
    conn = sqlite3.connect(target)
    try:
        table = safe_identifier(table_name)
        conn.execute(f'DROP TABLE IF EXISTS {quote_ident(table)}')
        definitions = ', '.join(f'{quote_ident(f.name)} {sqlite_type(f.type, f.length, f.decimal_count)}' for f in fields)
        if primary_key: definitions += f', PRIMARY KEY ({quote_ident(primary_key)})'
        conn.execute(f'CREATE TABLE {quote_ident(table)} ({definitions})')
        placeholders = ','.join('?' for _ in columns)
        conn.executemany(f'INSERT INTO {quote_ident(table)} ({", ".join(quote_ident(c) for c in columns)}) VALUES ({placeholders})', rows)
        conn.commit()
        return len(rows)
    finally: conn.close()


def analyze_columns(columns, rows):
    result = []
    for i, name in enumerate(columns):
        vals = [r[i] if i < len(r) else None for r in rows]
        nonblank = [v for v in vals if v is not None and str(v).strip() != '']
        unique = {str(v) for v in nonblank}
        numeric_bad = 0
        for v in nonblank:
            if isinstance(v, (int, float)) and not isinstance(v, bool): continue
            try: float(str(v).strip())
            except Exception: numeric_bad += 1
        result.append({'column': name, 'rows': len(vals), 'populated': len(nonblank), 'blank': len(vals)-len(nonblank), 'unique': len(unique), 'duplicate_values': max(0,len(nonblank)-len(unique)), 'numeric_invalid': numeric_bad})
    return result


def apply_mapping(columns, rows, mapping):
    indexes = {c:i for i,c in enumerate(columns)}
    out_cols, out_rows = [], []
    active = [m for m in mapping if m.get('include', True) and m.get('source') in indexes]
    for m in active:
        target = safe_identifier(m.get('target') or m['source'])
        if target in out_cols: raise ValueError(f'Duplicate target field: {target}')
        out_cols.append(target)
    for row in rows:
        new_row=[]
        for m in active:
            value=row[indexes[m['source']]] if indexes[m['source']] < len(row) else None
            transform=m.get('transform','None')
            if value is not None and transform=='Trim text': value=str(value).strip()
            elif value is not None and transform=='Uppercase': value=str(value).upper()
            elif value is not None and transform=='Lowercase': value=str(value).lower()
            elif transform=='Blank → NULL' and (value is None or str(value).strip()==''): value=None
            new_row.append(value)
        out_rows.append(new_row)
    return out_cols,out_rows


def duplicate_summary(columns, rows, column_index: int) -> dict[str, Any]:
    vals=[str(r[column_index]).strip() for r in rows if column_index < len(r) and r[column_index] not in (None,'')]
    counts=Counter(vals)
    duplicates={k:v for k,v in counts.items() if v>1}
    return {'populated':len(vals),'unique':len(counts),'duplicate_values':len(duplicates),'duplicate_rows':sum(v-1 for v in duplicates.values()),'examples':sorted(duplicates.items(),key=lambda x:(-x[1],x[0]))[:10]}


def candidate_keys(columns, rows, max_examples: int = 10) -> list[dict[str, Any]]:
    candidates=[]
    for i,col in enumerate(columns):
        s=duplicate_summary(columns,rows,i)
        if s['populated']==len(rows) and s['unique']==len(rows) and rows:
            candidates.append({'column':col,'confidence':'high','reason':'All rows populated and unique','examples':s['examples'][:max_examples]})
        elif rows and s['populated']>=max(1,int(len(rows)*0.98)) and s['unique']==s['populated']:
            candidates.append({'column':col,'confidence':'possible','reason':'Nearly all rows populated and unique','examples':s['examples'][:max_examples]})
    return candidates


def validate_migration(columns, rows, mapping, target_table: str, primary_key: str | None = None) -> dict[str, Any]:
    errors=[]; warnings=[]
    try: out_cols,out_rows=apply_mapping(columns,rows,mapping)
    except Exception as exc: return {'ok':False,'errors':[str(exc)],'warnings':[],'rows':0,'columns':0,'primary_key':primary_key,'duplicates':0}
    if not safe_identifier(target_table): errors.append('Target table name is empty or invalid.')
    if not out_cols: errors.append('No fields are included in the migration.')
    if len(set(out_cols)) != len(out_cols): errors.append('Target field names are not unique.')
    if primary_key and primary_key not in out_cols: errors.append(f'Primary key field is not included: {primary_key}')
    duplicate_pk=0
    if primary_key and primary_key in out_cols:
        idx=out_cols.index(primary_key); summary=duplicate_summary(out_cols,out_rows,idx)
        duplicate_pk=summary['duplicate_rows']
        if summary['populated'] < len(out_rows): errors.append(f'Primary key has {len(out_rows)-summary["populated"]} blank value(s).')
        if duplicate_pk: errors.append(f'Primary key has {duplicate_pk} duplicate row(s).')
    if not out_rows: warnings.append('The migration contains zero records.')
    return {'ok':not errors,'errors':errors,'warnings':warnings,'rows':len(out_rows),'columns':len(out_cols),'primary_key':primary_key,'duplicates':duplicate_pk,'target_columns':out_cols}
