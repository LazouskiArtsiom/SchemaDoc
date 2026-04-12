using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Export.Documents;

public class SchemaDocument : IDocument
{
    private readonly DatabaseSchema _schema;
    private readonly IReadOnlyList<TableAnnotationDto>? _annotations;

    // Colors
    private static readonly string PrimaryColor = "#1E3A5F";
    private static readonly string AccentColor = "#2563EB";
    private static readonly string LightGray = "#F3F4F6";
    private static readonly string MediumGray = "#9CA3AF";
    private static readonly string BorderColor = "#D1D5DB";
    private static readonly string HeaderBg = "#1E3A5F";
    private static readonly string HeaderText = "#FFFFFF";
    private static readonly string PkBadgeColor = "#FEF3C7";
    private static readonly string FkBadgeColor = "#DBEAFE";

    public SchemaDocument(DatabaseSchema schema, IReadOnlyList<TableAnnotationDto>? annotations = null)
    {
        _schema = schema;
        _annotations = annotations;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Schema Documentation - {_schema.DatabaseName}",
        Author = "SchemaDoc",
        Subject = "Database Schema Documentation",
        CreationDate = _schema.ExtractedAt
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginVertical(40);
            page.MarginHorizontal(50);

            page.Content().Column(col =>
            {
                // Title page
                ComposeTitlePage(col);

                // Table of Contents
                col.Item().PageBreak();
                ComposeTableOfContents(col);

                // Per-table sections
                foreach (var table in _schema.Tables)
                {
                    col.Item().PageBreak();
                    ComposeTableSection(col, table);
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private void ComposeTitlePage(ColumnDescriptor col)
    {
        col.Item().Height(250).AlignMiddle().Column(inner =>
        {
            inner.Item().AlignCenter().PaddingBottom(20)
                .Text("Schema Documentation")
                .FontSize(32).Bold().FontColor(PrimaryColor);

            inner.Item().AlignCenter().PaddingBottom(8)
                .Text(_schema.DatabaseName)
                .FontSize(20).FontColor(AccentColor);

            inner.Item().AlignCenter().PaddingBottom(30)
                .Text($"Provider: {_schema.Provider}")
                .FontSize(12).FontColor(MediumGray);

            // Divider line
            inner.Item().AlignCenter().Width(100).Height(2)
                .Background(AccentColor);

            inner.Item().PaddingTop(20).AlignCenter()
                .Text($"Generated on {_schema.ExtractedAt:MMMM dd, yyyy 'at' HH:mm}")
                .FontSize(10).FontColor(MediumGray);

            inner.Item().PaddingTop(8).AlignCenter()
                .Text($"{_schema.Tables.Count} tables | {_schema.Views.Count} views | {_schema.ForeignKeys.Count} foreign keys")
                .FontSize(10).FontColor(MediumGray);
        });
    }

    private void ComposeTableOfContents(ColumnDescriptor col)
    {
        col.Item().PaddingBottom(15)
            .Text("Table of Contents")
            .FontSize(22).Bold().FontColor(PrimaryColor);

        col.Item().Height(2).Background(AccentColor);
        col.Item().PaddingTop(15);

        var index = 1;
        foreach (var table in _schema.Tables)
        {
            col.Item().PaddingBottom(6).Row(row =>
            {
                row.AutoItem().Width(30)
                    .Text($"{index}.")
                    .FontSize(10).FontColor(MediumGray);

                row.RelativeItem()
                    .Text(table.FullName)
                    .FontSize(11).FontColor(PrimaryColor);

                row.AutoItem()
                    .Text($"{table.Columns.Count} columns")
                    .FontSize(9).FontColor(MediumGray);
            });
            index++;
        }
    }

    private void ComposeTableSection(ColumnDescriptor col, SchemaTable table)
    {
        var annotation = FindAnnotation(table);

        // Table header
        col.Item().PaddingBottom(5)
            .Text(table.FullName)
            .FontSize(18).Bold().FontColor(PrimaryColor);

        // Row count badge
        if (table.RowCount.HasValue)
        {
            col.Item().PaddingBottom(5)
                .Text($"Rows: {table.RowCount.Value:N0}")
                .FontSize(9).FontColor(MediumGray);
        }

        // Table description from annotations
        if (!string.IsNullOrWhiteSpace(annotation?.Description))
        {
            col.Item().PaddingBottom(8).PaddingLeft(2)
                .Text(annotation.Description)
                .FontSize(10).FontColor("#4B5563").Italic();
        }

        // Tags
        if (!string.IsNullOrWhiteSpace(annotation?.Tags))
        {
            col.Item().PaddingBottom(8).Row(row =>
            {
                foreach (var tag in annotation.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    row.AutoItem().PaddingRight(4)
                        .Background("#EFF6FF").Padding(3)
                        .Text(tag).FontSize(8).FontColor(AccentColor);
                }
            });
        }

        col.Item().PaddingBottom(3).Height(1).Background(BorderColor);
        col.Item().PaddingBottom(8);

        // Columns table
        ComposeColumnsGrid(col, table, annotation);

        // Foreign keys for this table
        var tableFks = _schema.ForeignKeys
            .Where(fk => fk.ParentSchema == table.Schema && fk.ParentTable == table.Name)
            .ToList();

        if (tableFks.Count > 0)
        {
            col.Item().PaddingTop(12).PaddingBottom(5)
                .Text("Foreign Key Relationships")
                .FontSize(12).Bold().FontColor(PrimaryColor);

            ComposeForeignKeysGrid(col, tableFks);
        }
    }

    private void ComposeColumnsGrid(ColumnDescriptor col, SchemaTable table, TableAnnotationDto? annotation)
    {
        col.Item().Table(grid =>
        {
            // Column definitions
            grid.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30);  // #
                columns.RelativeColumn(3);   // Name
                columns.RelativeColumn(2);   // Type
                columns.ConstantColumn(50);  // Nullable
                columns.ConstantColumn(30);  // PK
                columns.ConstantColumn(30);  // FK
                columns.RelativeColumn(3);   // Description
            });

            // Header row
            grid.Header(header =>
            {
                HeaderCell(header, "#");
                HeaderCell(header, "Name");
                HeaderCell(header, "Type");
                HeaderCell(header, "Nullable");
                HeaderCell(header, "PK");
                HeaderCell(header, "FK");
                HeaderCell(header, "Description");
            });

            // Data rows
            var ordinal = 1;
            foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                var colAnnotation = annotation?.ColumnAnnotations
                    .FirstOrDefault(ca => ca.ColumnName == column.Name);

                var bgColor = ordinal % 2 == 0 ? LightGray : "#FFFFFF";

                DataCell(grid, $"{ordinal}", bgColor);
                ComposeNameCell(grid, column, bgColor);
                ComposeTypeCell(grid, column, bgColor);
                DataCell(grid, column.IsNullable ? "Yes" : "No", bgColor);
                ComposeBadgeCell(grid, column.IsPrimaryKey, "PK", PkBadgeColor, "#92400E", bgColor);
                ComposeBadgeCell(grid, column.IsForeignKey, "FK", FkBadgeColor, "#1E40AF", bgColor);
                ComposeDescriptionCell(grid, column, colAnnotation, bgColor);

                ordinal++;
            }
        });
    }

