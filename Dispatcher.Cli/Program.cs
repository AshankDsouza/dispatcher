using Dispatcher.Core;
using Npgsql;

if (args is ["--write-sample", var samplePath, ..])
{
    // Writes the built-in mock data (Dispatcher.Core/SampleData.cs) as an intake workbook.
    // Optional third argument: the Monday to date it from (yyyy-MM-dd); defaults to next Monday.
    var monday = args.Length > 2 ? DateTime.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)
        : SampleData.NextMonday(DateTime.Today);
    Workbooks.WriteIntake(Path.GetFullPath(samplePath), SampleData.Create(monday));
    Console.WriteLine($"Wrote sample data for the week of {monday:yyyy-MM-dd} to {samplePath}.");
    return 0;
}

var importOnly = args is ["--import", _];
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Dispatcher.Cli <input.xlsx> <output.xlsx>");
    Console.Error.WriteLine("       Dispatcher.Cli --import <input.xlsx>   (load into the database, no dispatch)");
    Console.Error.WriteLine("       Dispatcher.Cli --write-sample <path.xlsx> [monday yyyy-MM-dd]");
    return 2;
}

try
{
    var input = Path.GetFullPath(importOnly ? args[1] : args[0]);
    var output = importOnly ? "" : Path.GetFullPath(args[1]);
    if (!importOnly && string.Equals(input, output, OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw new ArgumentException("The output workbook must be different from the input workbook.");

    var connectionString = Environment.GetEnvironmentVariable("DISPATCHER_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
            Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "dispatcher",
            Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "dispatcher",
            Password = string.IsNullOrEmpty(password) ? null : password
        }.ConnectionString;
    }

    // Import the workbook, dispatch from the database, export what the database holds.
    await Database.ImportAsync(connectionString, Workbooks.Read(input));
    if (importOnly)
    {
        Console.WriteLine($"Imported {args[1]} into the database.");
        return 0;
    }
    var (intake, assignments, _) = await Database.DispatchAsync(connectionString);
    Workbooks.WriteSchedule(output, intake, assignments);
    Console.WriteLine($"Finished: {assignments.Count(a => a.Crew.Count > 0)} scheduled, " +
        $"{assignments.Count(a => a.Crew.Count == 0)} unscheduled.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Dispatcher failed: {ex.Message}");
    return 1;
}
