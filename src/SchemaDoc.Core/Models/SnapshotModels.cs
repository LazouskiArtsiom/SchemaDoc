namespace SchemaDoc.Core.Models;

public record SnapshotSummary(
    int Id,
    int ConnectionId,
    string ConnectionName,
    string DatabaseName,
    DateTime ExtractedAt,
    string? Name,
    string? Notes,
    int TableCount
);
