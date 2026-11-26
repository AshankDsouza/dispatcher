using Npgsql;

namespace Dispatcher.Core;

public static class Database
{
    public static async Task SaveAsync(string connectionString, Intake intake, IReadOnlyList<Assignment> assignments)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction, "SELECT pg_advisory_xact_lock(1684632432)");
        await Execute(connection, transaction, """
            CREATE SCHEMA IF NOT EXISTS dispatcher;
            CREATE TABLE IF NOT EXISTS dispatcher.employees (id text PRIMARY KEY, name text NOT NULL);
            CREATE TABLE IF NOT EXISTS dispatcher.skills (name text PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS dispatcher.employee_skills (
                employee_id text REFERENCES dispatcher.employees(id), skill text REFERENCES dispatcher.skills(name),
                PRIMARY KEY (employee_id, skill));
            CREATE TABLE IF NOT EXISTS dispatcher.customers (id text PRIMARY KEY, name text NOT NULL);
            CREATE TABLE IF NOT EXISTS dispatcher.tasks (
                id text PRIMARY KEY, customer_id text NOT NULL REFERENCES dispatcher.customers(id),
                description text NOT NULL, skill text NOT NULL REFERENCES dispatcher.skills(name),
                starts_at timestamp NOT NULL, ends_at timestamp NOT NULL, crew_size integer NOT NULL CHECK (crew_size > 0),
                CHECK (ends_at > starts_at));
            CREATE TABLE IF NOT EXISTS dispatcher.completed_work (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                employee_id text NOT NULL REFERENCES dispatcher.employees(id),
                skill text NOT NULL REFERENCES dispatcher.skills(name));
            CREATE TABLE IF NOT EXISTS dispatcher.assignments (
                task_id text REFERENCES dispatcher.tasks(id), employee_id text REFERENCES dispatcher.employees(id),
                PRIMARY KEY (task_id, employee_id));
            DELETE FROM dispatcher.assignments;
            DELETE FROM dispatcher.completed_work;
            DELETE FROM dispatcher.tasks;
            DELETE FROM dispatcher.employee_skills;
            DELETE FROM dispatcher.customers;
            DELETE FROM dispatcher.employees;
            DELETE FROM dispatcher.skills;
            """);

        foreach (var employee in intake.Employees)
            await Execute(connection, transaction, "INSERT INTO dispatcher.employees VALUES (@id, @name)",
                ("id", employee.Id), ("name", employee.Name));
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
                INSERT INTO dispatcher.tasks VALUES (@id, @customer, @description, @skill, @start, @end, @crew)
                """, ("id", job.Id), ("customer", job.CustomerId), ("description", job.Description),
                ("skill", job.Skill), ("start", job.Start), ("end", job.End), ("crew", job.CrewSize));
        foreach (var work in intake.CompletedWork)
            await Execute(connection, transaction,
                "INSERT INTO dispatcher.completed_work (employee_id, skill) VALUES (@employee, @skill)",
                ("employee", work.EmployeeId), ("skill", work.Skill));
        foreach (var assignment in assignments)
            foreach (var employee in assignment.Crew)
                await Execute(connection, transaction, "INSERT INTO dispatcher.assignments VALUES (@task, @employee)",
                    ("task", assignment.Job.Id), ("employee", employee.Id));
        await transaction.CommitAsync();
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
