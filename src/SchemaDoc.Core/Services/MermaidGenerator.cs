using System.Text;
using System.Text.RegularExpressions;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services;

public static class MermaidGenerator
{
    public static string Generate(DatabaseSchema schema, IEnumerable<string>? tableFilter = null)
    {
        var filterSet = tableFilter?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tables = filterSet is null
            ? schema.Tables
            : schema.Tables.Where(t => filterSet.Contains(t.Name) || filterSet.Contains(t.FullName)).ToList();

        var tableNames = tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        // Entity definitions
        foreach (var table in tables)
        {
            var safeName = Sanitize(table.Name);
            sb.AppendLine($"  {safeName} {{");
            foreach (var col in table.Columns)
            {
                var type = SanitizeType(FormatType(col.DataType, col.MaxLength));
                var colName = Sanitize(col.Name);
                var suffix = col.IsPrimaryKey && col.IsForeignKey ? " PK,FK"
                           : col.IsPrimaryKey ? " PK"
                           : col.IsForeignKey ? " FK"
                           : "";
                // Mermaid erDiagram syntax: "type name key" — we put colName first so it
                // renders as: Name | Type | Key  (more readable than Type | Name | Key)
                sb.AppendLine($"    {colName} {type}{suffix}");
            }
            sb.AppendLine("  }");
        }

        // Relationships — labeled with exact column names
        var rendered = new HashSet<string>();
        foreach (var fk in schema.ForeignKeys)
        {
            if (!tableNames.Contains(fk.ParentTable) || !tableNames.Contains(fk.ReferencedTable))
                continue;

            var edge = $"{fk.ParentTable}.{fk.ParentColumn}->{fk.ReferencedTable}.{fk.ReferencedColumn}";
            if (!rendered.Add(edge)) continue;

            var from  = Sanitize(fk.ParentTable);
            var to    = Sanitize(fk.ReferencedTable);
            var label = $"{fk.ParentColumn} to {fk.ReferencedColumn}";
            sb.AppendLine($"  {from} }}o--|| {to} : \"{label}\"");
        }

        return sb.ToString();
    }

    private static string FormatType(string dataType, string? maxLength) =>
        maxLength is not null ? $"{dataType}({maxLength})" : dataType;

    // Mermaid v11 erDiagram allows letters, digits, underscores, hyphens, parens, brackets
    private static string SanitizeType(string type) =>
        Regex.Replace(type, @"[^a-zA-Z0-9_()\[\]\-]", "_");

    private static string Sanitize(string name) =>
        Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
}
