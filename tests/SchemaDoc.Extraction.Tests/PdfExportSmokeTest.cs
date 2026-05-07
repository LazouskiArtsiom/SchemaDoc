using SchemaDoc.Export;
using SchemaDoc.Extraction.Extractors;
using UglyToad.PdfPig;

namespace SchemaDoc.Extraction.Tests;

/// <summary>
/// Manual smoke test — extracts HRSystemDB and writes the PDF to disk so a human
/// can open it. Skipped in CI by default; remove the Skip to run locally.
/// </summary>
public class PdfExportSmokeTest
{
    private const string Cs = "Server=localhost;Database=HRSystemDB;Integrated Security=True;TrustServerCertificate=True;";

    [Fact]
    public async Task Generate_pdf_for_HRSystemDB_writes_to_disk()
    {
        var ext = new SqlServerExtractor();
        var schema = await ext.ExtractAsync(Cs);
        var bytes = new PdfExporter().GeneratePdf(schema);

        var path = Path.Combine(Path.GetTempPath(), "schemadoc-hrsystemdb.pdf");
        await File.WriteAllBytesAsync(path, bytes);

        Console.WriteLine($"Tables:    {schema.Tables.Count}");
        Console.WriteLine($"Triggers:  {schema.Triggers?.Count ?? 0}");
        Console.WriteLine($"SPs:       {schema.StoredProcedures.Count}");
        Console.WriteLine($"Functions: {schema.Functions?.Count ?? 0}");
        foreach (var p in schema.StoredProcedures)
            Console.WriteLine($"  SP {p.FullName}: {p.Parameters.Count} params, {(p.Dependencies?.Count ?? 0)} deps");
        foreach (var f in schema.Functions ?? Array.Empty<SchemaDoc.Core.Models.SchemaFunction>())
            Console.WriteLine($"  Fn {f.FullName} [{f.Kind}] returns {f.ReturnType}: {f.Parameters.Count} params, {(f.Dependencies?.Count ?? 0)} deps");
        var computed = schema.Tables.SelectMany(t => t.Columns.Where(c => c.IsComputed).Select(c => $"{t.FullName}.{c.Name}")).ToList();
        Console.WriteLine($"Computed:  {string.Join(", ", computed)}");

        Assert.True(bytes.Length > 1000, $"PDF too small ({bytes.Length} bytes)");
        Console.WriteLine($"PDF written to: {path} ({bytes.Length:N0} bytes)");

        // Open the PDF and assert the new sections + key seeded objects rendered.
        using var pdf = PdfDocument.Open(bytes);
        var allText = string.Join("\n", pdf.GetPages().Select(p => p.Text));

        Console.WriteLine($"Page count: {pdf.NumberOfPages}");

        var requiredFragments = new[]
        {
            "Stored Procedures",
            "Functions",
            "sp_GetEmployeesByDepartment",
            "sp_PromoteEmployee",
            "fn_GetEmployeeTenureYears",
            "fn_EmployeesInDepartment",
            "Dependencies",
            "Computed Columns",
            "FullName",
            "FirstName",     // referenced inside the FullName computed expression
        };
        foreach (var f in requiredFragments)
            Assert.Contains(f, allText, StringComparison.OrdinalIgnoreCase);
    }
}
