using Npgsql;

namespace Dispatcher.Core;

/// <summary>The stored schedule: all data plus one assignment per task (empty crew = not scheduled).</summary>
/// <param name="DispatchedAt">When dispatch last ran on this data; null if it has not run since the last import.</param>
public record DispatchSnapshot(Intake Intake, List<Assignment> Assignments, DateTime? DispatchedAt);

/// <summary>
/// PostgreSQL is the source of truth: workbooks are imported into it, dispatch reads its input from it
/// and adds bookings to the scheduled table, and the displayed/exported schedule is read from it.
/// </summary>
public static class Database
{
    private const long LockKey = 1684632432;
    private const int SchemaVersion = 2;

    /// <summary>Replaces all stored data, including the Scheduled table, with the workbook contents.</summary>
    public static async Task ImportAsync(string connectionString, Intake intake)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var transaction = await BeginLockedAsync(connection);
        await Execute(connection, transaction, """
            DELETE FROM dispatcher.dispatch_runs;
            DELETE FROM dispatcher.scheduled;
            DELETE FROM dispatcher.completed_work;
            DELETE FROM dispatcher.tasks;
            DELETE FROM dispatcher.employee_skills;
            DELETE FROM dispatcher.customers;
            DELETE FROM dispatcher.employees;
            DELETE FROM dispatcher.skills;
            """);

        foreach (var employee in intake.Employees)
            await Execute(connection, transaction,
                "INSERT INTO dispatcher.employees VALUES (@id, @name, @rating, @completion)",
                ("id", employee.Id), ("name", employee.Name), ("rating", employee.Rating),
                ("completion", employee.CompletionPercent));
        foreach (var customer in intake.Customers)
            await Execute(connection, transaction, "INSERT INTO dispatcher.customers VALUES (@id, @name)",
                ("id", customer.Id), ("name", customer.Name));
        var skills = intake.Skills.Select(s => s.Skill).Concat(intake.Jobs.Select(j => j.Skill))
            .Concat(intake.CompletedWork.Select(w => w.Skill)).Distinct(StringComparer.Ordinal);
        foreach (var skill in skills)
            await Execute(connection, transaction, "INSERT INTO dispatcher.skills VALUES (@skill)", ("skill", skill));
        foreach (var skill in intake.Skills)
            await Execute(connection, transaction, "INSERT INTO dispatcher.employee_skills VALUES (@employee, @skill)",
                ("employee", skill.EmployeeId), ("skill", skill.Skill));
        foreach (var job in intake.Jobs)
            await Execute(connection, transaction, """
                INSERT INTO dispatcher.tasks VALUES (@id, @customer, @description, @skill, @start, @end, @crew, @priority)
                """, ("id", job.Id), ("customer", job.CustomerId), ("description", job.Description),
                ("skill", job.Skill), ("start", job.Start), ("end", job.End), ("crew", job.CrewSize),
                ("priority", job.Priority));
        foreach (var work in intake.CompletedWork)
            await Execute(connection, transaction,
                "INSERT INTO dispatcher.completed_work (employee_id, skill) VALUES (@employee, @skill)",
                ("employee", work.EmployeeId), ("skill", work.Skill));
        await InsertScheduledAsync(connection, transaction, intake.Scheduled);
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Reads all data, including existing bookings in the Scheduled table, from the database, assigns crews
    /// to the tasks that are not scheduled yet, adds those bookings to the Scheduled table, and returns the
    /// schedule as read back from the database. Existing bookings are never changed. Runs in one transaction.
    /// </summary>
    public static async Task<DispatchSnapshot> DispatchAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var transaction = await BeginLockedAsync(connection);
        var intake = await ReadIntakeAsync(connection, transaction);
        var assignments = Scheduler.Schedule(intake);
        await InsertScheduledAsync(connection, transaction, Scheduler.NewBookings(intake, assignments));
        await Execute(connection, transaction,
            "DELETE FROM dispatcher.dispatch_runs; INSERT INTO dispatcher.dispatch_runs VALUES (localtimestamp);");

