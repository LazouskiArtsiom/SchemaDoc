using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface IPdfExporter
{
    byte[] GeneratePdf(DatabaseSchema schema, IReadOnlyList<TableAnnotationDto>? annotations = null);
}
