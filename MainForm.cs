using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DbfDataReader;

namespace JASS.DBF.Explorer;

public sealed class MainForm : Form
{
    private readonly MenuStrip menu;
    private readonly ToolStrip tool;
    private readonly DataGridView grid;
    private readonly TreeView schemaTree;
    private readonly TextBox searchBox;
    private readonly ComboBox filterColumn;
    private readonly ComboBox filterOperator;
    private readonly TextBox filterValue;
    private readonly Label statusLabel;
    private readonly Label fileLabel;
    private readonly TabControl tabs;
    private readonly DataGridView structureGrid;
    private readonly DataGridView statsGrid;
    private readonly TextBox headerBox;

    private string? currentFile;
    private DataTable? currentData;
    private DbfHeaderInfo? currentHeader;

    public MainForm()
    {
        Text = "JASS DBF Explorer v1.1";
        Width = 1380;
        Height = 820;
        MinimumSize = new Size(1050, 650);
        StartPosition = FormStartPosition.CenterScreen;

        menu = BuildMenu();
        tool = BuildToolbar();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 285,
            FixedPanel = FixedPanel.Panel1
        };

        schemaTree = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false
        };

        var leftGroup = new GroupBox
        {
            Text = "DBF Structure",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        leftGroup.Controls.Add(schemaTree);
        split.Panel1.Controls.Add(leftGroup);

        var right = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 122,
            FixedPanel = FixedPanel.Panel1
        };

        var info = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        fileLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 27,
            Text = "No DBF file opened",
            AutoEllipsis = true
        };

        var filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true
        };

        filterPanel.Controls.Add(new Label
        {
            Text = "Search:",
            AutoSize = true,
            Margin = new Padding(0, 7, 5, 0)
        });

        searchBox = new TextBox
        {
            Width = 220,
            PlaceholderText = "Search all columns..."
        };
        searchBox.TextChanged += (_, _) => ApplyFilter();
        filterPanel.Controls.Add(searchBox);

        filterPanel.Controls.Add(new Label
        {
            Text = "Column:",
            AutoSize = true,
            Margin = new Padding(12, 7, 5, 0)
        });

        filterColumn = new ComboBox
        {
            Width = 150,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        filterColumn.SelectedIndexChanged += (_, _) => ApplyFilter();
        filterPanel.Controls.Add(filterColumn);

        filterOperator = new ComboBox
        {
            Width = 125,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        filterOperator.Items.AddRange(new object[]
        {
            "Contains", "Starts with", "Equals", "Not equals",
            "Greater than", "Less than", "Greater/equal", "Less/equal"
        });
        filterOperator.SelectedIndex = 0;
        filterOperator.SelectedIndexChanged += (_, _) => ApplyFilter();
        filterPanel.Controls.Add(filterOperator);

        filterValue = new TextBox
        {
            Width = 170,
            PlaceholderText = "Filter value..."
        };
        filterValue.TextChanged += (_, _) => ApplyFilter();
        filterPanel.Controls.Add(filterValue);

        var clear = new Button { Text = "Clear Filters", AutoSize = true };
        clear.Click += (_, _) =>
        {
            searchBox.Clear();
            filterValue.Clear();
            if (filterColumn.Items.Count > 0)
                filterColumn.SelectedIndex = 0;
            filterOperator.SelectedIndex = 0;
        };
        filterPanel.Controls.Add(clear);

        info.Controls.Add(filterPanel);
        info.Controls.Add(fileLabel);

        tabs = new TabControl { Dock = DockStyle.Fill };

        var dataPage = new TabPage("Records");
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
        };
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (currentData == null || e.ColumnIndex < 0 || e.ColumnIndex >= currentData.Columns.Count)
                return;

            var column = currentData.Columns[e.ColumnIndex].ColumnName;
            var view = currentData.DefaultView;
            var current = view.Sort;

            var ascending = !current.Equals($"[{column}] ASC", StringComparison.OrdinalIgnoreCase);
            view.Sort = $"[{column}] {(ascending ? "ASC" : "DESC")}";
            grid.DataSource = view;
            statusLabel.Text = $"Sorted by {column} {(ascending ? "ascending" : "descending")}.";
        };
        dataPage.Controls.Add(grid);

        var structurePage = new TabPage("Field Structure");
        structureGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        structurePage.Controls.Add(structureGrid);

        var statsPage = new TabPage("Statistics");
        statsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        statsPage.Controls.Add(statsGrid);

        var headerPage = new TabPage("DBF Header");
        headerBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10F),
            BackColor = SystemColors.Window
        };
        headerPage.Controls.Add(headerBox);

        tabs.TabPages.Add(dataPage);
        tabs.TabPages.Add(structurePage);
        tabs.TabPages.Add(statsPage);
        tabs.TabPages.Add(headerPage);

        right.Panel1.Controls.Add(info);
        right.Panel2.Controls.Add(tabs);
        split.Panel2.Controls.Add(right);

        statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 25,
            Text = "Ready",
            BorderStyle = BorderStyle.Fixed3D,
            Padding = new Padding(6, 3, 0, 0)
        };

        Controls.Add(split);
        Controls.Add(statusLabel);
        Controls.Add(tool);
        Controls.Add(menu);
        MainMenuStrip = menu;

        ApplyPalette();
    }

    private MenuStrip BuildMenu()
    {
        var m = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("Open DBF...", null, (_, _) => OpenDbf());
        file.DropDownItems.Add("Export CSV...", null, (_, _) => ExportCsv());
        file.DropDownItems.Add("Export JSON...", null, (_, _) => ExportJson());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add("Refresh", null, (_, _) => Reload());
        view.DropDownItems.Add("Clear Filters", null, (_, _) =>
        {
            searchBox.Clear();
            filterValue.Clear();
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("About", null, (_, _) =>
            MessageBox.Show(
                "JASS DBF Explorer v1.1\n\nA DBF investigation and analysis workspace for legacy data.",
                "About JASS DBF Explorer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information));

        m.Items.Add(file);
        m.Items.Add(view);
        m.Items.Add(help);
        return m;
    }

    private ToolStrip BuildToolbar()
    {
        var t = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

        var open = new ToolStripButton("Open DBF");
        open.Click += (_, _) => OpenDbf();

        var refresh = new ToolStripButton("Refresh");
        refresh.Click += (_, _) => Reload();

        var csv = new ToolStripButton("Export CSV");
        csv.Click += (_, _) => ExportCsv();

        var json = new ToolStripButton("Export JSON");
        json.Click += (_, _) => ExportJson();

        t.Items.Add(open);
        t.Items.Add(refresh);
        t.Items.Add(new ToolStripSeparator());
        t.Items.Add(csv);
        t.Items.Add(json);
        return t;
    }

    private void OpenDbf()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open DBF File",
            Filter = "DBF files (*.dbf)|*.dbf|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
            LoadDbf(dlg.FileName);
    }

    private void LoadDbf(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            statusLabel.Text = "Reading DBF...";
            schemaTree.Nodes.Clear();

            currentHeader = DbfHeaderInfo.Read(path);
            var data = new DataTable(Path.GetFileNameWithoutExtension(path));

            using var reader = new global::DbfDataReader.DbfDataReader(
                path,
                new global::DbfDataReader.DbfDataReaderOptions
                {
                    SkipDeletedRecords = true
                });

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var type = reader.GetFieldType(i);
                var safeType = Nullable.GetUnderlyingType(type) ?? type;
                data.Columns.Add(reader.GetName(i), safeType);
            }

            while (reader.Read())
            {
                var row = data.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                data.Rows.Add(row);
            }

            currentFile = path;
            currentData = data;
            grid.DataSource = data;

            filterColumn.Items.Clear();
            filterColumn.Items.Add("(All columns)");
            foreach (DataColumn c in data.Columns)
                filterColumn.Items.Add(c.ColumnName);
            filterColumn.SelectedIndex = 0;

            BuildSchemaTree(data, path);
            BuildStructureGrid(path);
            BuildStatistics(data);
            BuildHeaderView(currentHeader);

            fileLabel.Text =
                $"{Path.GetFileName(path)}   |   {data.Rows.Count:N0} visible records   |   {data.Columns.Count} fields";

            statusLabel.Text =
                $"Loaded {data.Rows.Count:N0} records. Header reports {currentHeader.RecordCount:N0} records.";

            searchBox.Clear();
            filterValue.Clear();
            tabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Load failed.";
            MessageBox.Show(
                this,
                $"Could not read the DBF file.\n\n{ex.Message}",
                "DBF Read Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void BuildSchemaTree(DataTable data, string path)
    {
        var root = new TreeNode(Path.GetFileName(path));
        var fields = new TreeNode($"Fields ({data.Columns.Count})");

        foreach (DataColumn c in data.Columns)
            fields.Nodes.Add($"{c.ColumnName}  [{c.DataType.Name}]");

        root.Nodes.Add(fields);
        root.Nodes.Add($"Visible records: {data.Rows.Count:N0}");
        if (currentHeader != null)
            root.Nodes.Add($"Header records: {currentHeader.RecordCount:N0}");
        root.Nodes.Add($"File size: {new FileInfo(path).Length:N0} bytes");
        root.ExpandAll();
        schemaTree.Nodes.Add(root);
    }

    private void BuildStructureGrid(string path)
    {
        var structure = new DataTable();
        structure.Columns.Add("Field");
        structure.Columns.Add("DBF Type");
        structure.Columns.Add("Length");
        structure.Columns.Add("Decimals");
        structure.Columns.Add("CLR Type");
        structure.Columns.Add("Ordinal");

        using var table = new global::DbfDataReader.DbfTable(path);

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];
            structure.Rows.Add(
                c.ColumnName,
                c.ColumnType.ToString(),
                c.Length,
                c.DecimalCount,
                currentData?.Columns[i].DataType.Name ?? "",
                i);
        }

        structureGrid.DataSource = structure;
    }

    private void BuildStatistics(DataTable data)
    {
        var stats = new DataTable();
        stats.Columns.Add("Field");
        stats.Columns.Add("Type");
        stats.Columns.Add("Rows");
        stats.Columns.Add("Empty / Null");
        stats.Columns.Add("Non-empty");
        stats.Columns.Add("Unique");
        stats.Columns.Add("Example");

        foreach (DataColumn column in data.Columns)
        {
            var values = data.Rows.Cast<DataRow>()
                .Select(r => r[column])
                .ToList();

            var nonEmpty = values.Where(v =>
                v != DBNull.Value &&
                !string.IsNullOrWhiteSpace(Convert.ToString(v, CultureInfo.InvariantCulture)))
                .ToList();

            var unique = nonEmpty
                .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")
                .Distinct(StringComparer.Ordinal)
                .Count();

            var example = nonEmpty.Count > 0
                ? Convert.ToString(nonEmpty[0], CultureInfo.InvariantCulture) ?? ""
                : "";

            stats.Rows.Add(
                column.ColumnName,
                column.DataType.Name,
                values.Count,
                values.Count - nonEmpty.Count,
                nonEmpty.Count,
                unique,
                example);
        }

        statsGrid.DataSource = stats;
    }

    private void BuildHeaderView(DbfHeaderInfo h)
    {
        var sb = new StringBuilder();

        sb.AppendLine("JASS DBF EXPLORER — DBF HEADER");
        sb.AppendLine(new string('=', 48));
        sb.AppendLine($"File:              {Path.GetFileName(h.FilePath)}");
        sb.AppendLine($"DBF version byte:  0x{h.Version:X2}");
        sb.AppendLine($"Version meaning:   {h.VersionDescription}");
        sb.AppendLine($"Last update:       {h.LastUpdate:yyyy-MM-dd}");
        sb.AppendLine($"Record count:      {h.RecordCount:N0}");
        sb.AppendLine($"Header length:     {h.HeaderLength:N0} bytes");
        sb.AppendLine($"Record length:     {h.RecordLength:N0} bytes");
        sb.AppendLine($"File size:         {h.FileSize:N0} bytes");
        sb.AppendLine($"Field count:       {h.FieldCount:N0}");
        sb.AppendLine($"Code page byte:    0x{h.CodePageByte:X2}");
        sb.AppendLine($"Code page:         {h.CodePageDescription}");
        sb.AppendLine($"Has memo flag:     {h.HasMemoFlag}");
        sb.AppendLine();
        sb.AppendLine("Note: Header information is read directly from the DBF header.");
        sb.AppendLine("Visible record count can differ when deleted records are skipped.");

        headerBox.Text = sb.ToString();
    }

    private void ApplyFilter()
    {
        if (currentData == null)
            return;

        var globalTerm = searchBox.Text.Trim();
        var columnName = filterColumn.SelectedIndex <= 0
            ? null
            : filterColumn.SelectedItem?.ToString();

        var filterText = filterValue.Text.Trim();
        var expressions = new List<string>();

        if (!string.IsNullOrEmpty(globalTerm))
        {
            var escaped = EscapeFilter(globalTerm);
            expressions.Add(string.Join(" OR ",
                currentData.Columns.Cast<DataColumn>()
                    .Select(c => $"CONVERT([{c.ColumnName}], 'System.String') LIKE '%{escaped}%'")));
        }

        if (!string.IsNullOrEmpty(filterText) && columnName != null)
        {
            var c = currentData.Columns[columnName];
            var escaped = EscapeFilter(filterText);
            var op = filterOperator.SelectedItem?.ToString() ?? "Contains";

            if (IsNumericType(c.DataType) && op is not "Contains" and not "Starts with")
            {
                if (decimal.TryParse(filterText, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                {
                    expressions.Add(BuildNumericExpression(columnName, op, filterText));
                }
            }
            else
            {
                expressions.Add(BuildTextExpression(columnName, op, escaped));
            }
        }

        try
        {
            var view = currentData.DefaultView;
            view.RowFilter = expressions.Count == 0
                ? ""
                : string.Join(" AND ", expressions);

            grid.DataSource = view;

            statusLabel.Text =
                $"Showing {view.Count:N0} of {currentData.Rows.Count:N0} records.";
        }
        catch
        {
            currentData.DefaultView.RowFilter = "";
            grid.DataSource = currentData;
            statusLabel.Text = "Filter could not be applied.";
        }
    }

    private static string BuildTextExpression(string column, string op, string value)
    {
        var c = $"CONVERT([{column}], 'System.String')";

        return op switch
        {
            "Starts with" => $"{c} LIKE '{value}%'",
            "Equals" => $"{c} = '{value}'",
            "Not equals" => $"{c} <> '{value}'",
            _ => $"{c} LIKE '%{value}%'"
        };
    }

    private static string BuildNumericExpression(string column, string op, string value)
    {
        var safe = value.Replace(",", "");
        var c = $"CONVERT([{column}], 'System.Decimal')";

        return op switch
        {
            "Equals" => $"{c} = {safe}",
            "Not equals" => $"{c} <> {safe}",
            "Greater than" => $"{c} > {safe}",
            "Less than" => $"{c} < {safe}",
            "Greater/equal" => $"{c} >= {safe}",
            "Less/equal" => $"{c} <= {safe}",
            _ => $"{c} = {safe}"
        };
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) ||
               type == typeof(short) ||
               type == typeof(int) ||
               type == typeof(long) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);
    }

    private static string EscapeFilter(string value)
        => value.Replace("'", "''");

    private void Reload()
    {
        if (!string.IsNullOrWhiteSpace(currentFile))
            LoadDbf(currentFile);
    }

    private void ExportCsv()
    {
        var view = GetCurrentView();

        if (view == null)
        {
            MessageBox.Show(this, "Open a DBF file first.", "Export CSV",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export CSV",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = Path.GetFileNameWithoutExtension(currentFile ?? "data") + ".csv"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using var writer = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));

            writer.WriteLine(string.Join(",",
                view.Table.Columns.Cast<DataColumn>()
                    .Select(c => Csv(c.ColumnName))));

            foreach (DataRowView rowView in view)
                writer.WriteLine(string.Join(",",
                    view.Table.Columns.Cast<DataColumn>()
                        .Select(c => Csv(rowView.Row[c]?.ToString() ?? ""))));

            statusLabel.Text =
                $"CSV exported: {Path.GetFileName(dlg.FileName)} ({view.Count:N0} records)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "CSV Export Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportJson()
    {
        var view = GetCurrentView();

        if (view == null)
        {
            MessageBox.Show(this, "Open a DBF file first.", "Export JSON",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export JSON",
            Filter = "JSON files (*.json)|*.json",
            FileName = Path.GetFileNameWithoutExtension(currentFile ?? "data") + ".json"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var records = new List<Dictionary<string, object?>>();

            foreach (DataRowView rv in view)
            {
                var record = new Dictionary<string, object?>();

                foreach (DataColumn c in view.Table.Columns)
                    record[c.ColumnName] =
                        rv.Row[c] == DBNull.Value ? null : rv.Row[c];

                records.Add(record);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };

            File.WriteAllText(
                dlg.FileName,
                JsonSerializer.Serialize(records, options),
                Encoding.UTF8);

            statusLabel.Text =
                $"JSON exported: {Path.GetFileName(dlg.FileName)} ({view.Count:N0} records)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "JSON Export Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private DataView? GetCurrentView()
    {
        return grid.DataSource switch
        {
            DataTable dt => dt.DefaultView,
            DataView dv => dv,
            _ => currentData?.DefaultView
        };
    }

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') ||
            value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    private void ApplyPalette()
    {
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        grid.BackgroundColor = SystemColors.Window;
        grid.ForeColor = SystemColors.WindowText;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        schemaTree.BackColor = SystemColors.Window;
        schemaTree.ForeColor = SystemColors.WindowText;
    }

    private sealed class DbfHeaderInfo
    {
        public string FilePath { get; init; } = "";
        public byte Version { get; init; }
        public string VersionDescription { get; init; } = "";
        public DateTime LastUpdate { get; init; }
        public int RecordCount { get; init; }
        public int HeaderLength { get; init; }
        public int RecordLength { get; init; }
        public long FileSize { get; init; }
        public int FieldCount { get; init; }
        public byte CodePageByte { get; init; }
        public string CodePageDescription { get; init; } = "";
        public bool HasMemoFlag { get; init; }

        public static DbfHeaderInfo Read(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);

            if (fs.Length < 32)
                throw new InvalidDataException("The file is too small to contain a DBF header.");

            var version = br.ReadByte();
            var year = 1900 + br.ReadByte();
            var month = br.ReadByte();
            var day = br.ReadByte();

            var recordCount = br.ReadInt32();
            var headerLength = br.ReadInt16();
            var recordLength = br.ReadInt16();

            fs.Position = 29;
            var codePage = br.ReadByte();

            int fieldCount = Math.Max(0, (headerLength - 33) / 32);
            bool memo = (version & 0x80) != 0;

            DateTime date;
            try
            {
                date = new DateTime(year, month, day);
            }
            catch
            {
                date = DateTime.MinValue;
            }

            return new DbfHeaderInfo
            {
                FilePath = path,
                Version = version,
                VersionDescription = DescribeVersion(version),
                LastUpdate = date,
                RecordCount = recordCount,
                HeaderLength = headerLength,
                RecordLength = recordLength,
                FileSize = new FileInfo(path).Length,
                FieldCount = fieldCount,
                CodePageByte = codePage,
                CodePageDescription = DescribeCodePage(codePage),
                HasMemoFlag = memo
            };
        }

        private static string DescribeVersion(byte v)
        {
            return v switch
            {
                0x02 => "dBASE II",
                0x03 => "dBASE III",
                0x04 => "dBASE IV",
                0x05 => "dBASE V",
                0x30 => "Visual FoxPro",
                0x31 => "Visual FoxPro",
                0x32 => "Visual FoxPro",
                0x43 => "dBASE IV with memo",
                0x63 => "dBASE III with memo",
                _ => "Unknown / extended DBF variant"
            };
        }

        private static string DescribeCodePage(byte v)
        {
            return v switch
            {
                0x01 => "Code page 437 (US OEM)",
                0x02 => "Code page 850 (International OEM)",
                0x03 => "Code page 1252 (Windows ANSI)",
                0x57 => "ANSI 1252",
                0x64 => "Eastern European",
                0x65 => "Russian",
                0x66 => "Nordic",
                0x67 => "Icelandic",
                0x68 => "Hebrew",
                0x69 => "Windows Greek",
                0x6A => "Windows Turkish",
                0x7A => "Standard Macintosh",
                0x7B => "Macintosh Central European",
                0x7C => "Macintosh Croatian",
                0x7D => "Macintosh Icelandic",
                0x7E => "Macintosh Greek",
                0x7F => "Macintosh Turkish",
                0xC8 => "Windows 1250",
                0xC9 => "Windows 1251",
                0xCA => "Windows 1254",
                0xCB => "Windows 1253",
                _ => v == 0 ? "Not specified" : "Unknown code page"
            };
        }
    }
}
