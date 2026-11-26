using Dispatcher.Core;
using Npgsql;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Dispatcher.Cli <input.xlsx> <output.xlsx>");
    return 2;
}

try
{
    var input = Path.GetFullPath(args[0]);
    var output = Path.GetFullPath(args[1]);
    if (string.Equals(input, output, OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw new ArgumentException("The output workbook must be different from the input workbook.");

    var connectionString = Environment.GetEnvironmentVariable("DISPATCHER_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Set DISPATCHER_CONNECTION_STRING or POSTGRES_PASSWORD.");
        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost",
            Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "dispatcher",
            Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "dispatcher",
            Password = password
        }.ConnectionString;
    }

    var intake = Workbooks.Read(input);
    var assignments = Scheduler.Schedule(intake);
    await Database.SaveAsync(connectionString, intake, assignments);
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
