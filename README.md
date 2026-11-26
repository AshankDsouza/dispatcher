# Dispatcher

A Windows Forms application for importing an Excel staffing snapshot into PostgreSQL,
assigning crews without AI, and exporting a schedule as an `.xlsx` file.

## Run with Docker

Install Docker Desktop (or Docker Engine with Compose) and use Linux containers.
The Docker scheduler is a command-line version of the same scheduling engine;
it does not run the Windows Forms UI. From the repository directory, set a
local PostgreSQL password and run the included example:

```powershell
$env:POSTGRES_PASSWORD = Read-Host "Choose a local PostgreSQL password"
New-Item -ItemType Directory -Force output
docker compose run --build --rm scheduler
```

In a POSIX shell, use `read -rs POSTGRES_PASSWORD; export POSTGRES_PASSWORD`
and `mkdir -p output` before the `docker compose run` command instead. On Linux,
also set `export DISPATCHER_UID=$(id -u) DISPATCHER_GID=$(id -g)` so output files
belong to your user; otherwise the scheduler container runs as root. Compose
starts PostgreSQL and waits for it to be ready. Open `output/schedule.xlsx` to
see the results. The included `examples/example.xlsx` has four jobs: T001 is
assigned to E001, T002 is unscheduled due to overlap, T003 is assigned to E003,
and T004 is assigned to E001 and E002 when they are both available.

To try your own workbook, place it in `examples` and provide container paths:

```powershell
docker compose run --rm scheduler /input/my-intake.xlsx /output/my-schedule.xlsx
```

Input is mounted read-only and `output` is writable. Each run replaces the database
snapshot; use only a test database and sample data until you are ready to import
your own data. PostgreSQL persists in the `dispatcher_db` Docker volume. Run
`docker compose down` to stop it; `docker compose down -v` **deletes its data**.
The password is used when the database volume is first created. Changing it later
requires changing the database password as well, or deleting the volume and starting
over. Set `DISPATCHER_DB_PORT` to change the host port if 5432 is already in use.
Keep the password in your environment, not in source control. This Compose setup is
for local use, not a production deployment.

## Windows desktop setup

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
To use the Compose database instead, set `POSTGRES_PASSWORD` as above, run
`docker compose up -d db`, and connect the UI to `Host=localhost;Port=5432;
Database=dispatcher;Username=dispatcher` with the chosen password (use the
configured host port if changed). The desktop application still runs on Windows,
outside the container.

## Intake and scheduling

Click **Create intake template**, fill in the sheets below, then select that file and
a different output file and click **Import and schedule**. Use the exact sheet and
column names and order in the template. IDs and skills are case-sensitive; IDs must
be unique within their sheet. All fields are required, blank rows are ignored, and
formulas are not supported. Enter IDs as text to preserve leading zeroes.
Alternatively, open `examples/example.xlsx` as a starting point or use it directly
to try the application.

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