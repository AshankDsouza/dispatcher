using ClosedXML.Excel;
using Dispatcher.Core;
using Npgsql;

namespace Dispatcher.Tests;

public class DispatcherTests
{
    [Fact]
    public void ExampleWorkbookProducesExpectedSchedule()
    {
        var intake = Workbooks.Read(Path.Combine(AppContext.BaseDirectory, "example.xlsx"));
        var assignments = Scheduler.Schedule(intake);

        Assert.Equal(new[] { "T001", "T002", "T003", "T004" }, assignments.Select(a => a.Job.Id));
        Assert.Equal(new[] { "E001" }, assignments[0].Crew.Select(e => e.Id));
        Assert.Empty(assignments[1].Crew);
        Assert.Equal(new[] { "E003" }, assignments[2].Crew.Select(e => e.Id));
        Assert.Equal(new[] { "E001", "E002" }, assignments[3].Crew.Select(e => e.Id));
    }

    [Fact]
    public void SampleDataRoundTripsThroughWorkbookAndSchedules()
    {
        var monday = SampleData.NextMonday(new DateTime(2026, 11, 26));
        Assert.Equal(new DateTime(2026, 11, 30), monday);
        var sample = SampleData.Create(monday);
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.xlsx");
        try
        {
            Workbooks.WriteIntake(file, sample);
            var intake = Workbooks.Read(file);
            Assert.Equal(sample.Employees, intake.Employees);
            Assert.Equal(sample.Skills, intake.Skills);
            Assert.Equal(sample.Customers, intake.Customers);
            Assert.Equal(sample.Jobs, intake.Jobs);
            Assert.Equal(sample.CompletedWork, intake.CompletedWork);

            var assignments = Scheduler.Schedule(intake);
            var unscheduled = assignments.Where(a => a.Crew.Count == 0).Select(a => a.Job.Description).ToList();
            Assert.Contains("Tear-off and reroof east wing", unscheduled);
            Assert.Contains("Thermostat replacement, all units", unscheduled);
            Assert.True(assignments.Count(a => a.Crew.Count > 0) >= 20);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void SchedulerRanksExperienceAvoidsOverlapsAndLeavesIncompleteCrewsUnassigned()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var intake = new Intake(
            [new("B", "Bea"), new("A", "Ari"), new("C", "Cam")],
            [new("A", "Welding"), new("B", "Welding"), new("C", "Welding")],
            [new("C1", "Customer")],
            [new("2", "C1", "Second", "Welding", start, start.AddHours(1), 2),
             new("1", "C1", "First", "Welding", start, start.AddHours(1), 2),
             new("3", "C1", "Next", "Welding", start.AddHours(1), start.AddHours(2), 1)],
            [new("B", "Welding")]);

        var assignments = Scheduler.Schedule(intake);

        Assert.Equal(new[] { "1", "2", "3" }, assignments.Select(a => a.Job.Id));
        Assert.Equal(new[] { "B", "A" }, assignments[0].Crew.Select(e => e.Id));
        Assert.Empty(assignments[1].Crew);
        Assert.Equal(new[] { "B" }, assignments[2].Crew.Select(e => e.Id));
        Assert.Equal(assignments.Select(a => string.Join(",", a.Crew.Select(e => e.Id))),
            Scheduler.Schedule(intake).Select(a => string.Join(",", a.Crew.Select(e => e.Id))));
    }

    [Fact]
    public void WorkbookRoundTripValidatesAndExportsBothStatuses()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "input.xlsx");
            var output = Path.Combine(dir, "output.xlsx");
            Workbooks.CreateTemplate(input);
            using (var workbook = new XLWorkbook(input))
            {
                var employees = workbook.Worksheet("Employees");
                employees.Cell("A2").Value = "E1";
                employees.Cell("B2").Value = "Ana";
                workbook.Worksheet("Skills").Cell("A2").Value = "E1";
                workbook.Worksheet("Skills").Cell("B2").Value = "Plumbing";
                workbook.Worksheet("Customers").Cell("A2").Value = "C1";
                workbook.Worksheet("Customers").Cell("B2").Value = "Customer";
                var tasks = workbook.Worksheet("Tasks");
                for (var row = 2; row <= 3; row++)
                {
                    tasks.Cell(row, 1).Value = $"T{row}";
                    tasks.Cell(row, 2).Value = "C1";
                    tasks.Cell(row, 3).Value = "Repair";
                    tasks.Cell(row, 4).Value = "Plumbing";
                    tasks.Cell(row, 5).Value = "2026-01-01 09:00:10";
                    tasks.Cell(row, 6).Value = new DateTime(2026, 1, 1, 10, 0, 20);
                    tasks.Cell(row, 7).Value = 1;
                }
                workbook.Save();
            }
            var intake = Workbooks.Read(input);
            var result = Scheduler.Schedule(intake);
            Workbooks.WriteSchedule(output, intake, result);
            using var exported = new XLWorkbook(output);
            Assert.Equal("Scheduled", exported.Worksheet("Schedule").Cell("I2").GetString());
            Assert.Equal("E1", exported.Worksheet("Schedule").Cell("J2").GetString());
            Assert.Equal("Unscheduled", exported.Worksheet("Schedule").Cell("I3").GetString());
            Assert.Equal(10, exported.Worksheet("Schedule").Cell("F2").GetDateTime().Second);
            Assert.Contains("ss", exported.Worksheet("Schedule").Cell("F2").Style.DateFormat.Format);

            using (var workbook = new XLWorkbook(input))
            {
                workbook.Worksheet("Tasks").Cell("B2").Value = "unknown";
                workbook.Save();
            }
            Assert.Throws<FormatException>(() => Workbooks.Read(input));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void WorkbookRejectsFormulas()
    {
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            Workbooks.CreateTemplate(file);
            using (var workbook = new XLWorkbook(file))
            {
                workbook.Worksheet("Employees").Cell("A2").FormulaA1 = "\"E1\"";
                workbook.Worksheet("Employees").Cell("B2").Value = "Ana";
                workbook.Save();
            }
            Assert.Contains("formulas", Assert.Throws<FormatException>(() => Workbooks.Read(file)).Message);
        }
        finally { File.Delete(file); }
    }

    [PostgresFact]
    public async Task DatabaseReplacesSnapshotAndRollsBackOnFailure()
    {
        var connectionString = Environment.GetEnvironmentVariable("DISPATCHER_TEST_DATABASE")!;
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var intake = new Intake([new("E", "Employee")], [new("E", "Skill")], [new("C", "Customer")],
            [new("T", "C", "Task", "Skill", start, start.AddHours(1), 1)], [new("E", "Skill")]);
        var assignments = Scheduler.Schedule(intake);
        await Database.SaveAsync(connectionString, intake, assignments);
        await Database.SaveAsync(connectionString, intake, assignments);

        var invalid = intake with { Customers = [] };
        await Assert.ThrowsAsync<PostgresException>(() => Database.SaveAsync(connectionString, invalid, assignments));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM dispatcher.tasks), (SELECT count(*) FROM dispatcher.assignments), " +
            "(SELECT count(*) FROM dispatcher.completed_work)", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
    }
}

public class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPATCHER_TEST_DATABASE")))
            Skip = "Set DISPATCHER_TEST_DATABASE to a disposable PostgreSQL database to run integration tests.";
    }
}
