namespace Dispatcher.Core;

/// <summary>Mock intake data for trying the scheduler: a small field-service company over one work week.</summary>
public static class SampleData
{
    private static readonly (string Id, string Name, (string Skill, int Completed)[] Skills)[] Staff =
    [
        ("E001", "Maria Lopez",     [("HVAC", 14), ("Electrical", 6)]),
        ("E002", "James Carter",    [("HVAC", 9)]),
        ("E003", "Aisha Khan",      [("Electrical", 18), ("HVAC", 3)]),
        ("E004", "Tom Becker",      [("Electrical", 7)]),
        ("E005", "Priya Nair",      [("Plumbing", 21), ("Carpentry", 4)]),
        ("E006", "Daniel Kim",      [("Plumbing", 8), ("HVAC", 2)]),
        ("E007", "Sofia Rossi",     [("Roofing", 16), ("Carpentry", 9)]),
        ("E008", "Marcus Johnson",  [("Roofing", 11)]),
        ("E009", "Emily Chen",      [("Carpentry", 19), ("Roofing", 5)]),
        ("E010", "Luis Hernandez",  [("Plumbing", 12), ("Electrical", 4), ("HVAC", 1)]),
        ("E011", "Hannah Wright",   [("Carpentry", 6)]),
        ("E012", "Omar Haddad",     [("Electrical", 10), ("Roofing", 3)]),
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
        ("C008", "Northgate Auto Repair"),
    ];

    // Day is 0 = Monday. Times are local, 24-hour.
    private static readonly (string Customer, string Description, string Skill, int Day, int StartHour, int Hours, int Crew)[] Work =
    [
        ("C001", "Replace rooftop AC compressor",        "HVAC",       0,  8, 4, 2),
        ("C002", "Fix leaking riser in unit 4B",          "Plumbing",   0,  8, 3, 1),
        ("C003", "Install dedicated oven circuit",       "Electrical", 0,  9, 3, 1),
        ("C004", "Patch storm damage on gym roof",       "Roofing",    0, 10, 6, 3),
        ("C005", "Annual furnace inspection, bldg A",    "HVAC",       0, 13, 3, 1),
        ("C006", "Build custom pantry shelving",          "Carpentry",  0, 13, 4, 1),
        ("C007", "Tear-off and reroof east wing",        "Roofing",    0, 13, 4, 3), // overlaps the gym roof: not enough roofers
        ("C008", "Rewire service bay lighting",          "Electrical", 1,  8, 5, 2),
        ("C002", "Replace water heater, unit 1A",        "Plumbing",   1,  8, 4, 2),
        ("C007", "Service lobby HVAC units",             "HVAC",       1,  9, 3, 2),
        ("C004", "Repair classroom cabinets",            "Carpentry",  1, 10, 4, 2),
        ("C001", "Install backup generator transfer",    "Electrical", 1, 13, 4, 3),
        ("C003", "Unclog main sewer line",               "Plumbing",   1, 14, 2, 1),
        ("C005", "Inspect flat roof drains",             "Roofing",    2,  8, 3, 2),
        ("C006", "Install ceiling fans in 3 rooms",      "Electrical", 2,  9, 3, 1),
        ("C007", "Remodel bathroom vanity, room 210",    "Carpentry",  2,  9, 6, 2),
        ("C008", "Install compressed air line",          "Plumbing",   2, 12, 4, 2),
        ("C002", "Thermostat replacement, all units",    "HVAC",       2, 13, 4, 5), // needs all 5 HVAC techs, one is on a plumbing job: unscheduled
        ("C004", "Replace boiler circulation pump",      "Plumbing",   3,  8, 5, 2),
        ("C001", "Install exam room cabinetry",          "Carpentry",  3,  8, 6, 3),
        ("C005", "Upgrade parking lot lighting",         "Electrical", 3, 10, 5, 2),
        ("C003", "Repair walk-in cooler",                "HVAC",       3, 13, 3, 1),
        ("C006", "Replace damaged shingles",             "Roofing",    4,  8, 3, 2),
        ("C007", "Emergency panel repair, kitchen",      "Electrical", 4,  9, 2, 1),
        ("C008", "Fix office drop ceiling and framing",  "Carpentry",  4, 11, 4, 2),
    ];

    /// <summary>Builds mock intake data for the work week starting on <paramref name="monday"/>.</summary>
    public static Intake Create(DateTime monday)
    {
        monday = DateTime.SpecifyKind(monday.Date, DateTimeKind.Unspecified);
        var employees = Staff.Select(s => new Employee(s.Id, s.Name)).ToList();
        var skills = Staff.SelectMany(s => s.Skills.Select(k => new EmployeeSkill(s.Id, k.Skill))).ToList();
        var completed = Staff.SelectMany(s => s.Skills.SelectMany(k =>
            Enumerable.Repeat(new CompletedWork(s.Id, k.Skill), k.Completed))).ToList();
        var customers = Clients.Select(c => new Customer(c.Id, c.Name)).ToList();
        var jobs = Work.Select((w, i) =>
        {
            var start = monday.AddDays(w.Day).AddHours(w.StartHour);
            return new Job($"T{i + 1:000}", w.Customer, w.Description, w.Skill, start, start.AddHours(w.Hours), w.Crew);
        }).ToList();
        return new Intake(employees, skills, customers, jobs, completed);
    }

    /// <summary>The Monday after <paramref name="today"/> (or a week later if today is Monday).</summary>
    public static DateTime NextMonday(DateTime today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.Date.AddDays(days == 0 ? 7 : days);
    }
}
