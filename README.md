# Dispatcher

A Windows Forms application for importing an Excel staffing snapshot into PostgreSQL,
assigning crews without AI, and exporting a schedule as an `.xlsx` file.

## Setup

Install the .NET 8 SDK on Windows and create an empty PostgreSQL database. The database
user must be able to create a schema and tables and read and write them. Run:

```powershell
dotnet run --project Dispatcher/Dispatcher.csproj
```

Enter an Npgsql connection string in the application, for example
`Host=localhost;Database=dispatcher;Username=dispatcher` (add a password if required).
Alternatively, set `DISPATCHER_CONNECTION_STRING` before launching. The connection
string is masked in the UI and is not saved by the application. Use appropriate
PostgreSQL access controls and TLS for remote connections.

## Intake and scheduling

Click **Create intake template**, fill in the sheets below, then select that file and
a different output file and click **Import and schedule**. Use the exact sheet and
column names and order in the template. IDs and skills are case-sensitive; IDs must
be unique within their sheet. All fields are required, blank rows are ignored, and
formulas are not supported. Enter IDs as text to preserve leading zeroes.

| Sheet | Columns | Meaning |
| --- | --- | --- |
| Employees | Id, Name | One row per employee. |
| Skills | EmployeeId, Skill | One row per employee/skill pair; no duplicate pairs. |
| Customers | Id, Name | One row per customer. |
| Tasks | Id, CustomerId, Description, Skill, Start, End, CrewSize | One job per row. CustomerId must exist; CrewSize is a positive integer. |
| CompletedWork | EmployeeId, Skill | One row per completed job by that employee in that skill. Repeated rows count as separate jobs. |

Employee references must exist. Start and End must be Excel date/time cells or text
in `yyyy-MM-dd HH:mm` (optionally with seconds); End must be later than Start.
Times are local, timezone-free values; use the same local time basis for every row.
Each crew member must have the task's skill. Tasks are processed by start time,
then task ID (ordinal order). Eligible employees are ranked by the number of
completed jobs in that skill, descending, then employee ID (ordinal order). An
employee cannot be assigned to overlapping intervals; adjacent intervals are
allowed. If there are not enough eligible employees, the entire task is marked
**Unscheduled**, and no partial crew is reserved. The output contains all tasks,
their status, and assigned employee IDs and names.

**Important:** each import is a complete replacement snapshot, not an incremental
update. The application creates and replaces data in the `dispatcher` schema
(employees, skills, employee skills, customers, tasks, completed work and
assignments) in a single transaction. Include all current data in each intake.
An unsuccessful database transaction leaves the previous snapshot intact.
Export occurs after the database commit; if writing the output fails, correct the
output path and rerun the same intake to regenerate it. Do not edit the database
tables concurrently with an import.

## Verification

Run `dotnet test Dispatcher.Tests/Dispatcher.Tests.csproj`. PostgreSQL integration
tests are skipped unless `DISPATCHER_TEST_DATABASE` is set to a connection string
for a **disposable** database; they replace its `dispatcher` schema data. To build
the Windows project from a non-Windows host, use
`dotnet build Dispatcher/Dispatcher.csproj -p:EnableWindowsTargeting=true`.