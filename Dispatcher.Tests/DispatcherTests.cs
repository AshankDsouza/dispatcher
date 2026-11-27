using ClosedXML.Excel;
using Dispatcher.Core;
using Npgsql;

namespace Dispatcher.Tests;

public class DispatcherTests
{
    private static string Crew(Assignment a) => string.Join(",", a.Crew.Select(e => e.Id));

    [Fact]
    public void MockDataWorkbookKeepsBookingsAndDispatchesTheRest()
    {
        var intake = Workbooks.Read(Path.Combine(AppContext.BaseDirectory, "mock_data.xlsx"));
        Assert.Equal(7, intake.Jobs.Count);
        Assert.Equal(4, intake.Scheduled.Select(s => s.TaskId).Distinct().Count());

        var assignments = Scheduler.Schedule(intake).ToDictionary(a => a.Job.Id);

        // Already booked in the Scheduled sheet: unchanged.
        Assert.Equal("E001,E002", Crew(assignments["T001"]));
        Assert.Equal("E005", Crew(assignments["T002"]));
        Assert.Equal("E003", Crew(assignments["T003"]));
        Assert.Equal("E007,E008,E009", Crew(assignments["T004"]));
        // Dispatched by priority; booked employees are busy, ranked by rating then completion.
        Assert.Equal("E012", Crew(assignments["T005"]));      // rating 4, 92% beats E010's 4, 90%
        Assert.Equal("E010,E006", Crew(assignments["T006"])); // E001/E002/E003 are booked at that time
        Assert.Equal("E009,E007", Crew(assignments["T007"])); // rating 5 first, then rating 4 / 93%
        Assert.Equal(3, Scheduler.NewBookings(intake, assignments.Values).Select(b => b.TaskId).Distinct().Count());
    }

