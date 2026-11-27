# Dispatcher

A small web application for importing an Excel staffing snapshot into PostgreSQL,
assigning crews without AI, and exporting a schedule as an `.xlsx` file. It runs
on macOS, Linux and Windows.

> **Local development only:** the Compose PostgreSQL uses `trust` authentication
> (no password) and listens only on `127.0.0.1`. Do not expose it to a network.

## Run with Docker

Install Docker Desktop (or Docker Engine with Compose). From the repository directory:

```sh
docker compose up -d --build web
```

Open <http://localhost:5080>. Compose starts PostgreSQL first and waits for it to
be ready. Set `DISPATCHER_WEB_PORT` or `DISPATCHER_DB_PORT` to change the host ports.
PostgreSQL persists in the `dispatcher_db` Docker volume. Run `docker compose down`
to stop everything; `docker compose down -v` **deletes its data**. Authentication
is chosen when the volume is first created, so delete the volume if you later
switch to password authentication.

### Command-line scheduler

The same engine is available as a CLI container that processes the included example:

```sh
mkdir -p output
docker compose run --build --rm scheduler
```

Open `output/schedule.xlsx` to see the results. The included `examples/example.xlsx`
has four jobs: T001 is assigned to E001, T002 is unscheduled due to overlap, T003 is
assigned to E003, and T004 is assigned to E001 and E002 when they are both available.
To try your own workbook, place it in `examples` and provide container paths:

```sh
docker compose run --rm scheduler /input/my-intake.xlsx /output/my-schedule.xlsx
```

On Linux, also set `export DISPATCHER_UID=$(id -u) DISPATCHER_GID=$(id -g)` so output
files belong to your user.

## Desktop app (macOS and Windows)

`Dispatcher.Desktop` is a cross-platform desktop app built with
[Avalonia](https://avaloniaui.net/). It runs on macOS and Windows from the same code and shows
the resulting schedule in a table as well as saving `schedule.xlsx`.

1. Install the .NET 8 SDK and start the database: `docker compose up -d db`.
2. Open `Dispatcher.sln` in JetBrains Rider (install the *AvaloniaRider* plugin for the XAML
   previewer), choose the **Dispatcher.Desktop** run configuration and press Run.
   From a terminal: `dotnet run --project Dispatcher.Desktop`.
3. Click **Browse…** next to *Input workbook* and pick `examples/mock_data.xlsx` (or click
   **Save sample data…**), then click **Import and schedule**.

The connection defaults to `Host=localhost;Database=dispatcher;Username=dispatcher`; edit it in
the window or set `DISPATCHER_CONNECTION_STRING`.

## Run without Docker for the app

Install the .NET 8 SDK, start the database with `docker compose up -d db` (or use any
PostgreSQL database whose user can create a schema and tables), then run:

```sh
dotnet run --project Dispatcher.Web
```

and open <http://localhost:5080>. The app connects to
`Host=localhost;Database=dispatcher;Username=dispatcher` by default; set
`DISPATCHER_CONNECTION_STRING` to use a different Npgsql connection string.

## Intake and scheduling

Click **Download template**, fill in the sheets below, then choose that file and click
**Import and schedule**; the schedule downloads as `schedule.xlsx`. Use the exact sheet and
column names and order in the template. IDs and skills are case-sensitive; IDs must
be unique within their sheet. All fields are required, blank rows are ignored, and
formulas are not supported. Enter IDs as text to preserve leading zeroes.
To try the application without preparing data, click **Download sample data**: it
contains 12 technicians with their skills and job history, 8 customers and 25 jobs
during the coming work week (two jobs are deliberately overbooked and stay
unscheduled). Upload it as-is or edit it. `examples/example.xlsx` is a smaller
four-job example.

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
Export occurs after the database commit; if the download fails, rerun the same
intake to regenerate it. Do not edit the database
tables concurrently with an import.

## Verification

Run `dotnet test Dispatcher.Tests/Dispatcher.Tests.csproj`. PostgreSQL integration
tests are skipped unless `DISPATCHER_TEST_DATABASE` is set to a connection string
for a **disposable** database; they replace its `dispatcher` schema data.