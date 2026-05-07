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

                // DB-level sections (only emit a page break + section if there's content)
                if ((_schema.Triggers?.Count ?? 0) > 0)
                {
                    col.Item().PageBreak();
                    ComposeTriggersSection(col);
                }
                if (_schema.StoredProcedures.Count > 0)
                {
                    col.Item().PageBreak();
                    ComposeStoredProceduresSection(col);
                }
                if ((_schema.Functions?.Count ?? 0) > 0)
                {
                    col.Item().PageBreak();
                    ComposeFunctionsSection(col);
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

        // Indexes
        if (table.Indexes is { Count: > 0 })
        {
            col.Item().PaddingTop(12).PaddingBottom(5)
                .Text("Indexes")
                .FontSize(12).Bold().FontColor(PrimaryColor);
            ComposeIndexesGrid(col, table.Indexes);
        }

        // Unique constraints
        if (table.UniqueConstraints is { Count: > 0 })
        {
            col.Item().PaddingTop(12).PaddingBottom(5)
                .Text("Unique Constraints")
                .FontSize(12).Bold().FontColor(PrimaryColor);
            ComposeUniqueConstraintsGrid(col, table.UniqueConstraints);
        }

        // Check constraints
        if (table.CheckConstraints is { Count: > 0 })
        {
            col.Item().PaddingTop(12).PaddingBottom(5)
                .Text("Check Constraints")
                .FontSize(12).Bold().FontColor(PrimaryColor);
            ComposeCheckConstraintsGrid(col, table.CheckConstraints);
        }

        // Computed columns
        var computed = table.Columns.Where(c => c.IsComputed && !string.IsNullOrEmpty(c.ComputedExpression)).ToList();
        if (computed.Count > 0)
        {
            col.Item().PaddingTop(12).PaddingBottom(5)
                .Text("Computed Columns")
                .FontSize(12).Bold().FontColor(PrimaryColor);
            ComposeComputedColumnsGrid(col, computed);
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

    // ──────────────────────────────────────────────────────────────────────────
    // Per-table extras: indexes, unique constraints, check constraints, computed
    // ──────────────────────────────────────────────────────────────────────────

    private static void ComposeIndexesGrid(ColumnDescriptor col, IReadOnlyList<SchemaIndex> indexes)
    {
        col.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);   // Name
                columns.RelativeColumn(4);   // Columns
                columns.ConstantColumn(60);  // Unique
                columns.RelativeColumn(2);   // Type
                columns.RelativeColumn(3);   // Filter / Includes
            });

            grid.Header(header =>
            {
                HeaderCell(header, "Name");
                HeaderCell(header, "Columns");
                HeaderCell(header, "Unique");
                HeaderCell(header, "Type");
                HeaderCell(header, "Filter / Included");
            });

            var i = 1;
            foreach (var ix in indexes)
            {
                var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                var cols = string.Join(", ", ix.Columns.Select(c => c.IsDescending ? c.Name + " DESC" : c.Name));
                var extra = "";
                if (ix.IncludedColumns is { Count: > 0 })
                    extra = "INCLUDE (" + string.Join(", ", ix.IncludedColumns) + ")";
                if (!string.IsNullOrEmpty(ix.FilterExpression))
                    extra = (extra.Length > 0 ? extra + " · " : "") + "WHERE " + ix.FilterExpression;

                DataCell(grid, ix.Name, bg);
                DataCell(grid, cols, bg);
                DataCell(grid, ix.IsUnique ? "Yes" : "No", bg);
                DataCell(grid, ix.IndexType ?? "", bg);
                DataCell(grid, extra, bg);
                i++;
            }
        });
    }

    private static void ComposeUniqueConstraintsGrid(ColumnDescriptor col, IReadOnlyList<UniqueConstraint> uqs)
    {
        col.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(5);
            });
            grid.Header(header =>
            {
                HeaderCell(header, "Name");
                HeaderCell(header, "Columns");
            });
            var i = 1;
            foreach (var uq in uqs)
            {
                var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                DataCell(grid, uq.Name, bg);
                DataCell(grid, string.Join(", ", uq.Columns), bg);
                i++;
            }
        });
    }

    private static void ComposeCheckConstraintsGrid(ColumnDescriptor col, IReadOnlyList<CheckConstraint> cks)
    {
        col.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(5);
            });
            grid.Header(header =>
            {
                HeaderCell(header, "Name");
                HeaderCell(header, "Expression");
            });
            var i = 1;
            foreach (var ck in cks)
            {
                var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                DataCell(grid, ck.Name, bg);
                grid.Cell().Background(bg).BorderBottom(1).BorderColor(BorderColor).Padding(4)
                    .Text(ck.Expression).FontSize(8).FontFamily("Courier New");
                i++;
            }
        });
    }

    private static void ComposeComputedColumnsGrid(ColumnDescriptor col, IReadOnlyList<SchemaColumn> computed)
    {
        col.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
                columns.RelativeColumn(5);
            });
            grid.Header(header =>
            {
                HeaderCell(header, "Column");
                HeaderCell(header, "Type");
                HeaderCell(header, "Expression");
            });
            var i = 1;
            foreach (var c in computed)
            {
                var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                DataCell(grid, c.Name, bg);
                DataCell(grid, c.DataType, bg);
                grid.Cell().Background(bg).BorderBottom(1).BorderColor(BorderColor).Padding(4)
                    .Text(c.ComputedExpression ?? "").FontSize(8).FontFamily("Courier New");
                i++;
            }
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DB-level sections: triggers, stored procedures, functions
    // ──────────────────────────────────────────────────────────────────────────

    private void ComposeTriggersSection(ColumnDescriptor col)
    {
        var triggers = _schema.Triggers ?? Array.Empty<SchemaTrigger>();
        if (triggers.Count == 0) return;

        col.Item().PaddingBottom(15)
            .Text("Triggers")
            .FontSize(22).Bold().FontColor(PrimaryColor);
        col.Item().Height(2).Background(AccentColor);
        col.Item().PaddingTop(12);

        col.Item().Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);   // Name
                columns.RelativeColumn(3);   // Table
                columns.ConstantColumn(70);  // Timing
                columns.ConstantColumn(110); // Event
            });
            grid.Header(header =>
            {
                HeaderCell(header, "Name");
                HeaderCell(header, "Table");
                HeaderCell(header, "Timing");
                HeaderCell(header, "Event");
            });
            var i = 1;
            foreach (var t in triggers)
            {
                var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                DataCell(grid, t.FullName, bg);
                DataCell(grid, $"{t.TableSchema}.{t.TableName}", bg);
                DataCell(grid, t.Timing, bg);
                DataCell(grid, t.Event, bg);
                i++;
            }
        });
    }

    private void ComposeStoredProceduresSection(ColumnDescriptor col)
    {
        if (_schema.StoredProcedures.Count == 0) return;

        col.Item().PaddingBottom(15)
            .Text("Stored Procedures")
            .FontSize(22).Bold().FontColor(PrimaryColor);
        col.Item().Height(2).Background(AccentColor);
        col.Item().PaddingTop(12);

        foreach (var p in _schema.StoredProcedures)
        {
            ComposeRoutineBlock(col,
                title: p.FullName,
                kindLabel: "PROCEDURE",
                returnType: null,
                parameters: p.Parameters,
                dependencies: p.Dependencies,
                body: p.Definition);
        }
    }

    private void ComposeFunctionsSection(ColumnDescriptor col)
    {
        var functions = _schema.Functions ?? Array.Empty<SchemaFunction>();
        if (functions.Count == 0) return;

        col.Item().PaddingBottom(15)
            .Text("Functions")
            .FontSize(22).Bold().FontColor(PrimaryColor);
        col.Item().Height(2).Background(AccentColor);
        col.Item().PaddingTop(12);

        foreach (var f in functions)
        {
            var kindLabel = f.Kind switch
            {
                FunctionKind.Scalar => "FUNCTION (scalar)",
                FunctionKind.InlineTable => "FUNCTION (inline table-valued)",
                FunctionKind.TableValued => "FUNCTION (table-valued)",
                _ => "FUNCTION"
            };
            ComposeRoutineBlock(col,
                title: f.FullName,
                kindLabel: kindLabel,
                returnType: f.ReturnType,
                parameters: f.Parameters,
                dependencies: f.Dependencies,
                body: f.Definition);
        }
    }

    /// <summary>
    /// Renders one procedure or function: header, parameters table, dependencies, body.
    /// Body is truncated to keep the PDF manageable.
    /// </summary>
    private void ComposeRoutineBlock(
        ColumnDescriptor col,
        string title,
        string kindLabel,
        string? returnType,
        IReadOnlyList<ProcParameter> parameters,
        IReadOnlyList<ObjectDependency>? dependencies,
        string? body)
    {
        col.Item().PaddingTop(8).PaddingBottom(2)
            .Text(t =>
            {
                t.Span(title).FontSize(13).Bold().FontColor(PrimaryColor);
                t.Span("    " + kindLabel).FontSize(8).FontColor(MediumGray);
            });
        if (!string.IsNullOrEmpty(returnType))
        {
            col.Item().PaddingBottom(4).Text(t =>
            {
                t.Span("Returns: ").FontSize(9).FontColor(MediumGray);
                t.Span(returnType).FontSize(9).FontFamily("Courier New").FontColor("#4B5563");
            });
        }

        if (parameters.Count > 0)
        {
            col.Item().PaddingTop(2).PaddingBottom(2).Text("Parameters").FontSize(10).Bold().FontColor(PrimaryColor);
            col.Item().Table(grid =>
            {
                grid.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(70);
                });
                grid.Header(header =>
                {
                    HeaderCell(header, "Name");
                    HeaderCell(header, "Type");
                    HeaderCell(header, "Direction");
                    HeaderCell(header, "Optional");
                });
                var i = 1;
                foreach (var p in parameters)
                {
                    var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                    DataCell(grid, p.Name, bg);
                    DataCell(grid, p.DataType, bg);
                    DataCell(grid, p.Direction, bg);
                    DataCell(grid, p.IsOptional ? "Yes" : "No", bg);
                    i++;
                }
            });
        }

        if (dependencies is { Count: > 0 })
        {
            col.Item().PaddingTop(6).PaddingBottom(2).Text("Dependencies").FontSize(10).Bold().FontColor(PrimaryColor);
            col.Item().Table(grid =>
            {
                grid.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(80);
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(3);
                });
                grid.Header(header =>
                {
                    HeaderCell(header, "Kind");
                    HeaderCell(header, "Object");
                    HeaderCell(header, "Column");
                });
                var i = 1;
                foreach (var d in dependencies.OrderBy(d => d.Kind).ThenBy(d => d.ReferencedSchema).ThenBy(d => d.ReferencedObject).ThenBy(d => d.ReferencedColumn))
                {
                    var bg = i % 2 == 0 ? LightGray : "#FFFFFF";
                    DataCell(grid, d.Kind, bg);
                    DataCell(grid, string.IsNullOrEmpty(d.ReferencedSchema) ? d.ReferencedObject : $"{d.ReferencedSchema}.{d.ReferencedObject}", bg);
                    DataCell(grid, d.ReferencedColumn ?? "", bg);
                    i++;
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            col.Item().PaddingTop(6).PaddingBottom(2).Text("Definition").FontSize(10).Bold().FontColor(PrimaryColor);
            const int MaxBodyChars = 4000;
            var rendered = body.Length > MaxBodyChars
                ? body.Substring(0, MaxBodyChars) + $"\n-- … truncated ({body.Length - MaxBodyChars} more chars)"
                : body;
            col.Item().Background("#0F172A").Padding(8)
                .Text(rendered).FontSize(7).FontFamily("Courier New").FontColor("#A7F3D0");
        }

        col.Item().PaddingBottom(6).Height(1).Background(BorderColor);
    }
}
