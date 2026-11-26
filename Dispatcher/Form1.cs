using Dispatcher.Core;

namespace Dispatcher;

public partial class Form1 : Form
{
    private readonly TextBox connection = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly TextBox input = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button run = new() { Text = "Import and schedule", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, Text = "Create a template or select an intake workbook." };

    public Form1()
    {
        InitializeComponent();
        Text = "Dispatcher";
        Width = 760;
        Height = 300;
        MinimumSize = new Size(600, 300);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, RowCount = 5,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        connection.Text = Environment.GetEnvironmentVariable("DISPATCHER_CONNECTION_STRING") ?? "";
        layout.Controls.Add(new Label { Text = "PostgreSQL connection string", AutoSize = true }, 0, 0);
        layout.Controls.Add(connection, 1, 0);
        layout.Controls.Add(new Label { Text = "Input workbook", AutoSize = true }, 0, 1);
        layout.Controls.Add(input, 1, 1);
        var inputButton = new Button { Text = "Browse...", AutoSize = true };
        inputButton.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx" };
            if (dialog.ShowDialog() == DialogResult.OK) input.Text = dialog.FileName;
        };
        layout.Controls.Add(inputButton, 2, 1);
        layout.Controls.Add(new Label { Text = "Output workbook", AutoSize = true }, 0, 2);
        layout.Controls.Add(output, 1, 2);
        var outputButton = new Button { Text = "Browse...", AutoSize = true };
        outputButton.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "schedule.xlsx" };
            if (dialog.ShowDialog() == DialogResult.OK) output.Text = dialog.FileName;
        };
        layout.Controls.Add(outputButton, 2, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var template = new Button { Text = "Create intake template", AutoSize = true };
        template.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "intake.xlsx" };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            try { Workbooks.CreateTemplate(dialog.FileName); status.Text = "Template created."; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not create template"); }
        };
        run.Click += RunClicked;
        buttons.Controls.Add(template);
        buttons.Controls.Add(run);
        layout.Controls.Add(buttons, 1, 3);
        layout.Controls.Add(status, 1, 4);
        Controls.Add(layout);
    }

    private async void RunClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(connection.Text) || string.IsNullOrWhiteSpace(input.Text) ||
            string.IsNullOrWhiteSpace(output.Text))
        {
            MessageBox.Show(this, "Select an input and output workbook and enter a PostgreSQL connection string.");
            return;
        }
        var inputPath = input.Text;
        var outputPath = output.Text;
        var connectionString = connection.Text;
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "The output workbook must be different from the input workbook.");
            return;
        }
        run.Enabled = false;
        try
        {
            status.Text = "Processing...";
            var intake = Workbooks.Read(inputPath);
            var assignments = Scheduler.Schedule(intake);
            await Database.SaveAsync(connectionString, intake, assignments);
            Workbooks.WriteSchedule(outputPath, intake, assignments);
            status.Text = $"Finished: {assignments.Count(a => a.Crew.Count > 0)} scheduled, " +
                $"{assignments.Count(a => a.Crew.Count == 0)} unscheduled.";
        }
        catch (Exception ex)
        {
            status.Text = "Processing failed. See the error message.";
            MessageBox.Show(this, ex.Message, "Dispatcher");
        }
        finally { run.Enabled = true; }
    }
}
