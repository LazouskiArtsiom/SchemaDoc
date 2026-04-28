using SchemaDoc.Core.Models;

namespace SchemaDoc.Infrastructure.Data;

public class SavedConnection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DatabaseProvider Provider { get; set; }
    public string EncryptedConnectionString { get; set; } = "";
    public DateTime LastConnectedAt { get; set; }
    public string? LastDatabaseName { get; set; }
    /// <summary>
    /// Tag used for environment labelling (Dev/Staging/Prod/...). Stored as empty
    /// string when no tag is set so that the unique index on (Name, Tag) treats
    /// untagged connections deterministically.
    /// </summary>
    public string Tag { get; set; } = "";
    public string? TagColor { get; set; }

    public ICollection<SchemaSnapshot> Snapshots { get; set; } = [];
}

public class TableAnnotation
{
    public int Id { get; set; }
    public string ConnectionName { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string SchemaName { get; set; } = "";
    public string TableName { get; set; } = "";
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ColumnAnnotation> ColumnAnnotations { get; set; } = [];
}

public class ColumnAnnotation
{
    public int Id { get; set; }
    public int TableAnnotationId { get; set; }
    public TableAnnotation TableAnnotation { get; set; } = null!;
    public string ColumnName { get; set; } = "";
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SchemaSnapshot
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public SavedConnection Connection { get; set; } = null!;
    public string DatabaseName { get; set; } = "";
    public DateTime ExtractedAt { get; set; }
    public string SchemaJson { get; set; } = "";

    /// <summary>
    /// Optional user-supplied label. Named snapshots are durable artifacts and never
    /// auto-rotate. Unnamed snapshots are throwaway cache (Refresh Schema) and rotate
    /// when their count per (connection, database) exceeds the cap.
    /// </summary>
    public string? Name { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Per-database tag override under a server connection. Lets users mark e.g.
/// the `Bookstore_QA` DB on a shared server with a "QA" tag separately from
/// the server's default tag.
/// </summary>
public class DatabaseTag
{
    public int Id { get; set; }
    public int ConnectionId { get; set; }
    public string DatabaseName { get; set; } = "";
    public string? Tag { get; set; }
    public string? TagColor { get; set; }
}
