using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DbfDataReader;
using Microsoft.Data.Sqlite;

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
    private readonly DataGridView qualityGrid;
    private readonly TextBox headerBox;
    private DataGridView migrationGrid = null!;
    private Label migrationSummary = null!;
    private TextBox destinationBox = null!;
    private Button browseDestination = null!;
    private Button migrateButton = null!;
    private Button validateButton = null!;
    private ProgressBar migrationProgress = null!;
    private TextBox migrationLog = null!;

    private string? currentFile;
    private DataTable? currentData;
    private DbfHeaderInfo? currentHeader;
    private int deletedRecordCount;

    public MainForm()
    {
        Text = "JASS DBF Explorer v1.3";
        Width = 1500;
        Height = 900;
        MinimumSize = new Size(1150, 700);
        StartPosition = FormStartPosition.CenterScreen;

        menu = BuildMenu();
        tool = BuildToolbar();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 290,
            FixedPanel = FixedPanel.Panel1
        };

        schemaTree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };

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
            SplitterDistance = 125,
            FixedPanel = FixedPanel.Panel1
        };

        var info = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        fileLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
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
            Width = 155,
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
        grid = MakeGrid();
        grid.ColumnHeaderMouseClick += (_, e) => SortColumn(e.ColumnIndex);
        dataPage.Controls.Add(grid);

        var structurePage = new TabPage("Field Structure");
        structureGrid = MakeGrid();
        structurePage.Controls.Add(structureGrid);

        var qualityPage = new TabPage("Data Quality");
        qualityGrid = MakeGrid();
        qualityPage.Controls.Add(qualityGrid);

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

        var migrationPage = BuildMigrationPage();

        tabs.TabPages.Add(dataPage);
        tabs.TabPages.Add(structurePage);
        tabs.TabPages.Add(qualityPage);
        tabs.TabPages.Add(headerPage);
        tabs.TabPages.Add(migrationPage);

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

    private static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
    };

    private TabPage BuildMigrationPage()
    {
        var page = new TabPage("DBF → SQLite Migration");

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 120,
            ColumnCount = 4,
            RowCount = 3
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        top.Controls.Add(new Label
        {
            Text = "Destination SQLite:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3)
        }, 0, 0);

        destinationBox = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "Choose destination .sqlite file..."
        };
        top.Controls.Add(destinationBox, 1, 0);

        browseDestination = new Button
        {
            Text = "Browse...",
            AutoSize = true
        };
        browseDestination.Click += (_, _) => ChooseDestination();
        top.Controls.Add(browseDestination, 2, 0);

        var schemaButton = new Button
        {
            Text = "Preview Schema",
            AutoSize = true
        };
        schemaButton.Click += (_, _) => BuildMigrationPreview();
        top.Controls.Add(schemaButton, 3, 0);

        migrationSummary = new Label
        {
            Text = "Open a DBF file to prepare a migration.",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Padding = new Padding(3, 8, 3, 3)
        };
        top.Controls.Add(migrationSummary, 0, 1);
        top.SetColumnSpan(migrationSummary, 4);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };

        migrateButton = new Button
        {
            Text = "Migrate DBF → SQLite",
            AutoSize = true,
            Enabled = false
        };
        migrateButton.Click += async (_, _) => await MigrateAsync();
        actions.Controls.Add(migrateButton);

        validateButton = new Button
        {
            Text = "Validate Destination",
            AutoSize = true,
            Enabled = false
        };
        validateButton.Click += (_, _) => ValidateDestination();
        actions.Controls.Add(validateButton);

        migrationProgress = new ProgressBar
        {
            Width = 260,
            Height = 24,
            Minimum = 0,
            Maximum = 100
        };
        actions.Controls.Add(migrationProgress);

        top.Controls.Add(actions, 0, 2);
        top.SetColumnSpan(actions, 4);

        migrationGrid = MakeGrid();

        migrationLog = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 110,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F)
        };

        panel.Controls.Add(migrationGrid);
        panel.Controls.Add(migrationLog);
        panel.Controls.Add(top);

        page.Controls.Add(panel);
        return page;
    }

    private MenuStrip BuildMenu()
    {
        var m = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("Open DBF...", null, (_, _) => OpenDbf());
        file.DropDownItems.Add("Export CSV...", null, (_, _) => ExportCsv());
        file.DropDownItems.Add("Export JSON...", null, (_, _) => ExportJson());
        file.DropDownItems.Add("Export SQLite Schema...", null, (_, _) => ExportSqliteSchema());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add("Refresh", null, (_, _) => Reload());
        view.DropDownItems.Add("Clear Filters", null, (_, _) =>
        {
            searchBox.Clear();
            filterValue.Clear();
        });

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add("Rebuild Analysis", null, (_, _) => RebuildAnalysis());
        tools.DropDownItems.Add("Validate SQLite Destination", null, (_, _) => ValidateDestination());

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("About", null, (_, _) =>
            MessageBox.Show(
                "JASS DBF Explorer v1.3\n\nDBF → SQLite migration workspace with preview and validation.",
                "About JASS DBF Explorer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information));

        m.Items.Add(file);
        m.Items.Add(view);
        m.Items.Add(tools);
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

        var sql = new ToolStripButton("SQLite Schema");
        sql.Click += (_, _) => ExportSqliteSchema();

        var migrate = new ToolStripButton("Migrate to SQLite");
        migrate.Click += async (_, _) => await MigrateAsync();

        t.Items.Add(open);
        t.Items.Add(refresh);
        t.Items.Add(new ToolStripSeparator());
        t.Items.Add(csv);
        t.Items.Add(json);
        t.Items.Add(sql);
        t.Items.Add(new ToolStripSeparator());
        t.Items.Add(migrate);
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
            statusLabel.Text = "Reading DBF and analysing structure...";
            schemaTree.Nodes.Clear();

            currentHeader = DbfHeaderInfo.Read(path);
            deletedRecordCount = DbfAnalyzer.CountDeletedRecords(path, currentHeader);

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

            RebuildAnalysis();

            fileLabel.Text =
                $"{Path.GetFileName(path)}   |   {data.Rows.Count:N0} visible   |   {data.Columns.Count} fields   |   {deletedRecordCount:N0} deleted";

            statusLabel.Text =
                $"Loaded {data.Rows.Count:N0} visible records; detected {deletedRecordCount:N0} deleted records.";

            searchBox.Clear();
            filterValue.Clear();
            tabs.SelectedIndex = 0;

            PrepareMigrationState();
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

    private void RebuildAnalysis()
    {
        if (currentData == null || currentFile == null || currentHeader == null)
            return;

        BuildSchemaTree(currentData, currentFile);
        BuildStructureGrid(currentFile);
        BuildQualityGrid(currentData);
        BuildHeaderView(currentHeader);
        BuildMigrationPreview();
    }

    private void BuildSchemaTree(DataTable data, string path)
    {
        var root = new TreeNode(Path.GetFileName(path));
        var fields = new TreeNode($"Fields ({data.Columns.Count})");

        foreach (DataColumn c in data.Columns)
            fields.Nodes.Add($"{c.ColumnName}  [{c.DataType.Name}]");

        root.Nodes.Add(fields);
        root.Nodes.Add($"Visible records: {data.Rows.Count:N0}");
        root.Nodes.Add($"Header records: {currentHeader?.RecordCount:N0}");
        root.Nodes.Add($"Deleted records: {deletedRecordCount:N0}");
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

    private void BuildQualityGrid(DataTable data)
    {
        var quality = new DataTable();
        quality.Columns.Add("Field");
        quality.Columns.Add("Type");
        quality.Columns.Add("Rows");
        quality.Columns.Add("Empty / Null");
        quality.Columns.Add("Non-empty");
        quality.Columns.Add("Unique");
        quality.Columns.Add("Min / Earliest");
        quality.Columns.Add("Max / Latest");
        quality.Columns.Add("Example");
        quality.Columns.Add("Quality Notes");

        foreach (DataColumn column in data.Columns)
        {
            var values = data.Rows.Cast<DataRow>()
                .Select(r => r[column])
                .Where(v => v != DBNull.Value)
                .ToList();

            var nonEmpty = values
                .Where(v => !string.IsNullOrWhiteSpace(
                    Convert.ToString(v, CultureInfo.InvariantCulture)))
                .ToList();

            var textValues = nonEmpty
                .Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")
                .ToList();

            var unique = textValues
                .Distinct(StringComparer.Ordinal)
                .Count();

            var example = textValues.Count > 0 ? textValues[0] : "";
            string min = "";
            string max = "";
            string notes = "";

            if (IsNumericType(column.DataType))
            {
                var nums = nonEmpty
                    .Select(v => TryDecimal(v, out var d) ? (decimal?)d : null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (nums.Count > 0)
                {
                    min = nums.Min().ToString(CultureInfo.InvariantCulture);
                    max = nums.Max().ToString(CultureInfo.InvariantCulture);
                }

                if (nums.Count != nonEmpty.Count)
                    notes = "Some non-empty values are not numeric.";
            }
            else if (column.DataType == typeof(DateTime))
            {
                var dates = nonEmpty.OfType<DateTime>().ToList();
                if (dates.Count > 0)
                {
                    min = dates.Min().ToString("yyyy-MM-dd");
                    max = dates.Max().ToString("yyyy-MM-dd");
                }
            }
            else
            {
                var lengths = textValues.Select(v => v.Length).ToList();
                if (lengths.Count > 0)
                {
                    min = lengths.Min().ToString(CultureInfo.InvariantCulture) + " chars";
                    max = lengths.Max().ToString(CultureInfo.InvariantCulture) + " chars";
                }
            }

            if (nonEmpty.Count == 0)
                notes = "Column contains no non-empty values.";
            else if (unique == 1)
                notes = string.IsNullOrEmpty(notes)
                    ? "Single distinct value."
                    : notes + " Single distinct value.";

            quality.Rows.Add(
                column.ColumnName,
                column.DataType.Name,
                data.Rows.Count,
                data.Rows.Count - nonEmpty.Count,
                nonEmpty.Count,
                unique,
                min,
                max,
                example,
                notes);
        }

        qualityGrid.DataSource = quality;
    }

    private void BuildHeaderView(DbfHeaderInfo h)
    {
        var sb = new StringBuilder();

        sb.AppendLine("JASS DBF EXPLORER — DBF HEADER");
        sb.AppendLine(new string('=', 52));
        sb.AppendLine($"File:              {Path.GetFileName(h.FilePath)}");
        sb.AppendLine($"DBF version byte:  0x{h.Version:X2}");
        sb.AppendLine($"Version meaning:   {h.VersionDescription}");
        sb.AppendLine($"Last update:       {h.LastUpdate:yyyy-MM-dd}");
        sb.AppendLine($"Header records:    {h.RecordCount:N0}");
        sb.AppendLine($"Visible records:   {currentData?.Rows.Count:N0}");
        sb.AppendLine($"Deleted records:   {deletedRecordCount:N0}");
        sb.AppendLine($"Header length:     {h.HeaderLength:N0} bytes");
        sb.AppendLine($"Record length:     {h.RecordLength:N0} bytes");
        sb.AppendLine($"File size:         {h.FileSize:N0} bytes");
        sb.AppendLine($"Field count:       {h.FieldCount:N0}");
        sb.AppendLine($"Code page byte:    0x{h.CodePageByte:X2}");
        sb.AppendLine($"Code page:         {h.CodePageDescription}");
        sb.AppendLine($"Memo flag:         {h.HasMemoFlag}");
        sb.AppendLine();
        sb.AppendLine("The original DBF is never modified by migration.");
        headerBox.Text = sb.ToString();
    }

    private void BuildMigrationPreview()
    {
        if (currentData == null || currentFile == null)
            return;

        var preview = new DataTable();
        preview.Columns.Add("DBF Field");
        preview.Columns.Add("DBF Type");
        preview.Columns.Add("Length");
        preview.Columns.Add("Decimals");
        preview.Columns.Add("SQLite Type");
        preview.Columns.Add("SQLite Column");
        preview.Columns.Add("Migration Note");

        using var table = new global::DbfDataReader.DbfTable(currentFile);

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];
            var sqlite = MapSqliteType(
                c.ColumnType.ToString(),
                currentData!.Columns[i].DataType);

            preview.Rows.Add(
                c.ColumnName,
                c.ColumnType.ToString(),
                c.Length,
                c.DecimalCount,
                sqlite,
                SanitizeSqlIdentifier(c.ColumnName),
                BuildMigrationNote(
                    c.ColumnType.ToString(),
                    currentData!.Columns[i].DataType));
        }

        migrationGrid.DataSource = preview;

        var tableName = SanitizeSqlIdentifier(
            Path.GetFileNameWithoutExtension(currentFile));

        migrationSummary.Text =
            $"Migration preview only. Destination: {GetDestinationDisplay()}   |   Table: [{tableName}]   |   {preview.Rows.Count} columns   |   {currentData!.Rows.Count:N0} visible records.";
    }

    private void PrepareMigrationState()
    {
        var suggested = Path.Combine(
            Path.GetDirectoryName(currentFile!) ?? "",
            Path.GetFileNameWithoutExtension(currentFile!) + ".sqlite");

        destinationBox.Text = suggested;
        migrateButton.Enabled = currentData != null;
        validateButton.Enabled = File.Exists(suggested);
    }

    private string GetDestinationDisplay()
        => string.IsNullOrWhiteSpace(destinationBox.Text)
            ? "(not selected)"
            : destinationBox.Text;

    private void ChooseDestination()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Choose SQLite Destination",
            Filter = "SQLite database (*.sqlite;*.db)|*.sqlite;*.db|All files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(
                currentFile ?? "migrated") + ".sqlite"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        destinationBox.Text = dlg.FileName;
        validateButton.Enabled = File.Exists(dlg.FileName);
        BuildMigrationPreview();
    }

    private async Task MigrateAsync()
    {
        if (currentData == null || currentFile == null)
            return;

        var destination = destinationBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(destination))
        {
            ChooseDestination();
            destination = destinationBox.Text.Trim();
        }

        if (string.IsNullOrWhiteSpace(destination))
            return;

        if (string.Equals(
            Path.GetFullPath(destination),
            Path.GetFullPath(currentFile),
            StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                this,
                "The SQLite destination cannot be the source DBF file.",
                "Migration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (File.Exists(destination))
        {
            var result = MessageBox.Show(
                this,
                $"The destination already exists:\n\n{destination}\n\nIt will be replaced with a fresh SQLite database. Continue?",
                "Replace SQLite Destination?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
                return;
        }

        try
        {
            SetMigrationBusy(true);
            migrationLog.Clear();
            migrationProgress.Value = 0;
            Log($"Source: {currentFile}");
            Log($"Destination: {destination}");
            Log($"Source records: {currentData!.Rows.Count:N0}");
            Log($"Fields: {currentData!.Columns.Count}");
            Log($"Deleted records excluded: {deletedRecordCount:N0}");
            Log("");

            await Task.Run(() => PerformMigration(destination));

            migrationProgress.Value = 100;
            Log("");
            Log("Migration completed successfully.");
            statusLabel.Text = "DBF → SQLite migration completed.";
            validateButton.Enabled = true;

            ValidateDestination();
        }
        catch (Exception ex)
        {
            Log("");
            Log("MIGRATION FAILED:");
            Log(ex.ToString());
            statusLabel.Text = "Migration failed.";

            MessageBox.Show(
                this,
                ex.Message,
                "Migration Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetMigrationBusy(false);
        }
    }

    private void PerformMigration(string destination)
    {
        var tableName = SanitizeSqlIdentifier(
            Path.GetFileNameWithoutExtension(currentFile!));

        if (File.Exists(destination))
            File.Delete(destination);

        using var connection = new SqliteConnection(
            $"Data Source={destination}");

        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var columns = GetMigrationColumns();

            using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                var defs = columns.Select(c =>
                    $"[{c.SqlName}] {c.SqliteType}");

                create.CommandText =
                    $"CREATE TABLE [{tableName}] ({string.Join(", ", defs)});";

                create.ExecuteNonQuery();
            }

            var placeholders = string.Join(
                ", ",
                columns.Select((_, i) => $"$p{i}"));

            var names = string.Join(
                ", ",
                columns.Select(c => $"[{c.SqlName}]"));

            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"INSERT INTO [{tableName}] ({names}) VALUES ({placeholders});";

            foreach (var c in columns)
                insert.Parameters.AddWithValue($"$p{c.Ordinal}", DBNull.Value);

            var total = currentData!.Rows.Count;
            var done = 0;

            foreach (DataRow row in currentData.Rows)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    var c = columns[i];
                    insert.Parameters[$"$p{i}"].Value =
                        ToSqliteValue(row[c.SourceName], c.SqliteType);
                }

                insert.ExecuteNonQuery();
                done++;

                if (done == total || done % Math.Max(1, total / 100) == 0)
                {
                    var pct = total == 0 ? 100 : done * 100 / total;
                    Invoke(() =>
                    {
                        migrationProgress.Value =
                            Math.Min(100, Math.Max(0, pct));
                        statusLabel.Text =
                            $"Migrating {done:N0} / {total:N0} records...";
                    });
                }
            }

            transaction.Commit();

            Log($"Created table: [{tableName}]");
            Log($"Migrated records: {done:N0}");
            Log($"Created columns: {columns.Count}");
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            try { connection.Close(); } catch { }
            try { File.Delete(destination); } catch { }
            throw;
        }
    }

    private void ValidateDestination()
    {
        var destination = destinationBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(destination) ||
            !File.Exists(destination))
        {
            MessageBox.Show(
                this,
                "Choose or create a SQLite destination first.",
                "Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            using var connection = new SqliteConnection(
                $"Data Source={destination}");
            connection.Open();

            var tableName = SanitizeSqlIdentifier(
                Path.GetFileNameWithoutExtension(currentFile ?? "data"));

            using var count = connection.CreateCommand();
            count.CommandText =
                $"SELECT COUNT(*) FROM [{tableName}];";

            var destinationRows =
                Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);

            var sourceRows = currentData?.Rows.Count ?? 0;

            Log("");
            Log("VALIDATION");
            Log($"Source visible records:      {sourceRows:N0}");
            Log($"Destination records:         {destinationRows:N0}");
            Log($"Source deleted records:      {deletedRecordCount:N0}");

            if (sourceRows == destinationRows)
            {
                Log("RESULT: RECORD COUNTS MATCH.");
                statusLabel.Text =
                    "Validation passed: source and destination record counts match.";
            }
            else
            {
                Log("RESULT: RECORD COUNTS DO NOT MATCH.");
                statusLabel.Text =
                    "Validation warning: record counts differ.";
            }

            MessageBox.Show(
                this,
                $"Source records: {sourceRows:N0}\n" +
                $"SQLite records: {destinationRows:N0}\n\n" +
                (sourceRows == destinationRows
                    ? "Validation passed."
                    : "Validation warning: counts differ."),
                "SQLite Validation",
                MessageBoxButtons.OK,
                sourceRows == destinationRows
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Log("VALIDATION ERROR: " + ex.Message);
            MessageBox.Show(
                this,
                ex.Message,
                "Validation Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private List<MigrationColumn> GetMigrationColumns()
    {
        var list = new List<MigrationColumn>();

        using var table = new global::DbfDataReader.DbfTable(currentFile!);

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var c = table.Columns[i];

            list.Add(new MigrationColumn
            {
                Ordinal = i,
                SourceName = c.ColumnName,
                SqlName = SanitizeSqlIdentifier(c.ColumnName),
                SqliteType = MapSqliteType(
                    c.ColumnType.ToString(),
                    currentData!.Columns[i].DataType)
            });
        }

        return list;
    }

    private static object ToSqliteValue(object value, string sqliteType)
    {
        if (value == DBNull.Value || value == null)
            return DBNull.Value;

        if (sqliteType == "INTEGER")
        {
            if (value is bool b)
                return b ? 1 : 0;

            if (value is IConvertible)
            {
                try
                {
                    return Convert.ToInt64(
                        value,
                        CultureInfo.InvariantCulture);
                }
                catch { }
            }
        }

        if (sqliteType == "REAL")
        {
            try
            {
                return Convert.ToDouble(
                    value,
                    CultureInfo.InvariantCulture);
            }
            catch { }
        }

        if (sqliteType == "NUMERIC")
        {
            if (value is decimal dec)
                return dec.ToString(
                    CultureInfo.InvariantCulture);

            if (value is IConvertible)
            {
                try
                {
                    return Convert.ToDouble(
                        value,
                        CultureInfo.InvariantCulture);
                }
                catch { }
            }
        }

        if (value is DateTime dt)
            return dt.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

        return Convert.ToString(
            value,
            CultureInfo.InvariantCulture) ?? "";
    }

    private void ExportSqliteSchema()
    {
        if (currentData == null || currentFile == null)
        {
            MessageBox.Show(
                this,
                "Open a DBF file first.",
                "SQLite Schema",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export SQLite Schema",
            Filter = "SQL files (*.sql)|*.sql",
            FileName =
                Path.GetFileNameWithoutExtension(currentFile) +
                "_sqlite_schema.sql"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var tableName = SanitizeSqlIdentifier(
                Path.GetFileNameWithoutExtension(currentFile));

            var sb = new StringBuilder();
            sb.AppendLine("-- JASS DBF Explorer v1.3");
            sb.AppendLine("-- SQLite schema generated from DBF metadata.");
            sb.AppendLine("-- This file creates the table only.");
            sb.AppendLine();
            sb.AppendLine($"CREATE TABLE [{tableName}] (");

            var columns = GetMigrationColumns();

            for (int i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                var comma = i == columns.Count - 1 ? "" : ",";
                sb.AppendLine(
                    $"    [{c.SqlName}] {c.SqliteType}{comma}");
            }

            sb.AppendLine(");");

            File.WriteAllText(
                dlg.FileName,
                sb.ToString(),
                new UTF8Encoding(true));

            statusLabel.Text =
                $"SQLite schema exported: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "SQLite Schema Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string MapSqliteType(string dbfType, Type clrType)
    {
        var t = dbfType.ToUpperInvariant();

        if (t.Contains("MEMO") || t == "M")
            return "TEXT";
        if (t == "L" || clrType == typeof(bool))
            return "INTEGER";
        if (t == "D" || clrType == typeof(DateTime))
            return "TEXT";
        if (t == "N")
            return "NUMERIC";
        if (t == "F")
            return "REAL";
        if (t == "I" || t == "B" || t == "Y")
            return "NUMERIC";
        if (t == "C" || t == "V" || t == "Q")
            return "TEXT";

        return IsNumericType(clrType) ? "NUMERIC" : "TEXT";
    }

    private static string BuildMigrationNote(string dbfType, Type clrType)
    {
        var t = dbfType.ToUpperInvariant();

        if (t == "D" || clrType == typeof(DateTime))
            return "Date stored as ISO text; review before production migration.";

        if (t == "M" || t.Contains("MEMO"))
            return "Memo content may require an associated memo file.";

        if (t == "N" || t == "F")
            return "Preserve numeric precision during migration.";

        if (t == "L")
            return "Logical values mapped to SQLite INTEGER 0/1.";

        if (t == "C")
            return "TEXT; review legacy encoding/code page.";

        return "Review before production migration.";
    }

    private static string SanitizeSqlIdentifier(string value)
    {
        var sb = new StringBuilder();

        foreach (var ch in value)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

        var result = sb.ToString();

        if (string.IsNullOrWhiteSpace(result))
            result = "Column";

        if (char.IsDigit(result[0]))
            result = "_" + result;

        return result;
    }

    private void SortColumn(int index)
    {
        if (currentData == null ||
            index < 0 ||
            index >= currentData!.Columns.Count)
            return;

        var column = currentData!.Columns[index].ColumnName;
        var view = currentData.DefaultView;
        var current = view.Sort;

        var ascending = !current.Equals(
            $"[{column}] ASC",
            StringComparison.OrdinalIgnoreCase);

        view.Sort =
            $"[{column}] {(ascending ? "ASC" : "DESC")}";

        grid.DataSource = view;
        statusLabel.Text =
            $"Sorted by {column} {(ascending ? "ascending" : "descending")}.";
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

            expressions.Add(string.Join(
                " OR ",
                currentData!.Columns.Cast<DataColumn>()
                    .Select(c =>
                        $"CONVERT([{c.ColumnName}], 'System.String') LIKE '%{escaped}%'")));
        }

        if (!string.IsNullOrEmpty(filterText) &&
            columnName != null)
        {
            var c = currentData!.Columns[columnName];
            var escaped = EscapeFilter(filterText);
            var op = filterOperator.SelectedItem?.ToString() ?? "Contains";

            if (IsNumericType(c.DataType) &&
                op is not "Contains" and not "Starts with")
            {
                if (decimal.TryParse(
                    filterText,
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out _))
                {
                    expressions.Add(
                        BuildNumericExpression(
                            columnName,
                            op,
                            filterText));
                }
            }
            else
            {
                expressions.Add(
                    BuildTextExpression(
                        columnName,
                        op,
                        escaped));
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
                $"Showing {view.Count:N0} of {currentData!.Rows.Count:N0} records.";
        }
        catch
        {
            currentData.DefaultView.RowFilter = "";
            grid.DataSource = currentData;
            statusLabel.Text = "Filter could not be applied.";
        }
    }

    private static string BuildTextExpression(
        string column,
        string op,
        string value)
    {
        var c =
            $"CONVERT([{column}], 'System.String')";

        return op switch
        {
            "Starts with" => $"{c} LIKE '{value}%'",
            "Equals" => $"{c} = '{value}'",
            "Not equals" => $"{c} <> '{value}'",
            _ => $"{c} LIKE '%{value}%'"
        };
    }

    private static string BuildNumericExpression(
        string column,
        string op,
        string value)
    {
        var safe = value.Replace(",", "");
        var c =
            $"CONVERT([{column}], 'System.Decimal')";

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

    private static bool TryDecimal(
        object value,
        out decimal result)
    {
        if (value is decimal d)
        {
            result = d;
            return true;
        }

        return decimal.TryParse(
            Convert.ToString(
                value,
                CultureInfo.InvariantCulture),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out result);
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

    private void SetMigrationBusy(bool busy)
    {
        migrateButton.Enabled = !busy && currentData != null;
        browseDestination.Enabled = !busy;
        destinationBox.Enabled = !busy;
        validateButton.Enabled = !busy && File.Exists(destinationBox.Text);
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void Log(string text)
    {
        if (InvokeRequired)
        {
            Invoke(() => Log(text));
            return;
        }

        migrationLog.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private void ExportCsv()
    {
        var view = GetCurrentView();

        if (view == null)
        {
            MessageBox.Show(
                this,
                "Open a DBF file first.",
                "Export CSV",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export CSV",
            Filter = "CSV files (*.csv)|*.csv",
            FileName =
                Path.GetFileNameWithoutExtension(
                    currentFile ?? "data") + ".csv"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            using var writer =
                new StreamWriter(
                    dlg.FileName,
                    false,
                    new UTF8Encoding(true));

            writer.WriteLine(
                string.Join(",",
                    view.Table.Columns.Cast<DataColumn>()
                        .Select(c => Csv(c.ColumnName))));

            foreach (DataRowView rowView in view)
            {
                writer.WriteLine(
                    string.Join(",",
                        view.Table.Columns.Cast<DataColumn>()
                            .Select(c =>
                                Csv(rowView.Row[c]?.ToString() ?? ""))));
            }

            statusLabel.Text =
                $"CSV exported: {Path.GetFileName(dlg.FileName)} ({view.Count:N0} records)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "CSV Export Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExportJson()
    {
        var view = GetCurrentView();

        if (view == null)
        {
            MessageBox.Show(
                this,
                "Open a DBF file first.",
                "Export JSON",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "Export JSON",
            Filter = "JSON files (*.json)|*.json",
            FileName =
                Path.GetFileNameWithoutExtension(
                    currentFile ?? "data") + ".json"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var records =
                new List<Dictionary<string, object?>>();

            foreach (DataRowView rv in view)
            {
                var record =
                    new Dictionary<string, object?>();

                foreach (DataColumn c in view.Table.Columns)
                    record[c.ColumnName] =
                        rv.Row[c] == DBNull.Value
                            ? null
                            : rv.Row[c];

                records.Add(record);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            File.WriteAllText(
                dlg.FileName,
                JsonSerializer.Serialize(
                    records,
                    options),
                Encoding.UTF8);

            statusLabel.Text =
                $"JSON exported: {Path.GetFileName(dlg.FileName)} ({view.Count:N0} records)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "JSON Export Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
        if (value.Contains('"') ||
            value.Contains(',') ||
            value.Contains('\n') ||
            value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    private void ApplyPalette()
    {
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        grid.BackgroundColor = SystemColors.Window;
        grid.ForeColor = SystemColors.WindowText;
        grid.ColumnHeadersDefaultCellStyle.BackColor =
            SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor =
            SystemColors.ControlText;
        schemaTree.BackColor = SystemColors.Window;
        schemaTree.ForeColor = SystemColors.WindowText;
    }

    private sealed class MigrationColumn
    {
        public int Ordinal { get; init; }
        public string SourceName { get; init; } = "";
        public string SqlName { get; init; } = "";
        public string SqliteType { get; init; } = "TEXT";
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
            using var br =
                new BinaryReader(
                    fs,
                    Encoding.ASCII,
                    leaveOpen: false);

            if (fs.Length < 32)
                throw new InvalidDataException(
                    "The file is too small to contain a DBF header.");

            var version = br.ReadByte();
            var year = 1900 + br.ReadByte();
            var month = br.ReadByte();
            var day = br.ReadByte();
            var recordCount = br.ReadInt32();
            var headerLength = br.ReadInt16();
            var recordLength = br.ReadInt16();

            fs.Position = 29;
            var codePage = br.ReadByte();

            var fieldCount =
                Math.Max(0, (headerLength - 33) / 32);

            var memo = (version & 0x80) != 0;

            DateTime date;

            try
            {
                date = new DateTime(
                    year,
                    month,
                    day);
            }
            catch
            {
                date = DateTime.MinValue;
            }

            return new DbfHeaderInfo
            {
                FilePath = path,
                Version = version,
                VersionDescription =
                    DescribeVersion(version),
                LastUpdate = date,
                RecordCount = recordCount,
                HeaderLength = headerLength,
                RecordLength = recordLength,
                FileSize =
                    new FileInfo(path).Length,
                FieldCount = fieldCount,
                CodePageByte = codePage,
                CodePageDescription =
                    DescribeCodePage(codePage),
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
                _ => v == 0
                    ? "Not specified"
                    : "Unknown code page"
            };
        }
    }

    private static class DbfAnalyzer
    {
        public static int CountDeletedRecords(
            string path,
            DbfHeaderInfo h)
        {
            if (h.RecordCount <= 0 ||
                h.RecordLength <= 0)
                return 0;

            try
            {
                using var fs = File.OpenRead(path);

                if (h.HeaderLength >= fs.Length)
                    return 0;

                fs.Position = h.HeaderLength;

                var marker = new byte[1];
                var deleted = 0;

                for (int i = 0;
                     i < h.RecordCount;
                     i++)
                {
                    if (fs.Position >= fs.Length)
                        break;

                    var read = fs.Read(
                        marker,
                        0,
                        1);

                    if (read != 1)
                        break;

                    if (marker[0] == 0x2A)
                        deleted++;

                    var remaining =
                        h.RecordLength - 1;

                    if (remaining > 0)
                        fs.Seek(
                            remaining,
                            SeekOrigin.Current);
                }

                return deleted;
            }
            catch
            {
                return 0;
            }
        }
    }
}
