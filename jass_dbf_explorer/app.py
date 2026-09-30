from __future__ import annotations

import csv
import json
import os
import sys
from pathlib import Path
from datetime import date, datetime, time

from .dbf_compat import inspect_dbf, read_fixed_width_dbf
from .migration import build_create_sql, build_insert_sql, export_sqlite, safe_identifier, analyze_columns, apply_mapping, candidate_keys, validate_migration, duplicate_summary
from typing import Any

from PySide6.QtCore import Qt, QSettings, QSortFilterProxyModel, QAbstractTableModel, QModelIndex, Signal
from PySide6.QtGui import QAction, QIcon, QFont
from PySide6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, QLabel,
    QPushButton, QFileDialog, QTableView, QLineEdit, QComboBox, QStatusBar,
    QMessageBox, QSplitter, QTreeWidget, QTreeWidgetItem, QTabWidget,
    QDialog, QDialogButtonBox, QPlainTextEdit, QTableWidget, QTableWidgetItem,
    QToolBar, QHeaderView, QGroupBox, QFormLayout, QCheckBox, QTextEdit
)

try:
    from dbfread import DBF
except ImportError:
    DBF = None


APP_NAME = "JASS DBF Explorer"
VERSION = "1.6.0 Migration Studio"


def safe_text(value: Any) -> str:
    if value is None:
        return ""
    if isinstance(value, (datetime, date, time)):
        return value.isoformat(sep=" ") if isinstance(value, datetime) else value.isoformat()
    return str(value)


class DBFTableModel(QAbstractTableModel):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.columns: list[str] = []
        self.rows: list[list[Any]] = []
        self.source_path = ""

    def set_data(self, columns, rows, source_path=""):
        self.beginResetModel()
        self.columns = list(columns)
        self.rows = list(rows)
        self.source_path = source_path
        self.endResetModel()

    def clear(self):
        self.set_data([], [])

    def rowCount(self, parent=QModelIndex()):
        return 0 if parent.isValid() else len(self.rows)

    def columnCount(self, parent=QModelIndex()):
        return 0 if parent.isValid() else len(self.columns)

    def data(self, index, role=Qt.DisplayRole):
        if not index.isValid():
            return None
        value = self.rows[index.row()][index.column()]
        if role == Qt.DisplayRole:
            return safe_text(value)
        if role == Qt.ToolTipRole:
            return safe_text(value)
        return None

    def headerData(self, section, orientation, role=Qt.DisplayRole):
        if role != Qt.DisplayRole:
            return None
        if orientation == Qt.Horizontal and section < len(self.columns):
            return self.columns[section]
        if orientation == Qt.Vertical:
            return str(section + 1)
        return None

    def sort(self, column, order=Qt.AscendingOrder):
        if not self.rows or column >= len(self.columns):
            return
        self.layoutAboutToBeChanged.emit()
        self.rows.sort(
            key=lambda r: (r[column] is None, safe_text(r[column]).lower()),
            reverse=order == Qt.DescendingOrder,
        )
        self.layoutChanged.emit()


class DBFProxy(QSortFilterProxyModel):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.search_text = ""
        self.search_column = -1
        self.setDynamicSortFilter(True)

    def set_search(self, text, column=-1):
        self.search_text = text.lower().strip()
        self.search_column = column
        self.invalidateFilter()

    def filterAcceptsRow(self, source_row, source_parent):
        if not self.search_text:
            return True
        model = self.sourceModel()
        columns = range(model.columnCount())
        if self.search_column >= 0:
            columns = [self.search_column]
        for col in columns:
            idx = model.index(source_row, col, source_parent)
            value = model.data(idx, Qt.DisplayRole) or ""
            if self.search_text in value.lower():
                return True
        return False


