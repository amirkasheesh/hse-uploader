public class Submission
{
    public Guid Id { get; set; }

    public string StudentName { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public string Assignment { get; set; } = string.Empty;

    public Guid FileId { get; set; }

    public DateTime SubmittedAt { get; set; }
}
