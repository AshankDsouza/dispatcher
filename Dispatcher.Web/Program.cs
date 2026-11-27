using Dispatcher.Core;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Local dev default matches the passwordless Compose database.
var connectionString = Environment.GetEnvironmentVariable("DISPATCHER_CONNECTION_STRING")
    ?? "Host=localhost;Database=dispatcher;Username=dispatcher";

const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/template", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"dispatcher-template-{Guid.NewGuid():N}.xlsx");
    try
    {
        Workbooks.CreateTemplate(path);
        return Results.File(File.ReadAllBytes(path), XlsxType, "intake.xlsx");
    }
    finally { File.Delete(path); }
});

app.MapGet("/sample", () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"dispatcher-sample-{Guid.NewGuid():N}.xlsx");
    try
    {
        Workbooks.WriteIntake(path, SampleData.Create(SampleData.NextMonday(DateTime.Today)));
        return Results.File(File.ReadAllBytes(path), XlsxType, "sample-intake.xlsx");
    }
    finally { File.Delete(path); }
});

app.MapPost("/schedule", async (HttpRequest request, HttpResponse response) =>
{
    if (!request.HasFormContentType)
        return Results.Text("Upload an intake workbook.", statusCode: 400);
    var form = await request.ReadFormAsync();
    var upload = form.Files["intake"];
    if (upload is null || upload.Length == 0)
        return Results.Text("Upload an intake workbook.", statusCode: 400);

    var input = Path.Combine(Path.GetTempPath(), $"dispatcher-in-{Guid.NewGuid():N}.xlsx");
    var output = Path.Combine(Path.GetTempPath(), $"dispatcher-out-{Guid.NewGuid():N}.xlsx");
    try
    {
        await using (var stream = File.Create(input))
            await upload.CopyToAsync(stream);

        var intake = Workbooks.Read(input);
        var assignments = Scheduler.Schedule(intake);
        await Database.SaveAsync(connectionString, intake, assignments);
        Workbooks.WriteSchedule(output, intake, assignments);

        response.Headers["X-Scheduled"] = assignments.Count(a => a.Crew.Count > 0).ToString();
        response.Headers["X-Unscheduled"] = assignments.Count(a => a.Crew.Count == 0).ToString();
        return Results.File(await File.ReadAllBytesAsync(output), XlsxType, "schedule.xlsx");
    }
    catch (Exception ex)
    {
        return Results.Text(ex.Message, statusCode: 400);
    }
    finally
    {
        File.Delete(input);
        File.Delete(output);
    }
});

app.Run();
