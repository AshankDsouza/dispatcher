using System.Globalization;
using ClosedXML.Excel;

namespace Dispatcher.Core;

public static class Workbooks
{
    private static readonly (string Name, string[] Headers)[] Sheets =
    [
        ("Employees", ["Id", "Name", "Rating", "CompletionPercent"]),
        ("Skills", ["EmployeeId", "Skill"]),
        ("Customers", ["Id", "Name"]),
        ("Tasks", ["Id", "CustomerId", "Description", "Skill", "Start", "End", "CrewSize", "Priority"]),
        ("CompletedWork", ["EmployeeId", "Skill"]),
        ("Scheduled", ["TaskId", "EmployeeId", "ScheduledStart", "ScheduledEnd"])
    ];

    public static void CreateTemplate(string path) =>
        WriteIntake(path, new Intake([], [], [], [], [], []));

    /// <summary>Writes an intake workbook in the format <see cref="Read"/> accepts.</summary>
    public static void WriteIntake(string path, Intake intake)
    {
        using var workbook = new XLWorkbook();
        var rows = new Dictionary<string, IEnumerable<XLCellValue[]>>
        {
            ["Employees"] = intake.Employees.Select(e => new XLCellValue[]
                { e.Id, e.Name, e.Rating, e.CompletionPercent }),
            ["Skills"] = intake.Skills.Select(s => new XLCellValue[] { s.EmployeeId, s.Skill }),
            ["Customers"] = intake.Customers.Select(c => new XLCellValue[] { c.Id, c.Name }),
            ["Tasks"] = intake.Jobs.Select(j => new XLCellValue[]
                { j.Id, j.CustomerId, j.Description, j.Skill, j.Start, j.End, j.CrewSize, j.Priority }),
            ["CompletedWork"] = intake.CompletedWork.Select(w => new XLCellValue[] { w.EmployeeId, w.Skill }),
            ["Scheduled"] = intake.Scheduled.Select(b => new XLCellValue[] { b.TaskId, b.EmployeeId, b.Start, b.End }),
        };
        foreach (var (name, headers) in Sheets)
        {
            var sheet = workbook.AddWorksheet(name);
            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Row(1).Style.Font.Bold = true;
            var r = 2;
            foreach (var values in rows[name])
            {
                for (var i = 0; i < values.Length; i++)
                    sheet.Cell(r, i + 1).Value = values[i];
                r++;
            }
            if (name == "Tasks")
                sheet.Columns(5, 6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            if (name == "Scheduled")
                sheet.Columns(3, 4).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            sheet.Columns(1, headers.Length).Width = 18;
            if (name is "Employees" or "Customers")
                sheet.Column(2).Width = 30;
            if (name == "Tasks")
                sheet.Column(3).Width = 40;
        }
        workbook.SaveAs(path);
        workbook.Dispose();
        XlsxCompat.Normalize(path);
    }

    public static Intake Read(string path)
    {
        using var workbook = new XLWorkbook(path);
        foreach (var (name, headers) in Sheets)
        {
            if (!workbook.TryGetWorksheet(name, out var sheet))
                throw new FormatException($"Missing worksheet '{name}'.");
            for (var i = 0; i < headers.Length; i++)
                if (sheet.Cell(1, i + 1).GetString().Trim() != headers[i])
                    throw new FormatException($"{name}: column {i + 1} must be '{headers[i]}'.");
        }

        var employees = Rows(workbook, "Employees", 4)
            .Select(r => new Employee(Text(r, 1), Text(r, 2), IntIn(r, 3, 1, 5), Percent(r, 4))).ToList();
        var skills = Rows(workbook, "Skills", 2)
            .Select(r => new EmployeeSkill(Text(r, 1), Text(r, 2))).ToList();
        var customers = Rows(workbook, "Customers", 2)
            .Select(r => new Customer(Text(r, 1), Text(r, 2))).ToList();
        var jobs = Rows(workbook, "Tasks", 8)
            .Select(r => new Job(Text(r, 1), Text(r, 2), Text(r, 3), Text(r, 4),
                Date(r, 5), Date(r, 6), PositiveInt(r, 7), IntIn(r, 8, 1, 5))).ToList();
        var completed = Rows(workbook, "CompletedWork", 2)
            .Select(r => new CompletedWork(Text(r, 1), Text(r, 2))).ToList();
        var scheduled = Rows(workbook, "Scheduled", 4)
            .Select(r => new ScheduledWork(Text(r, 1), Text(r, 2), Date(r, 3), Date(r, 4))).ToList();

        Unique(employees.Select(e => e.Id), "employee ID");
        Unique(customers.Select(c => c.Id), "customer ID");
        Unique(jobs.Select(j => j.Id), "task ID");
        if (skills.Distinct().Count() != skills.Count)
            throw new FormatException("Skills contains a duplicate employee/skill pair.");
        var employeeIds = employees.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var customerIds = customers.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var skill in skills)
            if (!employeeIds.Contains(skill.EmployeeId))
                throw new FormatException($"Skills references unknown employee '{skill.EmployeeId}'.");
        foreach (var work in completed)
            if (!employeeIds.Contains(work.EmployeeId))
                throw new FormatException($"CompletedWork references unknown employee '{work.EmployeeId}'.");
        foreach (var job in jobs)
        {
            if (!customerIds.Contains(job.CustomerId))
                throw new FormatException($"Task '{job.Id}' references unknown customer '{job.CustomerId}'.");
            if (job.End <= job.Start)
                throw new FormatException($"Task '{job.Id}' must end after it starts.");
        }
        var jobsById = jobs.ToDictionary(j => j.Id, StringComparer.Ordinal);
        var skillSet = skills.ToHashSet();
        Unique(scheduled.Select(b => $"{b.TaskId}/{b.EmployeeId}"), "Scheduled task/employee pair");
        foreach (var booking in scheduled)
        {
            var where = $"Scheduled task '{booking.TaskId}', employee '{booking.EmployeeId}'";
            if (!jobsById.TryGetValue(booking.TaskId, out var job))
                throw new FormatException($"{where}: unknown task.");
            if (!employeeIds.Contains(booking.EmployeeId))
                throw new FormatException($"{where}: unknown employee.");
            if (!skillSet.Contains(new EmployeeSkill(booking.EmployeeId, job.Skill)))
                throw new FormatException($"{where}: employee does not have skill '{job.Skill}'.");
            if (booking.End <= booking.Start)
                throw new FormatException($"{where}: must end after it starts.");
        }
        foreach (var bookings in scheduled.GroupBy(b => b.EmployeeId))
        {
            var ordered = bookings.OrderBy(b => b.Start).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (ordered[i].Start < ordered[i - 1].End)
                    throw new FormatException($"Scheduled: employee '{bookings.Key}' is booked on tasks " +
                        $"'{ordered[i - 1].TaskId}' and '{ordered[i].TaskId}' at overlapping times.");
        }
        return new Intake(employees, skills, customers, jobs, completed, scheduled);
    }

