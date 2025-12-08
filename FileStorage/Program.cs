using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();



(string filePath, string metaPath) BuildPaths(Guid fileId)
{
    string directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "Store");
    if (!Directory.Exists(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }

    string filePath = Path.Combine(directoryPath, fileId.ToString());
    string metaPath = Path.Combine(directoryPath, fileId.ToString() + ".meta.json");

    return (filePath, metaPath);
}

(Guid, string, long) UploadFile(IFormFile file)
{
    if (file == null)
        throw new ArgumentException("Файл не был предоставлен!");

    if (file.Length == 0)
        throw new ArgumentException("Файл пустой!");

    Guid fileId = Guid.NewGuid();

    var (filePath, metaPath) = BuildPaths(fileId);

    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        file.CopyTo(stream);
    }

    var meta = new FileMeta
    {
        FileId = fileId,
        OriginalName = file.FileName,
        Size = file.Length,
        ContentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType
    };

    string json = JsonSerializer.Serialize(meta);
    File.WriteAllText(metaPath, json);

    return (fileId, file.FileName, file.Length);
}

string DownloadFile(Guid fileId)
{
    var (filePath, _) = BuildPaths(fileId);

    if (!File.Exists(filePath))
        throw new FileNotFoundException("Файл не был найден!");

    return filePath;
}

string GetDownloadName(Guid fileId)
{
    var (_, metaPath) = BuildPaths(fileId);

    if (!File.Exists(metaPath))
        return fileId.ToString();

    string json = File.ReadAllText(metaPath);
    var meta = JsonSerializer.Deserialize<FileMeta>(json);

    if (meta == null || string.IsNullOrWhiteSpace(meta.OriginalName))
        return fileId.ToString();

    return meta.OriginalName;
}


app.MapPost("/files", (IFormFile file) =>
{
    try
    {
        var (id, name, size) = UploadFile(file);
        return Results.Ok(new { fileId = id, fileName = name, size });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(ex.Message);
    }
})
.DisableAntiforgery();

app.MapGet("/files/{fileId:guid}", (Guid fileId) =>
{
    try
    {
        var filePath = DownloadFile(fileId);
        var downloadName = GetDownloadName(fileId);

        return Results.File(filePath, "application/octet-stream", downloadName);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(ex.Message);
    }
});

app.Run();

class FileMeta
{
    public Guid FileId { get; set; }
    public string OriginalName { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
}