    [Fact]
    public void SampleDataRoundTripsThroughWorkbook()
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
            Assert.Equal(sample.Scheduled, intake.Scheduled);
            Assert.All(Scheduler.Schedule(intake), a => Assert.NotEmpty(a.Crew));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void SchedulerOrdersByPriorityAndRanksByRatingCompletionThenExperience()
    {
        var at = (int hour) => new DateTime(2026, 1, 1, hour, 0, 0);
        var intake = new Intake(
            [new("A", "Ari", 4, 80), new("B", "Bea", 4, 80), new("C", "Cam", 4, 95), new("D", "Dev", 5, 10),
             new("E", "Eli", 5, 100)],
            [new("A", "Welding"), new("B", "Welding"), new("C", "Welding"), new("D", "Welding")],
            [new("C1", "Customer")],
            [new("0", "C1", "Booked early", "Welding", at(8), at(9), 1, 1),
             new("3", "C1", "Low", "Welding", at(9), at(10), 1, 1),
             new("2", "C1", "Mid", "Welding", at(9), at(10), 1, 3),
             new("1", "C1", "High", "Welding", at(9), at(10), 2, 5),
             new("4", "C1", "Low, no one left", "Welding", at(9), at(10), 1, 1),
             new("5", "C1", "High, D booked", "Welding", at(11), at(12), 1, 5),
             new("6", "C1", "Booked late", "Welding", at(11), at(12), 1, 1)],
            [new("B", "Welding")],
            [new("0", "A", at(8), at(9)), new("6", "D", at(11), at(12))]);

        var assignments = Scheduler.Schedule(intake);

        Assert.Equal(new[] { "0", "1", "2", "3", "4", "5", "6" }, assignments.Select(a => a.Job.Id));
        Assert.Equal("A", Crew(assignments[0]));   // existing booking kept despite low priority
        Assert.Equal("D,C", Crew(assignments[1])); // priority 5 first: rating 5, then rating 4 with 95%
        Assert.Equal("B", Crew(assignments[2]));   // A and B tie on rating/completion; B has more experience
        Assert.Equal("A", Crew(assignments[3]));   // adjacent to A's 8-9 booking is allowed
        Assert.Empty(assignments[4].Crew);         // E lacks the skill; everyone else is busy
        Assert.Equal("C", Crew(assignments[5]));   // D is booked 11-12 on task 6
        Assert.Equal("D", Crew(assignments[6]));
        Assert.Equal(assignments.Select(Crew), Scheduler.Schedule(intake).Select(Crew));
        Assert.Equal(new[] { "1", "1", "2", "3", "5" }, Scheduler.NewBookings(intake, assignments).Select(b => b.TaskId));
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
                employees.Cell("C2").Value = 4;
                employees.Cell("D2").Value = 0.85;             // typed as 85% in Excel
                employees.Cell("D2").Style.NumberFormat.NumberFormatId = 9;
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
                    tasks.Cell(row, 8).Value = 3;
                }
                workbook.Save();
            }
            var intake = Workbooks.Read(input);
            Assert.Equal(new Employee("E1", "Ana", 4, 85), Assert.Single(intake.Employees));
            var result = Scheduler.Schedule(intake);
            Workbooks.WriteSchedule(output, intake, result);
            using (var exported = new XLWorkbook(output))
            {
                var sheet = exported.Worksheet("Schedule");
                Assert.Equal(3, sheet.Cell("I2").GetValue<int>());
                Assert.Equal("Scheduled", sheet.Cell("J2").GetString());
                Assert.Equal("E1", sheet.Cell("K2").GetString());
                Assert.Equal("Unscheduled", sheet.Cell("J3").GetString());
                Assert.Equal(10, sheet.Cell("F2").GetDateTime().Second);
                Assert.Contains("ss", sheet.Cell("F2").Style.DateFormat.Format);
            }

            void Expect(string message, Action<XLWorkbook> change)
            {
                File.Copy(input, Path.Combine(dir, "bad.xlsx"), true);
                using (var workbook = new XLWorkbook(Path.Combine(dir, "bad.xlsx")))
                {
                    change(workbook);
                    workbook.Save();
                }
                Assert.Contains(message, Assert.Throws<FormatException>(
                    () => Workbooks.Read(Path.Combine(dir, "bad.xlsx"))).Message);
            }
            Expect("unknown customer", w => w.Worksheet("Tasks").Cell("B2").Value = "unknown");
            Expect("from 1 to 5", w => w.Worksheet("Tasks").Cell("H2").Value = 6);
            Expect("from 1 to 5", w => w.Worksheet("Employees").Cell("C2").Value = 0);
            Expect("0 to 100", w => w.Worksheet("Employees").Cell("D2").Value = 1.5);
            Expect("unknown task", w =>
            {
                var scheduled = w.Worksheet("Scheduled");
                scheduled.Cell("A2").Value = "T9";
                scheduled.Cell("B2").Value = "E1";
                scheduled.Cell("C2").Value = new DateTime(2026, 1, 1, 9, 0, 0);
                scheduled.Cell("D2").Value = new DateTime(2026, 1, 1, 10, 0, 0);
            });
            Expect("overlapping", w =>
            {
                var scheduled = w.Worksheet("Scheduled");
                for (var row = 2; row <= 3; row++)
                {
                    scheduled.Cell(row, 1).Value = $"T{row}";
                    scheduled.Cell(row, 2).Value = "E1";
                    scheduled.Cell(row, 3).Value = new DateTime(2026, 1, 1, 9, 0, 0);
                    scheduled.Cell(row, 4).Value = new DateTime(2026, 1, 1, 10, 0, 0);
                }
            });
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

    private static string TestDatabase => Environment.GetEnvironmentVariable("DISPATCHER_TEST_DATABASE")!;

    private static async Task<long> Count(string table)
    {
        await using var connection = new NpgsqlConnection(TestDatabase);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM dispatcher.{table}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Sql(string sql)
    {
        await using var connection = new NpgsqlConnection(TestDatabase);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ImportReplacesDataClearsAssignmentsAndRollsBackOnFailure()
    {
        var start = new DateTime(2026, 1, 1, 9, 0, 0);
        var intake = new Intake([new("E", "Employee", 3, 50)], [new("E", "Skill")], [new("C", "Customer")],
            [new("T", "C", "Task", "Skill", start, start.AddHours(1), 1, 3)], [new("E", "Skill")], []);
        await Database.ImportAsync(TestDatabase, intake);
        await Database.ImportAsync(TestDatabase, intake);
        var dispatched = await Database.DispatchAsync(TestDatabase);
        Assert.NotNull(dispatched.DispatchedAt);
        Assert.Equal(1, await Count("scheduled"));

        var invalid = intake with { Customers = [] };
        await Assert.ThrowsAsync<PostgresException>(() => Database.ImportAsync(TestDatabase, invalid));
        Assert.Equal(1, await Count("tasks"));
        Assert.Equal(1, await Count("scheduled"));
        Assert.Equal(1, await Count("completed_work"));

        await Database.ImportAsync(TestDatabase, intake);
        var reimported = await Database.LoadAsync(TestDatabase);
        Assert.Null(reimported.DispatchedAt);
        Assert.All(reimported.Assignments, a => Assert.Empty(a.Crew));
    }

    [PostgresFact]
    public async Task DatabaseRoundTripsIntake()
    {
        var sample = SampleData.Create(new DateTime(2026, 11, 30));
        await Database.ImportAsync(TestDatabase, sample);
        var stored = await Database.LoadIntakeAsync(TestDatabase);

        Assert.Equal(sample.Employees.OrderBy(e => e.Id, StringComparer.Ordinal), stored.Employees);
        Assert.Equal(sample.Customers.OrderBy(c => c.Id, StringComparer.Ordinal), stored.Customers);
        Assert.Equal(sample.Skills.OrderBy(s => s.EmployeeId, StringComparer.Ordinal)
            .ThenBy(s => s.Skill, StringComparer.Ordinal), stored.Skills);
        Assert.Equal(sample.Jobs.OrderBy(j => j.Start).ThenBy(j => j.Id, StringComparer.Ordinal), stored.Jobs);
        Assert.Equal(sample.CompletedWork, stored.CompletedWork);
        Assert.Equal(sample.Scheduled.OrderBy(b => b.TaskId, StringComparer.Ordinal)
            .ThenBy(b => b.EmployeeId, StringComparer.Ordinal), stored.Scheduled);
    }

    [PostgresFact]
    public async Task DispatchReadsItsInputFromTheDatabaseAndKeepsExistingBookings()
    {
        var sample = SampleData.Create(new DateTime(2026, 11, 30));
        await Database.ImportAsync(TestDatabase, sample);
        Assert.Equal(sample.Scheduled.Count, await Count("scheduled"));

        // Change data only in the database: Omar (E012) loses Electrical, Tom (E004) is now rated 5,
        // and a new roofing task is added.
        await Sql("""
            DELETE FROM dispatcher.employee_skills WHERE employee_id = 'E012' AND skill = 'Electrical';
            UPDATE dispatcher.employees SET rating = 5 WHERE id = 'E004';
            INSERT INTO dispatcher.tasks VALUES
                ('T900', 'C001', 'Added in database', 'Roofing', '2026-12-02 09:00', '2026-12-02 10:00', 1, 2);
            """);
        var first = await Database.DispatchAsync(TestDatabase);
        var crews = first.Assignments.ToDictionary(a => a.Job.Id, Crew);

        Assert.Equal("E001,E002", crews["T001"]); // existing bookings unchanged
        Assert.Equal("E007,E008,E009", crews["T004"]);
        Assert.Equal("E004", crews["T005"]);      // picked from database data, not the workbook
        Assert.Equal("E009", crews["T900"]);      // task that only exists in the database
        Assert.All(first.Assignments, a => Assert.NotEmpty(a.Crew));
        var rows = await Count("scheduled");
        Assert.Equal(first.Assignments.Sum(a => a.Crew.Count), rows);

        // Dispatching again changes nothing: every task is already booked.
        var second = await Database.DispatchAsync(TestDatabase);
        Assert.Equal(first.Assignments.Select(Crew), second.Assignments.Select(Crew));
        Assert.Equal(rows, await Count("scheduled"));
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