    private static void HeaderCell(TableCellDescriptor header, string text)
    {
        header.Cell().Background(HeaderBg).Padding(5)
            .Text(text).FontSize(9).Bold().FontColor(HeaderText);
    }

    private static void DataCell(TableDescriptor grid, string text, string bgColor)
    {
        grid.Cell().Background(bgColor)
            .BorderBottom(1).BorderColor(BorderColor)
            .Padding(4)
            .Text(text).FontSize(9);
    }

    private static void ComposeNameCell(TableDescriptor grid, SchemaColumn column, string bgColor)
    {
        grid.Cell().Background(bgColor)
            .BorderBottom(1).BorderColor(BorderColor)
            .Padding(4)
            .Text(text =>
            {
                if (column.IsPrimaryKey)
                    text.Span(column.Name).FontSize(9).Bold();
                else
                    text.Span(column.Name).FontSize(9);
            });
    }

    private static void ComposeTypeCell(TableDescriptor grid, SchemaColumn column, string bgColor)
    {
        var typeText = column.DataType;
        if (!string.IsNullOrEmpty(column.MaxLength) && column.MaxLength != "-1")
            typeText += $"({column.MaxLength})";
        else if (column.NumericPrecision.HasValue && column.NumericScale.HasValue)
            typeText += $"({column.NumericPrecision},{column.NumericScale})";

        grid.Cell().Background(bgColor)
            .BorderBottom(1).BorderColor(BorderColor)
            .Padding(4)
            .Text(typeText).FontSize(8).FontFamily("Courier New");
    }

    private static void ComposeBadgeCell(TableDescriptor grid, bool isSet, string label,
        string badgeBg, string badgeText, string bgColor)
    {
        grid.Cell().Background(bgColor)
            .BorderBottom(1).BorderColor(BorderColor)
            .Padding(4)
            .AlignCenter()
            .Text(text =>
            {
                if (isSet)
                    text.Span(label).FontSize(8).Bold().FontColor(badgeText);
            });
    }

    private static void ComposeDescriptionCell(TableDescriptor grid, SchemaColumn column,
        ColumnAnnotationDto? colAnnotation, string bgColor)
    {
        var description = colAnnotation?.Description ?? column.DbNativeComment ?? "";

        grid.Cell().Background(bgColor)
            .BorderBottom(1).BorderColor(BorderColor)
            .Padding(4)
            .Text(description).FontSize(8).FontColor("#6B7280");
    }

    private void ComposeForeignKeysGrid(ColumnDescriptor col, List<ForeignKeyRelation> fks)
    {
        col.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);  // Constraint Name
                columns.RelativeColumn(2);  // Column
                columns.RelativeColumn(3);  // References
                columns.RelativeColumn(2);  // On Delete
                columns.RelativeColumn(2);  // On Update
            });

            grid.Header(header =>
            {
                HeaderCell(header, "Constraint");
                HeaderCell(header, "Column");
                HeaderCell(header, "References");
                HeaderCell(header, "On Delete");
                HeaderCell(header, "On Update");
            });

            var index = 1;
            foreach (var fk in fks)
            {
                var bgColor = index % 2 == 0 ? LightGray : "#FFFFFF";

                DataCell(grid, fk.ConstraintName, bgColor);
                DataCell(grid, fk.ParentColumn, bgColor);
                DataCell(grid, $"{fk.ReferencedSchema}.{fk.ReferencedTable}.{fk.ReferencedColumn}", bgColor);
                DataCell(grid, fk.OnDelete ?? "NO ACTION", bgColor);
                DataCell(grid, fk.OnUpdate ?? "NO ACTION", bgColor);

                index++;
            }
        });
    }

    private TableAnnotationDto? FindAnnotation(SchemaTable table)
    {
        return _annotations?.FirstOrDefault(a =>
            a.SchemaName == table.Schema && a.TableName == table.Name);
    }
}
