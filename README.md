# Dispatcher

A desktop application for importing an Excel staffing snapshot into PostgreSQL,
assigning crews without AI, and exporting a schedule as an `.xlsx` file. It is built
with [Avalonia](https://avaloniaui.net/) and runs on macOS and Windows from the same code.

> **Local development only:** the Compose PostgreSQL uses `trust` authentication
> (no password) and listens only on `127.0.0.1`. Do not expose it to a network.

## Run the desktop app

1. Install the .NET 8 SDK and Docker Desktop.
2. Start the database from the repository directory: `docker compose up -d db`.
3. Open `Dispatcher.sln` in JetBrains Rider (install the *AvaloniaRider* plugin for the XAML
   previewer), choose the **Dispatcher.Desktop** run configuration and press Run.
   From a terminal: `dotnet run --project Dispatcher.Desktop`.
4. Click **Browse…** next to *Intake workbook*, pick `examples/mock_data.xlsx`, then:
   1. **Import into database** – loads the workbook into PostgreSQL (replacing what was there).
   2. **Run dispatch** – reads the data from the database, assigns crews to the tasks that are not
      booked yet and adds them to the `scheduled` table.
   3. **Export schedule…** – saves the stored schedule as an `.xlsx` file.

The database is the source of truth: dispatch reads its input from the database, not from the
workbook, and the table in the window always shows what is stored. The window loads the stored
schedule when it opens, so you can edit data directly in PostgreSQL and click **Run dispatch**
again without re-importing.

The connection defaults to `Host=localhost;Database=dispatcher;Username=dispatcher`; edit it in
the window or set `DISPATCHER_CONNECTION_STRING`. Any PostgreSQL database works if its user can
create a schema and tables. On Windows, use `Host=127.0.0.1` if `localhost` fails to connect.

To build a standalone Windows executable (no .NET install needed on the target machine):

```sh
dotnet publish Dispatcher.Desktop -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64
```

Use `-r osx-arm64` for Apple Silicon Macs. The executable is not code-signed, so Windows
SmartScreen asks for confirmation on first launch (**More info → Run anyway**).

## Database

PostgreSQL persists in the `dispatcher_db` Docker volume. Set `DISPATCHER_DB_PORT` to change
the host port. Run `docker compose down` to stop it; `docker compose down -v` **deletes its
data**. Authentication is chosen when the volume is first created, so delete the volume if you
later switch to password authentication.

## Command-line scheduler

The same engine is available as a CLI. The Compose container imports, dispatches and exports
`examples/mock_data.xlsx` (this replaces the data in the Compose database):

```sh
mkdir -p output
docker compose run --build --rm scheduler
```

Open `output/schedule.xlsx` to see the results: all 7 tasks scheduled. To try your own
workbook, place it in `examples` and provide container paths:

```sh
docker compose run --rm scheduler /input/my-intake.xlsx /output/my-schedule.xlsx
```

On Linux, also set `export DISPATCHER_UID=$(id -u) DISPATCHER_GID=$(id -g)` so output
files belong to your user.

Locally (`dotnet run --project Dispatcher.Cli -- …`):

| Arguments | Does |
| --- | --- |
| `<input.xlsx> <output.xlsx>` | Import, dispatch and export. |
| `--import <input.xlsx>` | Import only; dispatch later from the desktop app. |
| `--write-sample <path.xlsx> [yyyy-MM-dd]` | Write the built-in mock data (`Dispatcher.Core/SampleData.cs`) dated from the given Monday (default: next Monday). `examples/mock_data.xlsx` was made with `--write-sample examples/mock_data.xlsx 2026-11-30`. |

## Intake and scheduling

Click **Create intake template…**, fill in the sheets below, then choose that file as the
intake workbook and click **Import into database** and **Run dispatch**. Use the exact sheet and
column names and order in the template. IDs and skills are case-sensitive; IDs must
be unique within their sheet. All fields are required, blank rows are ignored, and
formulas are not supported. Enter IDs as text to preserve leading zeroes.
To try the application without preparing data, use `examples/mock_data.xlsx` (or click
**Save sample data…** for a copy dated next week): it contains 12 technicians with ratings,
completion percentages, skills and job history, 7 customers and 7 tasks. Tasks T001–T004 are
already booked in the Scheduled sheet; T005–T007 are left for dispatch, which has to work around
the booked employees. Use it as-is or edit it.

| Sheet | Columns | Meaning |
| --- | --- | --- |
| Employees | Id, Name, Rating, CompletionPercent | One row per employee. Rating is a whole number 1–5; CompletionPercent is 0–100 (a cell formatted as a percentage, e.g. 92%, also works). |
| Skills | EmployeeId, Skill | One row per employee/skill pair; no duplicate pairs. |
| Customers | Id, Name | One row per customer. |
| Tasks | Id, CustomerId, Description, Skill, Start, End, CrewSize, Priority | One job per row. CustomerId must exist; CrewSize is a positive integer; Priority is a whole number 1 (lowest) to 5 (highest). |
| CompletedWork | EmployeeId, Skill | One row per completed job by that employee in that skill. Repeated rows count as separate jobs. |
| Scheduled | TaskId, EmployeeId, ScheduledStart, ScheduledEnd | Optional. One row per employee already booked on a task. The task and employee must exist, the employee must have the task's skill, and an employee's bookings must not overlap. |

Employee references must exist. Start and End must be Excel date/time cells or text
in `yyyy-MM-dd HH:mm` (optionally with seconds); End must be later than Start.
Times are local, timezone-free values; use the same local time basis for every row.
Tasks that already have rows in the Scheduled table are kept exactly as booked, and their
employees count as busy during the booked times. Every other task is dispatched in order of
priority (highest first), then start time, then task ID (ordinal order). Each crew member must
have the task's skill and no overlapping booking; adjacent intervals are allowed. Eligible
employees are ranked by rating, then completion percentage, then the number of completed jobs
in that skill (all highest first), then employee ID (ordinal order). If there are not enough
eligible employees, the task is marked **Unscheduled**, and no partial crew is reserved. Running
dispatch again retries only the tasks that are still unscheduled. The output contains all
tasks, their priority and status, and assigned employee IDs and names.

**Important:** each import is a complete replacement snapshot, not an incremental
update. The application creates the `dispatcher` schema if needed and replaces its data
(employees, skills, employee skills, customers, tasks, completed work and scheduled) in a
single transaction. Include all current data, including existing bookings, in each intake. An
unsuccessful import leaves the previous data intact. Dispatch reads the stored data, adds
bookings for newly scheduled tasks to the `scheduled` table (existing rows are never changed)
and records the run in `dispatch_runs`, all in one transaction. If the database was created by
an older version of the app, the `dispatcher` schema is dropped and recreated on first use;
re-import your workbook afterwards. Export reads the stored schedule, so it can be
repeated at any time.

## Verification

Run `dotnet test Dispatcher.Tests/Dispatcher.Tests.csproj`. PostgreSQL integration
tests are skipped unless `DISPATCHER_TEST_DATABASE` is set to a connection string
for a **disposable** database; they replace its `dispatcher` schema data. With the Compose
database, create one once and use it:

```sh
psql -h 127.0.0.1 -U dispatcher -d dispatcher -c "CREATE DATABASE dispatcher_test"
DISPATCHER_TEST_DATABASE="Host=localhost;Database=dispatcher_test;Username=dispatcher" \
  dotnet test Dispatcher.Tests
```