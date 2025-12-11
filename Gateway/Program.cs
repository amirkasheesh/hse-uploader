using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var fileStorageBaseUrl =
    builder.Configuration["Services:FileStorage"]
    ?? "http://localhost:5070";

var fileAnalysisBaseUrl =
    builder.Configuration["Services:FileAnalysis"]
    ?? "http://localhost:5170";

builder.Services.AddHttpClient("FileStorage", client =>
{
    client.BaseAddress = new Uri(fileStorageBaseUrl);
});

builder.Services.AddHttpClient("FileAnalysis", client =>
{
    client.BaseAddress = new Uri(fileAnalysisBaseUrl);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();


app.MapPost("/submit", async (
    [FromForm] SubmitForm form,
    IHttpClientFactory httpClientFactory) =>
{
    var file = form.File;
    var studentName = form.StudentName;
    var assignment = form.Assignment;
    var group = form.Group;

    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("Файл не был предоставлен или пустой!");
    }

    if (string.IsNullOrWhiteSpace(studentName))
    {
        return Results.BadRequest("Имя студента не должно быть пустым!");
    }

    if (string.IsNullOrWhiteSpace(assignment))
    {
        return Results.BadRequest("Название задания не должно быть пустым!");
    }

    var storageClient = httpClientFactory.CreateClient("FileStorage");

    using var fileStream = file.OpenReadStream();
    using var formData = new MultipartFormDataContent();
    formData.Add(new StreamContent(fileStream), "file", file.FileName);

    HttpResponseMessage storageResponse;
    try
    {
        storageResponse = await storageClient.PostAsync("/files", formData);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileStorage",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    if (!storageResponse.IsSuccessStatusCode)
    {
        var errorText = await storageResponse.Content.ReadAsStringAsync();
        return Results.Problem(
            title: "FileStorage вернул ошибку",
            detail: errorText,
            statusCode: (int)storageResponse.StatusCode);
    }

    var storageJson = await storageResponse.Content.ReadAsStringAsync();

    FileUploadResult? uploadResult;
    try
    {
        uploadResult = JsonSerializer.Deserialize<FileUploadResult>(
            storageJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Не удалось разобрать ответ FileStorage",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (uploadResult == null)
    {
        return Results.Problem(
            title: "Пустой ответ от FileStorage",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    var analysisClient = httpClientFactory.CreateClient("FileAnalysis");

    var submissionRequest = new SubmissionRequestDto(
        StudentName: studentName,
        Group: group,
        Assignment: assignment,
        FileId: uploadResult.FileId);

    var submissionJson = JsonSerializer.Serialize(submissionRequest);
    var content = new StringContent(submissionJson, Encoding.UTF8, "application/json");

    HttpResponseMessage analysisResponse;
    try
    {
        analysisResponse = await analysisClient.PostAsync("/submissions", content);
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileAnalysis",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var analysisBody = await analysisResponse.Content.ReadAsStringAsync();

    if (!analysisResponse.IsSuccessStatusCode)
    {
        return Results.Problem(
            title: "FileAnalysis вернул ошибку",
            detail: analysisBody,
            statusCode: (int)analysisResponse.StatusCode);
    }

    return Results.Content(analysisBody, "application/json");
})
.WithName("SubmitWork")
.WithOpenApi()
.DisableAntiforgery();



app.MapGet("/submissions/{id:guid}", async (Guid id, IHttpClientFactory httpClientFactory) =>
{
    var analysisClient = httpClientFactory.CreateClient("FileAnalysis");

    HttpResponseMessage response;
    try
    {
        response = await analysisClient.GetAsync($"/submissions/{id}");
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileAnalysis",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var body = await response.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);

})
.WithName("GatewayGetSubmissionById")
.WithOpenApi();


app.MapGet("/submissions/{id:guid}/report", async (Guid id, IHttpClientFactory httpClientFactory) =>
{
    var analysisClient = httpClientFactory.CreateClient("FileAnalysis");

    HttpResponseMessage response;
    try
    {
        response = await analysisClient.GetAsync($"/submissions/{id}/report");
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileAnalysis",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var body = await response.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);

})
.WithName("GatewayGetSubmissionReport")
.WithOpenApi();

app.MapGet("/submissions/{id:guid}/wordcloud", async (Guid id, IHttpClientFactory httpClientFactory) =>
{
    var analysisClient = httpClientFactory.CreateClient("FileAnalysis");

    HttpResponseMessage response;
    try
    {
        response = await analysisClient.GetAsync($"/submissions/{id}/wordcloud");
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileAnalysis",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    if (!response.IsSuccessStatusCode)
    {
        var error = await response.Content.ReadAsStringAsync();
        return Results.Problem(detail: error, statusCode: (int)response.StatusCode);
    }

    var bytes = await response.Content.ReadAsByteArrayAsync();
    return Results.File(bytes, "image/png");
})
.WithName("GatewayGetWordCloud")
.WithOpenApi();

app.MapGet("/files/{fileId:guid}", async (Guid fileId, IHttpClientFactory httpClientFactory) =>
{
    var storageClient = httpClientFactory.CreateClient("FileStorage");

    HttpResponseMessage response;
    try
    {
        response = await storageClient.GetAsync($"/files/{fileId}");
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileStorage",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    if (!response.IsSuccessStatusCode)
    {
        var errorBody = await response.Content.ReadAsStringAsync();
        return Results.Problem(
            title: "FileStorage вернул ошибку",
            detail: errorBody,
            statusCode: (int)response.StatusCode);
    }

    var stream = await response.Content.ReadAsStreamAsync();
    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
    var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                   response.Content.Headers.ContentDisposition?.FileName ??
                   fileId.ToString();

    return Results.File(stream, contentType, fileName);
})
.WithName("GatewayDownloadFile")
.WithOpenApi();


app.MapGet("/assignments/{assignment}/reports", async (string assignment, IHttpClientFactory httpClientFactory) =>
{
    var analysisClient = httpClientFactory.CreateClient("FileAnalysis");

    HttpResponseMessage response;
    try
    {
        var encoded = Uri.EscapeDataString(assignment);
        response = await analysisClient.GetAsync($"/assignments/{encoded}/reports");
    }
    catch (Exception ex)
    {
        return Results.Problem(
            title: "Ошибка при обращении к FileAnalysis",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }

    var body = await response.Content.ReadAsStringAsync();
    return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);
})
.WithName("GatewayGetAssignmentReports")
.WithOpenApi();


app.Run();

record FileUploadResult(Guid FileId, string FileName, long Size);

record SubmissionRequestDto(
    string StudentName,
    string? Group,
    string Assignment,
    Guid FileId
);

public class SubmitForm
{
    public IFormFile File { get; set; } = default!;

    public string StudentName { get; set; } = string.Empty;

    public string Assignment { get; set; } = string.Empty;

    public string? Group { get; set; }
}