class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.settings = QSettings("JASS Digital Lab", "JASS DBF Explorer")
        self.current_path = ""
        self.current_dbf = None
        self.columns = []
        self.rows = []
        self.setWindowTitle(f"{APP_NAME} — {VERSION}")
        self.resize(1280, 760)
        self.setMinimumSize(980, 620)
        self._build_ui()
        self._build_actions()
        self._restore_recent()
        self.statusBar().showMessage("Ready — open a DBF file to begin.")

    def _build_ui(self):
        root = QWidget()
        outer = QVBoxLayout(root)
        outer.setContentsMargins(14, 14, 14, 10)
        outer.setSpacing(10)

        title_row = QHBoxLayout()
        title = QLabel("JASS DBF Explorer")
        title.setObjectName("Title")
        subtitle = QLabel("Legacy data, finally visible.")
        subtitle.setObjectName("Subtitle")
        title_col = QVBoxLayout()
        title_col.addWidget(title)
        title_col.addWidget(subtitle)
        title_row.addLayout(title_col)
        title_row.addStretch()
        self.file_label = QLabel("No DBF loaded")
        self.file_label.setObjectName("FileLabel")
        title_row.addWidget(self.file_label)
        outer.addLayout(title_row)

        search_row = QHBoxLayout()
        self.search = QLineEdit()
        self.search.setPlaceholderText("Search records…")
        self.search.setClearButtonEnabled(True)
        self.column_combo = QComboBox()
        self.column_combo.addItem("All fields", -1)
        self.open_btn = QPushButton("Open DBF")
        self.open_btn.setObjectName("PrimaryButton")
        self.export_btn = QPushButton("Export CSV")
        self.export_btn.setEnabled(False)
        self.profile_btn = QPushButton("Profile Data")
        self.compat_btn = QPushButton("Compatibility")
        self.migrate_btn = QPushButton("Migration Workbench")
        self.compat_btn.setEnabled(False)
        self.migrate_btn.setEnabled(False)
        self.profile_btn.setEnabled(False)
        search_row.addWidget(self.search, 1)
        search_row.addWidget(self.column_combo)
        search_row.addWidget(self.open_btn)
        search_row.addWidget(self.export_btn)
        search_row.addWidget(self.profile_btn)
        search_row.addWidget(self.compat_btn)
        search_row.addWidget(self.migrate_btn)
        outer.addLayout(search_row)

        splitter = QSplitter(Qt.Horizontal)

        left = QWidget()
        left_layout = QVBoxLayout(left)
        left_layout.setContentsMargins(0, 0, 0, 0)

        info_group = QGroupBox("Database")
        form = QFormLayout(info_group)
        self.info_file = QLabel("—")
        self.info_records = QLabel("—")
        self.info_fields = QLabel("—")
        self.info_encoding = QLabel("—")
        for w in (self.info_file, self.info_records, self.info_fields, self.info_encoding):
            w.setTextInteractionFlags(Qt.TextSelectableByMouse)
        form.addRow("File:", self.info_file)
        form.addRow("Records:", self.info_records)
        form.addRow("Fields:", self.info_fields)
        form.addRow("Encoding:", self.info_encoding)
        left_layout.addWidget(info_group)

        schema_group = QGroupBox("Fields")
        schema_layout = QVBoxLayout(schema_group)
        self.schema_tree = QTreeWidget()
        self.schema_tree.setHeaderLabels(["Field", "Type", "Length", "Decimal"])
        self.schema_tree.setRootIsDecorated(False)
        self.schema_tree.setAlternatingRowColors(True)
        self.schema_tree.header().setStretchLastSection(False)
        self.schema_tree.header().setSectionResizeMode(0, QHeaderView.Stretch)
        for i in range(1, 4):
            self.schema_tree.header().setSectionResizeMode(i, QHeaderView.ResizeToContents)
        schema_layout.addWidget(self.schema_tree)
        left_layout.addWidget(schema_group, 1)

        splitter.addWidget(left)

        table_wrap = QWidget()
        table_layout = QVBoxLayout(table_wrap)
        table_layout.setContentsMargins(0, 0, 0, 0)

        self.table = QTableView()
        self.model = DBFTableModel(self)
        self.proxy = DBFProxy(self)
        self.proxy.setSourceModel(self.model)
        self.table.setModel(self.proxy)
        self.table.setSortingEnabled(True)
        self.table.setAlternatingRowColors(True)
        self.table.setSelectionBehavior(QTableView.SelectRows)
        self.table.setWordWrap(False)
        self.table.horizontalHeader().setStretchLastSection(True)
        self.table.verticalHeader().setDefaultSectionSize(28)
        table_layout.addWidget(self.table)

        self.result_label = QLabel("0 records")
        self.result_label.setObjectName("Muted")
        table_layout.addWidget(self.result_label)

        splitter.addWidget(table_wrap)
        splitter.setSizes([350, 850])
        outer.addWidget(splitter, 1)

        profile_group = QGroupBox("Data Profile")
        profile_layout = QVBoxLayout(profile_group)
        self.profile_text = QTextEdit()
        self.profile_text.setReadOnly(True)
        self.profile_text.setPlaceholderText("Open a DBF file and choose Profile Data.")
        profile_layout.addWidget(self.profile_text)
        outer.addWidget(profile_group)
        profile_group.setMaximumHeight(185)

        root.setLayout(outer)
        self.setCentralWidget(root)

        self.open_btn.clicked.connect(self.open_dbf)
        self.export_btn.clicked.connect(self.export_csv)
        self.profile_btn.clicked.connect(self.profile_data)
        self.compat_btn.clicked.connect(self.compatibility_report)
        self.migrate_btn.clicked.connect(self.migration_workbench)
        self.search.textChanged.connect(self._search_changed)
        self.column_combo.currentIndexChanged.connect(self._search_changed)

    def _build_actions(self):
        toolbar = QToolBar("Main")
        toolbar.setMovable(False)
        self.addToolBar(toolbar)

        open_action = QAction("Open", self)
        open_action.triggered.connect(self.open_dbf)
        toolbar.addAction(open_action)

        export_action = QAction("Export CSV", self)
        export_action.triggered.connect(self.export_csv)
        toolbar.addAction(export_action)

        toolbar.addSeparator()

        clear_action = QAction("Clear", self)
        clear_action.triggered.connect(self.clear_dbf)
        toolbar.addAction(clear_action)

        toolbar.addSeparator()

        about_action = QAction("About", self)
        about_action.triggered.connect(self.about)
        toolbar.addAction(about_action)

    def _restore_recent(self):
        recent = self.settings.value("recent", [])
        if isinstance(recent, str):
            recent = [recent]
        self.recent = [p for p in recent if Path(p).exists()][:8]

    def _save_recent(self, path):
        recent = [path] + [p for p in getattr(self, "recent", []) if p != path]
        self.recent = recent[:8]
        self.settings.setValue("recent", self.recent)

    def open_dbf(self):
        path, _ = QFileDialog.getOpenFileName(
            self, "Open DBF File", "", "DBF files (*.dbf *.DBF);;All files (*.*)"
        )
        if path:
            self.load_dbf(path)

    def load_dbf(self, path):
        try:
            self.statusBar().showMessage("Inspecting DBF header…")
            QApplication.processEvents()
            info = inspect_dbf(path)
            fields = info.fields
            columns = [f.name for f in fields]
            rows = None
            table = None

            # Prefer dbfread when it can open the file, but fall back to our
            # fixed-width parser so a missing .FPT/.DBT does not hide usable data.
            if DBF is not None:
                try:
                    table = DBF(path, load=True, char_decode_errors="replace")
                    rows = [[record.get(col) for col in columns] for record in table]
                except Exception:
                    rows = None

            if rows is None:
                info, columns, rows = read_fixed_width_dbf(path)

            self.current_dbf = table
            self.current_path = path
            self.current_info = info
            self.columns = columns
            self.rows = rows
            self.model.set_data(columns, rows, path)
            self._populate_schema(fields)
            self._populate_columns(columns)
            self._update_info(table, info)
            self._save_recent(path)
            self.file_label.setText(Path(path).name)
            self.export_btn.setEnabled(True)
            self.profile_btn.setEnabled(True)
            self.compat_btn.setEnabled(True)
            self.profile_text.clear()
            self.compatibility_report(show_status=False)
            self.statusBar().showMessage(f"Loaded {len(rows):,} records — {info.dialect}.")
        except Exception as exc:
            QMessageBox.critical(
                self, "Unable to open DBF",
                f"JASS DBF Explorer could not open this file.\n\n{exc}"
            )
            self.statusBar().showMessage("Open failed.")

    def _populate_schema(self, fields):
        self.schema_tree.clear()
        for f in fields:
            item = QTreeWidgetItem([
                str(f.name),
                str(f.type),
                str(getattr(f, "length", "")),
                str(getattr(f, "decimal_count", "")),
            ])
            self.schema_tree.addTopLevelItem(item)

    def _populate_columns(self, columns):
        self.column_combo.blockSignals(True)
        self.column_combo.clear()
        self.column_combo.addItem("All fields", -1)
        for i, col in enumerate(columns):
            self.column_combo.addItem(col, i)
        self.column_combo.blockSignals(False)

    def _update_info(self, table, info):
        self.info_file.setText(Path(self.current_path).name)
        self.info_records.setText(f"{len(self.rows):,}")
        self.info_fields.setText(str(len(self.columns)))
        encoding = getattr(table, "encoding", None) if table is not None else None
        self.info_encoding.setText(str(encoding or f"code page 0x{info.codepage_mark:02X}"))
        self.result_label.setText(f"{len(self.rows):,} records • {len(self.columns)} fields")

    def _search_changed(self):
        col = self.column_combo.currentData()
        self.proxy.set_search(self.search.text(), -1 if col is None else int(col))
        self.result_label.setText(
            f"{self.proxy.rowCount():,} of {len(self.rows):,} records shown • {len(self.columns)} fields"
        )

    def export_csv(self):
        if not self.rows:
            return
        path, _ = QFileDialog.getSaveFileName(
            self, "Export CSV", f"{Path(self.current_path).stem}.csv", "CSV files (*.csv)"
        )
        if not path:
            return
        try:
            with open(path, "w", newline="", encoding="utf-8-sig") as fh:
                writer = csv.writer(fh)
                writer.writerow(self.columns)
                for proxy_row in range(self.proxy.rowCount()):
                    source_index = self.proxy.mapToSource(self.proxy.index(proxy_row, 0)).row()
                    writer.writerow([safe_text(v) for v in self.rows[source_index]])
            self.statusBar().showMessage(f"Exported {self.proxy.rowCount():,} records.")
        except Exception as exc:
            QMessageBox.critical(self, "Export failed", str(exc))

    def profile_data(self):
        if not self.rows or not self.columns:
            return
        lines = [
            f"<b>{self.current_path}</b>",
            f"Records: {len(self.rows):,} &nbsp; | &nbsp; Fields: {len(self.columns)}",
            "",
        ]
        for col_index, col in enumerate(self.columns):
            values = [r[col_index] for r in self.rows]
            nonempty = [v for v in values if v not in (None, "")]
            unique = len({safe_text(v) for v in nonempty})
            numeric = 0
            dates = 0
            for v in nonempty:
                if isinstance(v, (int, float)):
                    numeric += 1
                elif isinstance(v, (date, datetime)):
                    dates += 1
            sample = ", ".join(safe_text(v)[:30] for v in nonempty[:3])
            lines.append(
                f"<b>{col}</b> — populated {len(nonempty):,}/{len(values):,}; "
                f"unique {unique:,}; numeric {numeric:,}; dates {dates:,}"
                + (f"; samples: {sample}" if sample else "")
            )
        self.profile_text.setHtml("<br>".join(lines))
        self.statusBar().showMessage("Data profile generated.")

    def compatibility_report(self, show_status=True):
        info = getattr(self, "current_info", None)
        if info is None:
            return
        lines = [
            f"<h3>DBF Compatibility Report</h3>",
            f"<b>Dialect:</b> {info.dialect}",
            f"<b>Version byte:</b> 0x{info.version_byte:02X}",
            f"<b>Expected memo:</b> {info.expected_memo_extension}",
            f"<b>Records:</b> {info.record_count:,}",
            f"<b>Header length:</b> {info.header_length:,} bytes",
            f"<b>Record length:</b> {info.record_length:,} bytes",
            f"<b>Integrity:</b> {'OK' if info.integrity_ok else 'WARNING'}",
        ]
        if info.memo_fields:
            lines.append(f"<b>Memo fields:</b> {', '.join(info.memo_fields)}")
            if info.memo_path:
                lines.append(f"<b>Memo file:</b> {info.memo_path}")
            else:
                lines.append("<b>Memo status:</b> companion file not found; memo values are shown as placeholders.")
        if info.warnings:
            lines.append("<br><b>Warnings</b>")
            lines.extend(f"• {w}" for w in info.warnings)
        else:
            lines.append("<br><b>No compatibility warnings detected.</b>")
        self.profile_text.setHtml("<br>".join(lines))
        if show_status:
            self.statusBar().showMessage("Compatibility report generated.")

    def migration_workbench(self):
        if not getattr(self, "current_info", None) or not self.rows:
            return
        dlg = QDialog(self)
        dlg.setWindowTitle("JASS DBF Explorer — Migration Studio")
        dlg.resize(1180, 820)
        layout = QVBoxLayout(dlg)
        layout.addWidget(QLabel("Prepare, validate, preview and export a production-ready SQLite migration. The source DBF remains read-only."))
        tabs = QTabWidget(); layout.addWidget(tabs, 1)

        mapping_tab=QWidget(); ml=QVBoxLayout(mapping_tab)
        form=QFormLayout(); name_edit=QLineEdit(safe_identifier(Path(self.current_path).stem)); form.addRow("Target table:",name_edit); ml.addLayout(form)
        table=QTableWidget(len(self.columns),5); table.setHorizontalHeaderLabels(["Source field","Include","Target field","SQLite type","Transform"]); table.horizontalHeader().setStretchLastSection(True)
        transforms=["None","Trim text","Uppercase","Lowercase","Blank → NULL"]
        for i,col in enumerate(self.columns):
            table.setItem(i,0,QTableWidgetItem(col)); table.item(i,0).setFlags(table.item(i,0).flags() & ~Qt.ItemIsEditable)
            cb=QCheckBox(); cb.setChecked(True); table.setCellWidget(i,1,cb)
            table.setItem(i,2,QTableWidgetItem(safe_identifier(col)))
            f=next((x for x in self.current_info.fields if x.name==col),None)
            from .migration import sqlite_type
            table.setItem(i,3,QTableWidgetItem("TEXT" if f is None else sqlite_type(f.type,f.length,f.decimal_count)))
            combo=QComboBox(); combo.addItems(transforms); table.setCellWidget(i,4,combo)
        ml.addWidget(table,1)
        pk_row=QHBoxLayout(); pk_row.addWidget(QLabel("Primary key:")); pk_combo=QComboBox(); pk_combo.addItem("None",""); pk_row.addWidget(pk_combo,1); suggest=QLabel(); pk_row.addWidget(suggest); ml.addLayout(pk_row)
        tabs.addTab(mapping_tab,"Field Mapping")

        quality_tab=QWidget(); ql=QVBoxLayout(quality_tab)
        qt=QTableWidget(); qt.setColumnCount(7); qt.setHorizontalHeaderLabels(["Field","Populated","Blank","Unique","Duplicates","Numeric invalid","Key candidate"]); qt.horizontalHeader().setStretchLastSection(True); ql.addWidget(qt)
        stats=analyze_columns(self.columns,self.rows); keys=candidate_keys(self.columns,self.rows); key_names={x['column'] for x in keys}
        qt.setRowCount(len(stats))
        for i,x in enumerate(stats):
            vals=[x['column'],x['populated'],x['blank'],x['unique'],x['duplicate_values'],x['numeric_invalid'],"HIGH" if any(k['column']==x['column'] and k['confidence']=='high' for k in keys) else ("POSSIBLE" if x['column'] in key_names else "—")]
            for j,v in enumerate(vals): qt.setItem(i,j,QTableWidgetItem(str(v)))
        ql.addWidget(QLabel(f"{len(self.rows):,} rows analyzed • {sum(x['blank'] for x in stats):,} blank cells • {sum(x['duplicate_values'] for x in stats):,} duplicate values"))
        tabs.addTab(quality_tab,"Data Quality")

        dup_tab=QWidget(); dl=QVBoxLayout(dup_tab); dup_table=QTableWidget(); dup_table.setColumnCount(5); dup_table.setHorizontalHeaderLabels(["Field","Populated","Unique","Duplicate rows","Duplicate examples"]); dup_table.horizontalHeader().setStretchLastSection(True); dl.addWidget(dup_table)
        dup_rows=[]
        for i,col in enumerate(self.columns):
            d=duplicate_summary(self.columns,self.rows,i)
            if d['duplicate_values']:
                ex=', '.join(f"{k} ({v})" for k,v in d['examples'])
                dup_rows.append((col,d['populated'],d['unique'],d['duplicate_rows'],ex))
        dup_table.setRowCount(len(dup_rows))
        for r,row in enumerate(dup_rows):
            for c,v in enumerate(row): dup_table.setItem(r,c,QTableWidgetItem(str(v)))
        dl.addWidget(QLabel(f"Fields containing duplicate values: {len(dup_rows)}"))
        tabs.addTab(dup_tab,"Duplicates")

        validation_tab=QWidget(); vl=QVBoxLayout(validation_tab); validation_text=QPlainTextEdit(); validation_text.setReadOnly(True); vl.addWidget(validation_text); tabs.addTab(validation_tab,"Validation")
        preview_tab=QWidget(); pl=QVBoxLayout(preview_tab); preview=QPlainTextEdit(); preview.setReadOnly(True); pl.addWidget(preview); tabs.addTab(preview_tab,"Migration Preview")

        def current_mapping():
            result=[]
            for i,col in enumerate(self.columns):
                cb=table.cellWidget(i,1); combo=table.cellWidget(i,4)
                result.append({'source':col,'target':table.item(i,2).text(),'include':cb.isChecked(),'transform':combo.currentText()})
            return result
        def refresh_pk():
            current=pk_combo.currentData(); pk_combo.blockSignals(True); pk_combo.clear(); pk_combo.addItem("None","")
            mapping=current_mapping()
            for m in mapping:
                if m['include']: pk_combo.addItem(m['target'],m['target'])
            if current:
                ix=pk_combo.findData(current)
                if ix>=0: pk_combo.setCurrentIndex(ix)
            pk_combo.blockSignals(False)
            suggested=[k for k in keys if any(m['source']==k['column'] and m['include'] for m in mapping)]
            suggest.setText("Suggested: " + (suggested[0]['column'] if suggested else "none"))
        def refresh():
            try:
                mapping=current_mapping(); refresh_pk(); pk=pk_combo.currentData() or None
                result=validate_migration(self.columns,self.rows,mapping,name_edit.text(),pk)
                cols,rows=apply_mapping(self.columns,self.rows[:3],mapping)
                by={f.name:f for f in self.current_info.fields}; mapped_fields=[]
                for m in mapping:
                    if m['include']:
                        f=by[m['source']]; mapped_fields.append(type(f)(safe_identifier(m['target']),f.type,f.length,f.decimal_count))
                validation_lines=["MIGRATION VALIDATION", "="*60, f"Status: {'PASS' if result['ok'] else 'FAIL'}", f"Rows: {result['rows']:,}", f"Columns: {result['columns']}", f"Target table: {safe_identifier(name_edit.text())}", f"Primary key: {pk or 'None'}", ""]
                if result['errors']: validation_lines += ["ERRORS:"] + [f"- {x}" for x in result['errors']]
                if result['warnings']: validation_lines += ["WARNINGS:"] + [f"- {x}" for x in result['warnings']]
                if not result['errors'] and not result['warnings']: validation_lines.append("No validation issues detected.")
                validation_text.setPlainText("\n".join(validation_lines))
                preview.setPlainText(build_create_sql(name_edit.text(),mapped_fields,pk)+"\n\n-- First 3 mapped rows\n"+build_insert_sql(name_edit.text(),cols,rows))
            except Exception as exc:
                validation_text.setPlainText(f"VALIDATION ERROR\n\n{exc}"); preview.setPlainText(f"Migration preview error: {exc}")
        name_edit.textChanged.connect(refresh)
        table.cellChanged.connect(lambda *_: refresh())
        for i in range(table.rowCount()):
            table.cellWidget(i,1).stateChanged.connect(refresh); table.cellWidget(i,4).currentTextChanged.connect(refresh)
        pk_combo.currentIndexChanged.connect(refresh)
        refresh_pk(); refresh()
        buttons=QDialogButtonBox(QDialogButtonBox.Close)
        validate_btn=buttons.addButton("Validate Migration",QDialogButtonBox.ActionRole); export_sql_btn=buttons.addButton("Export Validated SQL",QDialogButtonBox.ActionRole); export_sqlite_btn=buttons.addButton("Create Validated SQLite DB",QDialogButtonBox.ActionRole)
        buttons.rejected.connect(dlg.reject)
        validate_btn.clicked.connect(lambda: (refresh(), tabs.setCurrentWidget(validation_tab)))
        def do_sql():
            refresh(); result=validate_migration(self.columns,self.rows,current_mapping(),name_edit.text(),pk_combo.currentData() or None)
            if result['ok']: self._export_mapped_sql(name_edit.text(),current_mapping(),pk_combo.currentData() or None)
            else: QMessageBox.warning(dlg,"Migration blocked","Validation failed. Fix the errors shown on the Validation tab before exporting.")
        def do_db():
            refresh(); result=validate_migration(self.columns,self.rows,current_mapping(),name_edit.text(),pk_combo.currentData() or None)
            if result['ok']: self._export_mapped_sqlite(name_edit.text(),current_mapping(),pk_combo.currentData() or None)
            else: QMessageBox.warning(dlg,"Migration blocked","Validation failed. Fix the errors shown on the Validation tab before creating the SQLite database.")
        export_sql_btn.clicked.connect(do_sql); export_sqlite_btn.clicked.connect(do_db); layout.addWidget(buttons)
        dlg.exec()

    def _mapped_payload(self, table_name, mapping, primary_key=None):
        cols, rows = apply_mapping(self.columns, self.rows, mapping)
        by={f.name:f for f in self.current_info.fields}; fields=[]
        for m,c in zip([x for x in mapping if x.get('include',True)],cols):
            f=by[m['source']]; fields.append(type(f)(c,f.type,f.length,f.decimal_count))
        return safe_identifier(table_name), fields, cols, rows

    def _export_mapped_sql(self, table_name, mapping, primary_key=None):
        path,_=QFileDialog.getSaveFileName(self,"Export Mapped Migration SQL",f"{safe_identifier(table_name)}_migration.sql","SQL files (*.sql)")
        if not path: return
        try:
            table,fields,cols,rows=self._mapped_payload(table_name,mapping)
            Path(path).write_text(build_create_sql(table,fields,primary_key)+"\n\n"+build_insert_sql(table,cols,rows)+"\n",encoding='utf-8')
            self.statusBar().showMessage(f"Mapped SQL exported: {len(rows):,} records.")
        except Exception as exc: QMessageBox.critical(self,"Mapped migration failed",str(exc))

    def _export_mapped_sqlite(self, table_name, mapping, primary_key=None):
        path,_=QFileDialog.getSaveFileName(self,"Create Mapped SQLite Database",f"{safe_identifier(table_name)}.sqlite","SQLite database (*.sqlite *.db)")
        if not path: return
        try:
            table,fields,cols,rows=self._mapped_payload(table_name,mapping)
            count=export_sqlite(path,table,fields,cols,rows,primary_key); self.statusBar().showMessage(f"Mapped SQLite database created: {count:,} records.")
        except Exception as exc: QMessageBox.critical(self,"Mapped SQLite migration failed",str(exc))

    def _export_migration_sql(self, table_name):
        if not self.current_info or not self.rows:
            return
        path, _ = QFileDialog.getSaveFileName(self, "Export Migration SQL", f"{safe_identifier(table_name)}_migration.sql", "SQL files (*.sql)")
        if not path:
            return
        try:
            sql = build_create_sql(table_name, self.current_info.fields) + "\n\n" + build_insert_sql(table_name, self.columns, self.rows) + "\n"
            Path(path).write_text(sql, encoding="utf-8")
            self.statusBar().showMessage(f"Migration SQL exported: {len(self.rows):,} records.")
        except Exception as exc:
            QMessageBox.critical(self, "Migration export failed", str(exc))

    def _export_migration_sqlite(self, table_name):
        if not self.current_info or not self.rows:
            return
        path, _ = QFileDialog.getSaveFileName(self, "Create SQLite Database", f"{safe_identifier(table_name)}.sqlite", "SQLite database (*.sqlite *.db)")
        if not path:
            return
        try:
            count = export_sqlite(path, table_name, self.current_info.fields, self.columns, self.rows)
            self.statusBar().showMessage(f"SQLite migration created: {count:,} records.")
        except Exception as exc:
            QMessageBox.critical(self, "SQLite migration failed", str(exc))

    def clear_dbf(self):
        self.current_dbf = None
        self.current_info = None
        self.current_path = ""
        self.columns = []
        self.rows = []
        self.model.clear()
        self.schema_tree.clear()
        self.column_combo.clear()
        self.column_combo.addItem("All fields", -1)
        self.info_file.setText("—")
        self.info_records.setText("—")
        self.info_fields.setText("—")
        self.info_encoding.setText("—")
        self.file_label.setText("No DBF loaded")
        self.export_btn.setEnabled(False)
        self.profile_btn.setEnabled(False)
        self.compat_btn.setEnabled(False)
        self.migrate_btn.setEnabled(False)
        self.profile_text.clear()
        self.search.clear()
        self.statusBar().showMessage("Ready — open a DBF file to begin.")

    def about(self):
        QMessageBox.about(
            self, "About JASS DBF Explorer",
            f"<h2>{APP_NAME}</h2>"
            f"<p><b>{VERSION}</b></p>"
            "<p>Modern desktop inspection tools for DBF, dBase and FoxPro-era data.</p>"
            "<p><b>Community Edition:</b> read-only inspection, search, filtering, schema review and CSV export.</p>"
            "<p>Part of JASS Digital Lab — Legacy & Data Modernization.</p>"
        )


