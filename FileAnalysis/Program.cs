using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AnalysisDbContext>(options =>
    options.UseSqlite("Data Source=analysis.db"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AnalysisDbContext>();
    db.Database.EnsureCreated();
}

app.MapPost("/submissions", (CreateSubmissionRequest request, AnalysisDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.StudentName))
        return Results.BadRequest("Имя студента не должно быть пустым");

    if (string.IsNullOrWhiteSpace(request.Assignment))
        return Results.BadRequest("Название задания не должно быть пустым");

    if (request.FileId == Guid.Empty)
        return Results.BadRequest("FileId не должен быть пустым GUID");

    var submission = new Submission
    {
        Id = Guid.NewGuid(),
        StudentName = request.StudentName,
        Group = request.Group ?? string.Empty,
        Assignment = request.Assignment,
        FileId = request.FileId,
        SubmittedAt = DateTime.UtcNow
    };

    db.Submissions.Add(submission);
    db.SaveChanges();

    var allSubmissions = db.Submissions.ToList();
    var report = RunAnalysis(submission, allSubmissions);

    db.Reports.Add(report);
    db.SaveChanges();

    var response = new SubmissionCreatedResponse
    {
        SubmissionId = submission.Id,
        StudentName = submission.StudentName,
        Group = submission.Group,
        Assignment = submission.Assignment,
        FileId = submission.FileId,
        SubmittedAt = submission.SubmittedAt,
        IsPlagiarized = report.IsPlagiarized,
        Similarity = report.Similarity,
        Comment = report.Comment
    };

    return Results.Ok(response);
})
.WithName("CreateSubmission")
.WithOpenApi();


app.MapGet("/submissions/{id:guid}", (Guid id, AnalysisDbContext db) =>
{
    var submission = db.Submissions.SingleOrDefault(s => s.Id == id);
    if (submission == null)
        return Results.NotFound("Сдача с таким идентификатором не найдена!");

    var report = db.Reports.SingleOrDefault(r => r.SubmissionId == id);

    var response = new SubmissionDetailsResponse
    {
        SubmissionId = submission.Id,
        StudentName = submission.StudentName,
        Group = submission.Group,
        Assignment = submission.Assignment,
        FileId = submission.FileId,
        SubmittedAt = submission.SubmittedAt,
        Report = report
    };

    return Results.Ok(response);
})
.WithName("GetSubmissionById")
.WithOpenApi();


app.MapGet("/submissions/{id:guid}/report", (Guid id, AnalysisDbContext db) =>
{
    var report = db.Reports.SingleOrDefault(r => r.SubmissionId == id);
    if (report == null)
        return Results.NotFound("Отчёт по этой сдаче не найден!");

    return Results.Ok(report);
})
.WithName("GetSubmissionReport")
.WithOpenApi();


app.MapGet("/submissions", (AnalysisDbContext db) =>
{
    var response = db.Submissions
        .Select(s => new SubmissionSummaryResponse
        {
            SubmissionId = s.Id,
            StudentName = s.StudentName,
            Group = s.Group,
            Assignment = s.Assignment,
            FileId = s.FileId,
            SubmittedAt = s.SubmittedAt
        })
        .ToList();

    return Results.Ok(response);
})
.WithName("GetSubmissions")
.WithOpenApi();

// 5) Аналитика по заданию: все сдачи + агрегаты
app.MapGet("/assignments/{assignment}/reports", (string assignment, AnalysisDbContext db) =>
{
    // Берём все сдачи по этому заданию
    var itemsQuery =
        from s in db.Submissions
        where s.Assignment == assignment
        join r in db.Reports on s.Id equals r.SubmissionId into sr
        from r in sr.DefaultIfEmpty()
        select new AssignmentReportItem
        {
            SubmissionId = s.Id,
            StudentName = s.StudentName,
            Group = s.Group,
            FileId = s.FileId,
            SubmittedAt = s.SubmittedAt,
            IsPlagiarized = r != null && r.IsPlagiarized,
            Similarity = r != null ? r.Similarity : 0.0,
            Comment = r != null
                ? r.Comment
                : "Отчёт по этой сдаче ещё не сформирован."
        };

    var items = itemsQuery.ToList();

    var total = items.Count;
    var plagiarized = items.Count(i => i.IsPlagiarized);
    var avgSimilarity = total > 0 ? items.Average(i => i.Similarity) : 0.0;

    var response = new AssignmentReportResponse
    {
        Assignment = assignment,
        TotalSubmissions = total,
        PlagiarizedCount = plagiarized,
        AverageSimilarity = avgSimilarity,
        Items = items
    };

    return Results.Ok(response);
})
.WithName("GetAssignmentReports")
.WithOpenApi();


app.Run();


AnalysisReport RunAnalysis(Submission submission, List<Submission> allSubmissions)
{
    var duplicates = allSubmissions
        .Where(s => s.Assignment == submission.Assignment &&
                    s.FileId == submission.FileId &&
                    s.Id != submission.Id)
        .ToList();

    bool isPlagiarized = duplicates.Any();
    double similarity = isPlagiarized ? 100.0 : 0.0;
    string comment;

    if (isPlagiarized)
    {
        var names = string.Join(", ", duplicates.Select(d => d.StudentName));
        comment = $"Обнаружено совпадение файла с работами: {names}";
    }
    else
    {
        comment = "Совпадений по этому файлу не найдено";
    }

    return new AnalysisReport
    {
        Id = Guid.NewGuid(),
        SubmissionId = submission.Id,
        IsPlagiarized = isPlagiarized,
        Similarity = similarity,
        Comment = comment,
        CreatedAt = DateTime.UtcNow
    };
}


public class CreateSubmissionRequest
{
    public string StudentName { get; set; } = string.Empty;
    public string? Group { get; set; }
    public string Assignment { get; set; } = string.Empty;
    public Guid FileId { get; set; }
}

public class SubmissionCreatedResponse
{
    public Guid SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Assignment { get; set; } = string.Empty;
    public Guid FileId { get; set; }
    public DateTime SubmittedAt { get; set; }

    public bool IsPlagiarized { get; set; }
    public double Similarity { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public class SubmissionDetailsResponse
{
    public Guid SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Assignment { get; set; } = string.Empty;
    public Guid FileId { get; set; }
    public DateTime SubmittedAt { get; set; }

    public AnalysisReport? Report { get; set; }
}

public class SubmissionSummaryResponse
{
    public Guid SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Assignment { get; set; } = string.Empty;
    public Guid FileId { get; set; }
    public DateTime SubmittedAt { get; set; }
}

public class AssignmentReportItem
{
    public Guid SubmissionId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public Guid FileId { get; set; }
    public DateTime SubmittedAt { get; set; }

    public bool IsPlagiarized { get; set; }
    public double Similarity { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public class AssignmentReportResponse
{
    public string Assignment { get; set; } = string.Empty;
    public int TotalSubmissions { get; set; }
    public int PlagiarizedCount { get; set; }
    public double AverageSimilarity { get; set; }

    public List<AssignmentReportItem> Items { get; set; } = new();
}
