public class AnalysisReport
{
    public Guid Id { get; set; }

    public Guid SubmissionId { get; set; }

    public bool IsPlagiarized { get; set; }

    public double Similarity { get; set; }

    public string Comment { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
