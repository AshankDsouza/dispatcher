namespace Dispatcher.Core;

public record Employee(string Id, string Name);
public record EmployeeSkill(string EmployeeId, string Skill);
public record Customer(string Id, string Name);
public record Job(string Id, string CustomerId, string Description, string Skill,
    DateTime Start, DateTime End, int CrewSize);
public record CompletedWork(string EmployeeId, string Skill);
public record Intake(List<Employee> Employees, List<EmployeeSkill> Skills, List<Customer> Customers,
    List<Job> Jobs, List<CompletedWork> CompletedWork);
public record Assignment(Job Job, IReadOnlyList<Employee> Crew);

public static class Scheduler
{
    public static List<Assignment> Schedule(Intake intake)
    {
        var busy = intake.Employees.ToDictionary(e => e.Id, _ => new List<Job>(), StringComparer.Ordinal);
        var skills = intake.Skills.ToHashSet();
        var experience = intake.CompletedWork.GroupBy(w => (w.EmployeeId, w.Skill))
            .ToDictionary(g => g.Key, g => g.Count());
        var result = new List<Assignment>();

        foreach (var job in intake.Jobs.OrderBy(j => j.Start).ThenBy(j => j.Id, StringComparer.Ordinal))
        {
            var crew = intake.Employees
                .Where(e => skills.Contains(new EmployeeSkill(e.Id, job.Skill)) &&
                    busy[e.Id].All(other => job.Start >= other.End || job.End <= other.Start))
                .OrderByDescending(e => experience.GetValueOrDefault((e.Id, job.Skill)))
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .Take(job.CrewSize).ToList();
            if (crew.Count != job.CrewSize)
                crew.Clear();
            else
                foreach (var employee in crew)
                    busy[employee.Id].Add(job);
            result.Add(new Assignment(job, crew));
        }
        return result;
    }
}
