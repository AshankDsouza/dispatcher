namespace Dispatcher.Core;

/// <summary>
/// Mock intake data for trying the scheduler: a small field-service company with 7 tasks over two days.
/// Tasks T001–T004 are already booked in the Scheduled table; T005–T007 are left for dispatch.
/// </summary>
public static class SampleData
{
    private static readonly (string Id, string Name, int Rating, decimal Completion, (string Skill, int Completed)[] Skills)[] Staff =
    [
        ("E001", "Maria Lopez",    5, 96, [("HVAC", 14), ("Electrical", 6)]),
        ("E002", "James Carter",   4, 91, [("HVAC", 9)]),
        ("E003", "Aisha Khan",     5, 98, [("Electrical", 18), ("HVAC", 3)]),
        ("E004", "Tom Becker",     3, 82, [("Electrical", 7)]),
        ("E005", "Priya Nair",     5, 97, [("Plumbing", 21), ("Carpentry", 4)]),
        ("E006", "Daniel Kim",     3, 85, [("Plumbing", 8), ("HVAC", 2)]),
        ("E007", "Sofia Rossi",    4, 93, [("Roofing", 16), ("Carpentry", 9)]),
        ("E008", "Marcus Johnson", 4, 88, [("Roofing", 11)]),
        ("E009", "Emily Chen",     5, 95, [("Carpentry", 19), ("Roofing", 5)]),
        ("E010", "Luis Hernandez", 4, 90, [("Plumbing", 12), ("Electrical", 4), ("HVAC", 1)]),
        ("E011", "Hannah Wright",  2, 75, [("Carpentry", 6)]),
        ("E012", "Omar Haddad",    4, 92, [("Electrical", 10), ("Roofing", 3)]),
    ];

    private static readonly (string Id, string Name)[] Clients =
    [
        ("C001", "Riverside Dental Clinic"),
        ("C002", "Oak Street Apartments"),
        ("C003", "Sunrise Bakery"),
        ("C004", "Hillcrest Elementary School"),
        ("C005", "Greenfield Office Park"),
        ("C006", "The Patel Residence"),
        ("C007", "Lakeside Hotel"),
    ];

    // Day is 0 = Monday. Times are local, 24-hour. Crew lists the employees already booked (empty = needs dispatch).
    private static readonly (string Customer, string Description, string Skill, int Day, int StartHour, int Hours,
        int CrewSize, int Priority, string[] Crew)[] Work =
    [
        ("C001", "Replace rooftop AC compressor",   "HVAC",       0,  8, 4, 2, 4, ["E001", "E002"]),
        ("C002", "Fix leaking riser in unit 4B",     "Plumbing",   0,  8, 3, 1, 5, ["E005"]),
        ("C003", "Install dedicated oven circuit",  "Electrical", 0,  9, 3, 1, 2, ["E003"]),
        ("C004", "Patch storm damage on gym roof",  "Roofing",    0, 10, 6, 3, 3, ["E007", "E008", "E009"]),
        // Left for dispatch. The booked employees above are busy at these times, so dispatch must pick others.
        ("C007", "Emergency panel repair, kitchen", "Electrical", 0, 10, 2, 1, 5, []),
        ("C005", "Annual furnace inspection",       "HVAC",       0,  9, 2, 2, 3, []),
        ("C006", "Replace damaged shingles",        "Roofing",    1,  8, 3, 2, 1, []),
    ];

    /// <summary>Builds mock intake data for the work week starting on <paramref name="monday"/>.</summary>
    public static Intake Create(DateTime monday)
    {
        monday = DateTime.SpecifyKind(monday.Date, DateTimeKind.Unspecified);
        var employees = Staff.Select(s => new Employee(s.Id, s.Name, s.Rating, s.Completion)).ToList();
        var skills = Staff.SelectMany(s => s.Skills.Select(k => new EmployeeSkill(s.Id, k.Skill))).ToList();
        var completed = Staff.SelectMany(s => s.Skills.SelectMany(k =>
            Enumerable.Repeat(new CompletedWork(s.Id, k.Skill), k.Completed))).ToList();
        var customers = Clients.Select(c => new Customer(c.Id, c.Name)).ToList();
        var jobs = new List<Job>();
        var scheduled = new List<ScheduledWork>();
        foreach (var (w, i) in Work.Select((w, i) => (w, i)))
        {
            var start = monday.AddDays(w.Day).AddHours(w.StartHour);
            var job = new Job($"T{i + 1:000}", w.Customer, w.Description, w.Skill, start, start.AddHours(w.Hours),
                w.CrewSize, w.Priority);
            jobs.Add(job);
            scheduled.AddRange(w.Crew.Select(e => new ScheduledWork(job.Id, e, job.Start, job.End)));
        }
        return new Intake(employees, skills, customers, jobs, completed, scheduled);
    }

    /// <summary>The Monday after <paramref name="today"/> (or a week later if today is Monday).</summary>
    public static DateTime NextMonday(DateTime today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.Date.AddDays(days == 0 ? 7 : days);
    }
}
