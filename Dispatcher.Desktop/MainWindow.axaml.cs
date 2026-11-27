using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Dispatcher.Core;

namespace Dispatcher.Desktop;

/// <summary>One row of the results table (property names become column headers).</summary>
public record ScheduleRow(string Task, string Customer, string Job, string Skill,
    string Start, string End, int Crew, string Status, string Assigned);

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType Xlsx = new("Excel workbook") { Patterns = ["*.xlsx"] };

    public MainWindow()
    {
        InitializeComponent();
        // Local dev default matches the passwordless Compose database.
        ConnectionBox.Text = Environment.GetEnvironmentVariable("DISPATCHER_CONNECTION_STRING")
            ?? "Host=localhost;Database=dispatcher;Username=dispatcher";
    }

    private async Task<string?> PickSaveAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = "xlsx", FileTypeChoices = [Xlsx]
        });
        return file?.TryGetLocalPath();
    }

    private async void OnBrowseInput(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select intake workbook", AllowMultiple = false, FileTypeFilter = [Xlsx]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            InputBox.Text = path;
    }

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        if (await PickSaveAsync("Save schedule as", "schedule.xlsx") is { } path)
            OutputBox.Text = path;
    }

    private async void OnCreateTemplate(object? sender, RoutedEventArgs e)
    {
        if (await PickSaveAsync("Save intake template", "intake.xlsx") is not { } path) return;
        try { Workbooks.CreateTemplate(path); ShowStatus($"Template saved to {path}."); }
        catch (Exception ex) { ShowError($"Could not create template: {ex.Message}"); }
    }

    private async void OnSaveSample(object? sender, RoutedEventArgs e)
    {
        if (await PickSaveAsync("Save sample data", "mock_data.xlsx") is not { } path) return;
        try
        {
            Workbooks.WriteIntake(path, SampleData.Create(SampleData.NextMonday(DateTime.Today)));
            InputBox.Text = path;
            ShowStatus($"Sample data saved to {path} and selected as the input workbook.");
        }
        catch (Exception ex) { ShowError($"Could not save sample data: {ex.Message}"); }
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        var input = InputBox.Text;
        var connection = ConnectionBox.Text;
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(connection))
        {
            ShowError("Choose an input workbook and enter a PostgreSQL connection string.");
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            if (await PickSaveAsync("Save schedule as", "schedule.xlsx") is not { } picked) return;
            OutputBox.Text = picked;
        }
        var output = OutputBox.Text!;
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
        {
            ShowError("The output workbook must be different from the input workbook.");
            return;
        }

        RunButton.IsEnabled = false;
        ShowStatus("Processing…");
        try
        {
            var (intake, assignments) = await Task.Run(() =>
            {
                var intake = Workbooks.Read(input);
                return (intake, Scheduler.Schedule(intake));
            });
            await Database.SaveAsync(connection, intake, assignments);
            await Task.Run(() => Workbooks.WriteSchedule(output, intake, assignments));

            var customers = intake.Customers.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
            ResultsGrid.ItemsSource = assignments.Select(a => new ScheduleRow(
                a.Job.Id, customers[a.Job.CustomerId], a.Job.Description, a.Job.Skill,
                a.Job.Start.ToString("ddd dd MMM HH:mm"), a.Job.End.ToString("HH:mm"), a.Job.CrewSize,
                a.Crew.Count == 0 ? "Unscheduled" : "Scheduled",
                string.Join(", ", a.Crew.Select(c => c.Name)))).ToList();

            ShowStatus($"Finished: {assignments.Count(a => a.Crew.Count > 0)} scheduled, " +
                $"{assignments.Count(a => a.Crew.Count == 0)} unscheduled. Saved to {output}.");
        }
        catch (Exception ex) { ShowError($"Processing failed: {ex.Message}"); }
        finally { RunButton.IsEnabled = true; }
    }

    private void ShowStatus(string message)
    {
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        StatusText.Text = message;
    }

    private void ShowError(string message)
    {
        StatusText.Foreground = Brushes.IndianRed;
        StatusText.Text = message;
    }
}
