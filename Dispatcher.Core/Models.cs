namespace Dispatcher.Core;

/// <param name="Rating">1 (lowest) to 5 (highest).</param>
/// <param name="CompletionPercent">Share of assigned jobs the employee completed, 0 to 100.</param>
public record Employee(string Id, string Name, int Rating, decimal CompletionPercent);
public record EmployeeSkill(string EmployeeId, string Skill);
public record Customer(string Id, string Name);
/// <param name="Priority">1 (lowest) to 5 (highest); higher-priority tasks are dispatched first.</param>
public record Job(string Id, string CustomerId, string Description, string Skill,
    DateTime Start, DateTime End, int CrewSize, int Priority);
public record CompletedWork(string EmployeeId, string Skill);
/// <summary>One employee booked on a task (a row of the Scheduled table).</summary>
public record ScheduledWork(string TaskId, string EmployeeId, DateTime Start, DateTime End);
public record Intake(List<Employee> Employees, List<EmployeeSkill> Skills, List<Customer> Customers,
    List<Job> Jobs, List<CompletedWork> CompletedWork, List<ScheduledWork> Scheduled);
/// <summary>A task and its crew; an empty crew means the task is not scheduled.</summary>
public record Assignment(Job Job, IReadOnlyList<Employee> Crew);

public static class Scheduler
{
    /// <summary>
    /// Keeps every task that is already in <see cref="Intake.Scheduled"/> as it is, and assigns crews to the rest.
    /// Tasks are dispatched by priority (highest first), then start time, then ID. Eligible employees have the
    /// task's skill and no overlapping booking; they are ranked by rating, then completion percentage, then
    /// completed jobs in the skill (all descending), then ID. A task without a full crew stays unscheduled.
    /// </summary>
    /// <returns>One assignment per task, ordered by start time then ID.</returns>
    public static List<Assignment> Schedule(Intake intake)
    {
        var employees = intake.Employees.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var busy = intake.Employees.ToDictionary(e => e.Id, _ => new List<(DateTime Start, DateTime End)>(),
            StringComparer.Ordinal);
        foreach (var booking in intake.Scheduled)
            busy[booking.EmployeeId].Add((booking.Start, booking.End));
        var existing = intake.Scheduled.ToLookup(s => s.TaskId, StringComparer.Ordinal);
        var skills = intake.Skills.ToHashSet();
        var experience = intake.CompletedWork.GroupBy(w => (w.EmployeeId, w.Skill))
            .ToDictionary(g => g.Key, g => g.Count());
        var crews = new Dictionary<string, IReadOnlyList<Employee>>(StringComparer.Ordinal);

        foreach (var job in intake.Jobs.OrderByDescending(j => j.Priority).ThenBy(j => j.Start)
                     .ThenBy(j => j.Id, StringComparer.Ordinal))
        {
            if (existing.Contains(job.Id))
            {
                crews[job.Id] = existing[job.Id].Select(s => employees[s.EmployeeId])
                    .OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
                continue;
            }
            var crew = intake.Employees
                .Where(e => skills.Contains(new EmployeeSkill(e.Id, job.Skill)) &&
                    busy[e.Id].All(other => job.Start >= other.End || job.End <= other.Start))
                .OrderByDescending(e => e.Rating)
                .ThenByDescending(e => e.CompletionPercent)
                .ThenByDescending(e => experience.GetValueOrDefault((e.Id, job.Skill)))
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .Take(job.CrewSize).ToList();
            if (crew.Count != job.CrewSize)
                crew.Clear();
            else
                foreach (var employee in crew)
                    busy[employee.Id].Add((job.Start, job.End));
            crews[job.Id] = crew;
        }

        return intake.Jobs.OrderBy(j => j.Start).ThenBy(j => j.Id, StringComparer.Ordinal)
            .Select(j => new Assignment(j, crews[j.Id])).ToList();
    }

    /// <summary>The Scheduled rows for tasks that <see cref="Schedule"/> newly assigned.</summary>
    public static List<ScheduledWork> NewBookings(Intake intake, IEnumerable<Assignment> assignments)
    {
        var existing = intake.Scheduled.Select(s => s.TaskId).ToHashSet(StringComparer.Ordinal);
        return assignments.Where(a => !existing.Contains(a.Job.Id))
            .SelectMany(a => a.Crew.Select(e => new ScheduledWork(a.Job.Id, e.Id, a.Job.Start, a.Job.End)))
            .ToList();
    }
}
