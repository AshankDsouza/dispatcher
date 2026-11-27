using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Dispatcher.Core;

namespace Dispatcher.Desktop;

/// <summary>One row of the results table (property names become column headers).</summary>
public record ScheduleRow(string Task, string Customer, string Job, string Skill,
    string Start, string End, int Crew, int Priority, string Status, string Assigned);

/// <summary>
/// The database is the source of truth: Import writes the workbook into it, Run dispatch reads its input
/// from it and stores assignments, and the table and export always show what is stored.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly FilePickerFileType Xlsx = new("Excel workbook") { Patterns = ["*.xlsx"] };
    private DispatchSnapshot? snapshot;

    public MainWindow()
    {
        InitializeComponent();
        // Local dev default matches the passwordless Compose database.
        ConnectionBox.Text = Environment.GetEnvironmentVariable("DISPATCHER_CONNECTION_STRING")
            ?? "Host=localhost;Database=dispatcher;Username=dispatcher";
        Opened += async (_, _) => await RunAsync("Loading from database…", async connection =>
        {
            Show(await Database.LoadAsync(connection));
            return snapshot!.Intake.Jobs.Count == 0
                ? "The database is empty. Choose an intake workbook and click Import into database."
                : "Loaded the stored data and schedule from the database.";
        });
    }

    private async void OnReload(object? sender, RoutedEventArgs e) =>
        await RunAsync("Loading from database…", async connection =>
        {
            Show(await Database.LoadAsync(connection));
            return "Reloaded from the database.";
        });

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var input = InputBox.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            ShowError("Choose an intake workbook first (Browse…).");
            return;
        }
        await RunAsync("Importing…", async connection =>
        {
            var intake = await Task.Run(() => Workbooks.Read(input));
            await Database.ImportAsync(connection, intake);
            Show(await Database.LoadAsync(connection));
            return $"Imported {Path.GetFileName(input)} into the database. Click Run dispatch to assign crews.";
        });
    }

    private async void OnDispatch(object? sender, RoutedEventArgs e) =>
        await RunAsync("Dispatching…", async connection =>
        {
            Show(await Database.DispatchAsync(connection));
            return snapshot!.Intake.Jobs.Count == 0
                ? "There are no tasks in the database. Import an intake workbook first."
                : "Dispatch finished; new bookings are saved in the scheduled table.";
        });

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (await PickSaveAsync("Export schedule", "schedule.xlsx") is not { } path) return;
        await RunAsync("Exporting…", async connection =>
        {
            var stored = await Database.LoadAsync(connection);
            if (stored.DispatchedAt is null)
                throw new InvalidOperationException("Dispatch has not run on the stored data yet. Click Run dispatch first.");
            await Task.Run(() => Workbooks.WriteSchedule(path, stored.Intake, stored.Assignments));
            Show(stored);
            return $"Exported the stored schedule to {path}.";
        });
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
            ShowStatus($"Sample data saved to {path} and selected. Click Import into database to load it.");
        }
        catch (Exception ex) { ShowError($"Could not save sample data: {ex.Message}"); }
    }

    /// <summary>Runs a database action with the buttons disabled and reports the outcome.</summary>
    private async Task RunAsync(string progress, Func<string, Task<string>> action)
    {
        var connection = ConnectionBox.Text;
        if (string.IsNullOrWhiteSpace(connection))
        {
            ShowError("Enter a PostgreSQL connection string.");
            return;
        }
        SetBusy(true);
        ShowStatus(progress);
        try { ShowStatus(await action(connection)); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false); }
    }

    private void Show(DispatchSnapshot stored)
    {
        snapshot = stored;
        var (intake, assignments, dispatchedAt) = stored;
        var customers = intake.Customers.ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        ResultsGrid.ItemsSource = assignments.Select(a => new ScheduleRow(
            a.Job.Id, customers[a.Job.CustomerId], a.Job.Description, a.Job.Skill,
            a.Job.Start.ToString("ddd dd MMM HH:mm"), a.Job.End.ToString("HH:mm"), a.Job.CrewSize, a.Job.Priority,
            a.Crew.Count > 0 ? "Scheduled" : dispatchedAt is null ? "Not dispatched" : "Unscheduled",
            string.Join(", ", a.Crew.Select(c => c.Name)))).ToList();

        var counts = $"Database: {intake.Employees.Count} employees, {intake.Customers.Count} customers, " +
            $"{intake.Jobs.Count} tasks";
        var scheduled = assignments.Count(a => a.Crew.Count > 0);
        var waiting = assignments.Count - scheduled;
        SummaryText.Text = dispatchedAt is null
            ? $"{counts} · {scheduled} scheduled, {waiting} waiting for dispatch"
            : $"{counts} · {scheduled} scheduled, {waiting} unscheduled · dispatched {dispatchedAt:ddd dd MMM HH:mm}";
        ExportButton.IsEnabled = dispatchedAt is not null;
    }

    private void SetBusy(bool busy)
    {
        ImportButton.IsEnabled = DispatchButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && snapshot?.DispatchedAt is not null;
    }

    private async Task<string?> PickSaveAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, DefaultExtension = "xlsx", FileTypeChoices = [Xlsx]
        });
        return file?.TryGetLocalPath();
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