    public static void WriteSchedule(string path, Intake intake, IReadOnlyList<Assignment> assignments)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Schedule");
        string[] headers = ["TaskId", "CustomerId", "Customer", "Description", "Skill", "Start", "End",
            "CrewSize", "Priority", "Status", "EmployeeIds", "Employees"];
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        var customers = intake.Customers.ToDictionary(c => c.Id, StringComparer.Ordinal);
        for (var i = 0; i < assignments.Count; i++)
        {
            var (job, crew) = assignments[i];
            var row = sheet.Row(i + 2);
            row.Cell(1).Value = job.Id;
            row.Cell(2).Value = job.CustomerId;
            row.Cell(3).Value = customers[job.CustomerId].Name;
            row.Cell(4).Value = job.Description;
            row.Cell(5).Value = job.Skill;
            row.Cell(6).Value = job.Start;
            row.Cell(7).Value = job.End;
            row.Cell(8).Value = job.CrewSize;
            row.Cell(9).Value = job.Priority;
            row.Cell(10).Value = crew.Count == 0 ? "Unscheduled" : "Scheduled";
            row.Cell(11).Value = string.Join(", ", crew.Select(e => e.Id));
            row.Cell(12).Value = string.Join(", ", crew.Select(e => e.Name));
        }
        sheet.Columns(6, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss.000";
        workbook.SaveAs(path);
        workbook.Dispose();
        XlsxCompat.Normalize(path);
    }

    private static IEnumerable<IXLRow> Rows(XLWorkbook workbook, string name, int columns)
    {
        var sheet = workbook.Worksheet(name);
        var rows = sheet.CellsUsed(XLCellsUsedOptions.Contents)
            .Where(cell => cell.Address.RowNumber > 1 && cell.Address.ColumnNumber <= columns)
            .Select(cell => cell.Address.RowNumber).Distinct().Order();
        foreach (var number in rows)
        {
            var row = sheet.Row(number);
            yield return row;
        }
    }

    private static string Text(IXLRow row, int column)
    {
        var cell = row.Cell(column);
        if (cell.HasFormula)
            throw Error(cell, "formulas are not supported");
        var value = cell.GetString().Trim();
        if (value.Length == 0)
            throw Error(cell, "value is required");
        return value;
    }

    private static DateTime Date(IXLRow row, int column)
    {
        var cell = row.Cell(column);
        if (cell.HasFormula)
            throw Error(cell, "formulas are not supported");
        if (cell.DataType == XLDataType.DateTime)
            return DateTime.SpecifyKind(cell.GetDateTime(), DateTimeKind.Unspecified);
        if (DateTime.TryParseExact(cell.GetString().Trim(), ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            return value;
        throw Error(cell, "use an Excel date/time or yyyy-MM-dd HH:mm");
    }

    private static int PositiveInt(IXLRow row, int column)
    {
        var value = Text(row, column);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
            throw Error(row.Cell(column), "must be a positive whole number");
        return number;
    }

    private static int IntIn(IXLRow row, int column, int min, int max)
    {
        var value = Text(row, column);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
            number < min || number > max)
            throw Error(row.Cell(column), $"must be a whole number from {min} to {max}");
        return number;
    }

    /// <summary>0–100. A cell formatted as a percentage in Excel (e.g. 92% stored as 0.92) is scaled to 0–100.</summary>
    private static decimal Percent(IXLRow row, int column)
    {
        var cell = row.Cell(column);
        var text = Text(row, column);
        decimal value;
        if (cell.DataType == XLDataType.Number)
        {
            value = (decimal)cell.GetDouble();
            if (cell.Style.NumberFormat.Format.Contains('%') || cell.Style.NumberFormat.NumberFormatId is 9 or 10)
                value *= 100;
        }
        else if (!decimal.TryParse(text.TrimEnd('%').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            throw Error(cell, "must be a number from 0 to 100");
        value = Math.Round(value, 2);
        if (value is < 0 or > 100)
            throw Error(cell, "must be a number from 0 to 100");
        return value;
    }

    private static FormatException Error(IXLCell cell, string message) =>
        new($"{cell.Worksheet.Name} row {cell.Address.RowNumber}, column {cell.Address.ColumnNumber}: {message}.");

    private static void Unique(IEnumerable<string> values, string description)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
            if (!seen.Add(value))
                throw new FormatException($"Duplicate {description} '{value}'.");
    }
}