        var snapshot = await ReadSnapshotAsync(connection, transaction);
        await transaction.CommitAsync();
        return snapshot;
    }

    /// <summary>Reads the stored data and schedule.</summary>
    public static async Task<DispatchSnapshot> LoadAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var snapshot = await ReadSnapshotAsync(connection, transaction);
        await transaction.CommitAsync();
        return snapshot;
    }

    /// <summary>Reads the stored data, including the Scheduled table.</summary>
    public static async Task<Intake> LoadIntakeAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var intake = await ReadIntakeAsync(connection, transaction);
        await transaction.CommitAsync();
        return intake;
    }

    private static async Task InsertScheduledAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IEnumerable<ScheduledWork> bookings)
    {
        foreach (var booking in bookings)
            await Execute(connection, transaction,
                "INSERT INTO dispatcher.scheduled VALUES (@task, @employee, @start, @end)",
                ("task", booking.TaskId), ("employee", booking.EmployeeId), ("start", booking.Start),
                ("end", booking.End));
    }

    private static async Task<Intake> ReadIntakeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        // COLLATE "C" gives ordinal ordering, matching the scheduler's tie-breaks.
        var employees = await Query(connection, transaction,
            "SELECT id, name, rating, completion_percent FROM dispatcher.employees ORDER BY id COLLATE \"C\"",
            r => new Employee(r.GetString(0), r.GetString(1), r.GetInt16(2), r.GetDecimal(3)));
        var skills = await Query(connection, transaction,
            "SELECT employee_id, skill FROM dispatcher.employee_skills ORDER BY employee_id COLLATE \"C\", skill COLLATE \"C\"",
            r => new EmployeeSkill(r.GetString(0), r.GetString(1)));
        var customers = await Query(connection, transaction,
            "SELECT id, name FROM dispatcher.customers ORDER BY id COLLATE \"C\"",
            r => new Customer(r.GetString(0), r.GetString(1)));
        var jobs = await Query(connection, transaction, """
            SELECT id, customer_id, description, skill, starts_at, ends_at, crew_size, priority
            FROM dispatcher.tasks ORDER BY starts_at, id COLLATE "C"
            """,
            r => new Job(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Unspecified(r.GetDateTime(4)), Unspecified(r.GetDateTime(5)), r.GetInt32(6), r.GetInt16(7)));
        var completed = await Query(connection, transaction,
            "SELECT employee_id, skill FROM dispatcher.completed_work ORDER BY id",
            r => new CompletedWork(r.GetString(0), r.GetString(1)));
        var scheduled = await Query(connection, transaction, """
            SELECT task_id, employee_id, scheduled_start_date, scheduled_end_date FROM dispatcher.scheduled
            ORDER BY task_id COLLATE "C", employee_id COLLATE "C"
            """,
            r => new ScheduledWork(r.GetString(0), r.GetString(1), Unspecified(r.GetDateTime(2)),
                Unspecified(r.GetDateTime(3))));
        return new Intake(employees, skills, customers, jobs, completed, scheduled);
    }

    private static async Task<DispatchSnapshot> ReadSnapshotAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var intake = await ReadIntakeAsync(connection, transaction);
        var dispatchedAt = (await Query(connection, transaction,
            "SELECT max(ran_at) FROM dispatcher.dispatch_runs",
            r => r.IsDBNull(0) ? (DateTime?)null : r.GetDateTime(0))).Single();

        var employees = intake.Employees.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var crews = intake.Scheduled.ToLookup(s => s.TaskId, s => employees[s.EmployeeId], StringComparer.Ordinal);
        var assignments = intake.Jobs.Select(job => new Assignment(job, crews[job.Id].ToList())).ToList();
        return new DispatchSnapshot(intake, assignments, dispatchedAt);
    }

    private static DateTime Unspecified(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Unspecified);

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await BeginLockedAsync(connection);
        await EnsureSchemaAsync(connection, transaction);
        await transaction.CommitAsync();
        return connection;
    }

    private static async Task<NpgsqlTransaction> BeginLockedAsync(NpgsqlConnection connection)
    {
        var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction, $"SELECT pg_advisory_xact_lock({LockKey})");
        return transaction;
    }

    private static async Task EnsureSchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        // Development-stage migration: a database from an older version of the app (no version row, or a
        // lower version) is dropped and recreated. Re-import the workbook afterwards.
        await Execute(connection, transaction, """
            CREATE SCHEMA IF NOT EXISTS dispatcher;
            CREATE TABLE IF NOT EXISTS dispatcher.schema_version (version integer NOT NULL);
            """);
        var version = (await Query(connection, transaction,
            "SELECT max(version) FROM dispatcher.schema_version", r => r.IsDBNull(0) ? 0 : r.GetInt32(0))).Single();
        if (version == SchemaVersion)
            return;
        await Execute(connection, transaction, $"""
            DROP SCHEMA dispatcher CASCADE;
            CREATE SCHEMA dispatcher;
            CREATE TABLE dispatcher.schema_version (version integer NOT NULL);
            INSERT INTO dispatcher.schema_version VALUES ({SchemaVersion});
            CREATE TABLE dispatcher.employees (
                id text PRIMARY KEY, name text NOT NULL,
                rating smallint NOT NULL CHECK (rating BETWEEN 1 AND 5),
                completion_percent numeric(5,2) NOT NULL CHECK (completion_percent BETWEEN 0 AND 100));
            CREATE TABLE dispatcher.skills (name text PRIMARY KEY);
            CREATE TABLE dispatcher.employee_skills (
                employee_id text REFERENCES dispatcher.employees(id), skill text REFERENCES dispatcher.skills(name),
                PRIMARY KEY (employee_id, skill));
            CREATE TABLE dispatcher.customers (id text PRIMARY KEY, name text NOT NULL);
            CREATE TABLE dispatcher.tasks (
                id text PRIMARY KEY, customer_id text NOT NULL REFERENCES dispatcher.customers(id),
                description text NOT NULL, skill text NOT NULL REFERENCES dispatcher.skills(name),
                starts_at timestamp NOT NULL, ends_at timestamp NOT NULL,
                crew_size integer NOT NULL CHECK (crew_size > 0),
                priority smallint NOT NULL CHECK (priority BETWEEN 1 AND 5),
                CHECK (ends_at > starts_at));
            CREATE TABLE dispatcher.completed_work (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                employee_id text NOT NULL REFERENCES dispatcher.employees(id),
                skill text NOT NULL REFERENCES dispatcher.skills(name));
            CREATE TABLE dispatcher.scheduled (
                task_id text REFERENCES dispatcher.tasks(id),
                employee_id text REFERENCES dispatcher.employees(id),
                scheduled_start_date timestamp NOT NULL,
                scheduled_end_date timestamp NOT NULL,
                PRIMARY KEY (task_id, employee_id),
                CHECK (scheduled_end_date > scheduled_start_date));
            CREATE TABLE dispatcher.dispatch_runs (ran_at timestamp NOT NULL);
            """);
    }

    private static async Task<List<T>> Query<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, Func<NpgsqlDataReader, T> map)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
            rows.Add(map(reader));
        return rows;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
