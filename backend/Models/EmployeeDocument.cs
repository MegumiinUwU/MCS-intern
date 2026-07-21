namespace MCS_app.Models
{
    public class EmployeeDocument
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;

        // Relative path (under the configured storage root) to the file on disk,
        // e.g. "3/2f7b1a-....pdf". The physical file, not the DB, holds the bytes.
        public string FilePath { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }
}
