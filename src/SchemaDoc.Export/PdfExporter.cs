using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Export.Documents;

namespace SchemaDoc.Export;

public class PdfExporter : IPdfExporter
{
    static PdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GeneratePdf(DatabaseSchema schema, IReadOnlyList<TableAnnotationDto>? annotations = null)
    {
        var document = new SchemaDocument(schema, annotations);
        return document.GeneratePdf();
    }
}
