from pathlib import Path
import sqlite3
from jass_dbf_explorer.migration import sqlite_type, safe_identifier, build_create_sql, build_insert_sql, export_sqlite
from jass_dbf_explorer.dbf_compat import DBFField

def test_type_mapping():
    assert sqlite_type('C') == 'TEXT'
    assert sqlite_type('N', decimals=0) == 'INTEGER'
    assert sqlite_type('N', decimals=2) == 'REAL'
    assert sqlite_type('M') == 'TEXT'

def test_identifier():
    assert safe_identifier('Customer Data.dbf') == 'customer_data_dbf'
    assert safe_identifier('123') == '_123'

def test_sql_generation():
    fields = [DBFField('ID','N',5,0), DBFField('NAME','C',20,0)]
    sql = build_create_sql('Customers', fields)
    assert '"ID" INTEGER' in sql
    assert '"NAME" TEXT' in sql
    ins = build_insert_sql('Customers', ['ID','NAME'], [[1, 'O\'Brien']])
    assert "O''Brien" in ins

def test_sqlite_export(tmp_path):
    fields = [DBFField('ID','N',5,0), DBFField('NAME','C',20,0)]
    out = tmp_path / 'x.sqlite'
    count = export_sqlite(out, 'Customers', fields, ['ID','NAME'], [[1,'A'],[2,'B']])
    assert count == 2
    con = sqlite3.connect(out)
    rows = con.execute('select ID, NAME from customers order by ID').fetchall()
    con.close()
    assert rows == [(1,'A'),(2,'B')]

def test_data_quality_and_mapping():
    from jass_dbf_explorer.migration import analyze_columns, apply_mapping
    cols=['NAME','CITY']
    rows=[[' Alice ','Delhi'],['Bob','Delhi'],['','Mumbai']]
    stats=analyze_columns(cols,rows)
    assert stats[0]['blank']==1
    assert stats[1]['unique']==2
    mapped_cols,mapped_rows=apply_mapping(cols,rows,[
        {'source':'NAME','target':'customer_name','include':True,'transform':'Trim text'},
        {'source':'CITY','target':'city_name','include':True,'transform':'Uppercase'},
    ])
    assert mapped_cols==['customer_name','city_name']
    assert mapped_rows[0]==['Alice','DELHI']

from jass_dbf_explorer.migration import candidate_keys, validate_migration, duplicate_summary


def test_candidate_key_and_duplicate_summary():
    columns=['ID','NAME']
    rows=[[1,'A'],[2,'B'],[3,'B']]
    assert duplicate_summary(columns,rows,0)['duplicate_rows'] == 0
    assert duplicate_summary(columns,rows,1)['duplicate_rows'] == 1
    assert candidate_keys(columns,rows)[0]['column'] == 'ID'


def test_migration_validation_rejects_duplicate_primary_key():
    columns=['ID','NAME']
    rows=[[1,'A'],[1,'B']]
    mapping=[{'source':'ID','target':'id','include':True,'transform':'None'}, {'source':'NAME','target':'name','include':True,'transform':'None'}]
    result=validate_migration(columns,rows,mapping,'customers','id')
    assert not result['ok']
    assert result['duplicates'] == 1
    assert any('duplicate' in e.lower() for e in result['errors'])