def apply_style(app):
    app.setStyle("Fusion")
    app.setStyleSheet("""
        QWidget { font-size: 13px; }
        QMainWindow { background: #f5f5f2; }
        QLabel#Title { font-size: 27px; font-weight: 800; color: #151515; }
        QLabel#Subtitle { color: #777; font-size: 13px; }
        QLabel#FileLabel { color: #555; padding: 8px 12px; border: 1px solid #d7d7d2; border-radius: 8px; background: #fff; }
        QLabel#Muted { color: #777; padding: 4px; }
        QLineEdit { background: white; border: 1px solid #cfcfc9; border-radius: 8px; padding: 9px; }
        QComboBox { background: white; border: 1px solid #cfcfc9; border-radius: 8px; padding: 8px; min-width: 130px; }
        QPushButton { border: 1px solid #cfcfc9; background: white; border-radius: 8px; padding: 9px 15px; }
        QPushButton:hover { background: #eeeeea; }
        QPushButton#PrimaryButton { background: #151515; color: white; border-color: #151515; font-weight: 700; }
        QGroupBox { font-weight: 700; border: 1px solid #d8d8d3; border-radius: 10px; margin-top: 9px; padding-top: 12px; background: #fafaf8; }
        QGroupBox::title { subcontrol-origin: margin; left: 12px; padding: 0 5px; color: #555; }
        QTableView { background: white; border: 1px solid #d8d8d3; border-radius: 8px; gridline-color: #ededE8; }
        QTableView::item { padding: 4px; }
        QTableView::item:selected { background: #deded8; color: #111; }
        QHeaderView::section { background: #efefea; border: none; border-right: 1px solid #ddd; border-bottom: 1px solid #ddd; padding: 7px; font-weight: 700; }
        QTreeWidget { background: white; border: 1px solid #d8d8d3; border-radius: 8px; }
        QStatusBar { color: #666; }
        QToolBar { background: #f5f5f2; border: none; spacing: 5px; padding: 3px; }
    """)


def main():
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setOrganizationName("JASS Digital Lab")
    apply_style(app)
    win = MainWindow()
    win.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
