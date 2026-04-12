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
}
