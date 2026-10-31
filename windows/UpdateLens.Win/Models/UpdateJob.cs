namespace UpdateLens.Win.Models;

public enum JobStatus
{
    Queued,
    Running,
    Success,
    Failed,
    Skipped
}

public sealed class UpdateJob
{
    public string AppId { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public JobStatus Status { get; set; }
    public int ExitCode { get; set; }
    public string Log { get; set; } = string.Empty;
}
