using System.Text;
using System.Xml.Linq;
using SchemaDoc.Core.Models;

namespace SchemaDoc.Export;

public static class DrawioExporter
{
    private const int TableWidth = 260;
    private const int HeaderHeight = 30;
    private const int RowHeight = 26;
    private const int ColSpacing = 320;
    private const int RowSpacing = 60;
    private const int TablesPerRow = 4;

    public static byte[] Generate(DatabaseSchema schema)
    {
        var root = new XElement("root",
            new XElement("mxCell", new XAttribute("id", "0")),
            new XElement("mxCell", new XAttribute("id", "1"), new XAttribute("parent", "0"))
        );

        var cellId = 2;
        var tableIds = new Dictionary<string, int>(); // fullName -> mxCell id of the table container

        // Layout tables in a grid
        for (int i = 0; i < schema.Tables.Count; i++)
        {
            var table = schema.Tables[i];
            var col = i % TablesPerRow;
            var row = i / TablesPerRow;
            var x = 40 + col * ColSpacing;
            var tableHeight = HeaderHeight + table.Columns.Count * RowHeight;
            var y = 40 + row * (EstimateMaxHeight(schema.Tables, row) + RowSpacing);

            var tableId = cellId++;
            tableIds[table.FullName] = tableId;

            // Table container
            root.Add(new XElement("mxCell",
                new XAttribute("id", tableId),
                new XAttribute("value", table.FullName),
                new XAttribute("style", "shape=table;startSize=30;container=1;collapsible=0;childLayout=tableLayout;fixedRows=1;rowLines=1;fontStyle=1;align=center;resizeLast=1;fillColor=#1E3A5F;fontColor=#FFFFFF;strokeColor=#1E3A5F;fontSize=12;"),
                new XAttribute("vertex", "1"),
                new XAttribute("parent", "1"),
                new XElement("mxGeometry",
                    new XAttribute("x", x),
                    new XAttribute("y", y),
                    new XAttribute("width", TableWidth),
                    new XAttribute("height", tableHeight),
                    new XAttribute("as", "geometry"))
            ));

            // Column rows
            foreach (var column in table.Columns.OrderBy(c => c.OrdinalPosition))
            {
                var rowId = cellId++;
                var pkFk = column.IsPrimaryKey ? "PK " : column.IsForeignKey ? "FK " : "";
                var typeText = FormatType(column);
                var label = $"{pkFk}{column.Name}";
                var fillColor = column.IsPrimaryKey ? "#FEF3C7" : column.IsForeignKey ? "#DBEAFE" : "#FFFFFF";
                var fontColor = column.IsPrimaryKey ? "#92400E" : column.IsForeignKey ? "#1E40AF" : "#1A202C";

                // Row container
                root.Add(new XElement("mxCell",
                    new XAttribute("id", rowId),
                    new XAttribute("value", ""),
                    new XAttribute("style", $"shape=tableRow;horizontal=0;startSize=0;swimlaneHead=0;swimlaneBody=0;fillColor=none;collapsible=0;dropTarget=0;points=[[0,0.5],[1,0.5]];portConstraint=eastwest;fontSize=11;"),
                    new XAttribute("vertex", "1"),
                    new XAttribute("parent", tableId),
                    new XElement("mxGeometry",
                        new XAttribute("y", HeaderHeight + table.Columns.OrderBy(c => c.OrdinalPosition).ToList().IndexOf(column) * RowHeight),
                        new XAttribute("width", TableWidth),
                        new XAttribute("height", RowHeight),
                        new XAttribute("as", "geometry"))
                ));

                // Name cell (left)
                var nameId = cellId++;
                root.Add(new XElement("mxCell",
                    new XAttribute("id", nameId),
                    new XAttribute("value", label),
                    new XAttribute("style", $"shape=partialRectangle;connectable=0;fillColor={fillColor};top=0;left=0;bottom=0;right=1;fontStyle={(column.IsPrimaryKey ? "1" : "0")};fontColor={fontColor};overflow=hidden;fontSize=11;"),
                    new XAttribute("vertex", "1"),
                    new XAttribute("parent", rowId),
                    new XElement("mxGeometry",
                        new XAttribute("width", 160),
                        new XAttribute("height", RowHeight),
                        new XAttribute("as", "geometry"))
                ));

                // Type cell (right)
                var typeId = cellId++;
                root.Add(new XElement("mxCell",
                    new XAttribute("id", typeId),
                    new XAttribute("value", typeText),
                    new XAttribute("style", $"shape=partialRectangle;connectable=0;fillColor={fillColor};top=0;left=1;bottom=0;right=0;fontColor=#718096;overflow=hidden;fontSize=10;"),
                    new XAttribute("vertex", "1"),
                    new XAttribute("parent", rowId),
                    new XElement("mxGeometry",
                        new XAttribute("x", 160),
                        new XAttribute("width", TableWidth - 160),
                        new XAttribute("height", RowHeight),
                        new XAttribute("as", "geometry"))
                ));
            }
        }

        // FK relationship edges
        foreach (var fk in schema.ForeignKeys)
        {
            var parentKey = $"{fk.ParentSchema}.{fk.ParentTable}";
            var refKey = $"{fk.ReferencedSchema}.{fk.ReferencedTable}";

            if (!tableIds.ContainsKey(parentKey) || !tableIds.ContainsKey(refKey))
                continue;

            var edgeId = cellId++;
            var label = $"{fk.ParentColumn} → {fk.ReferencedColumn}";

            root.Add(new XElement("mxCell",
                new XAttribute("id", edgeId),
                new XAttribute("value", label),
                new XAttribute("style", "edgeStyle=orthogonalEdgeStyle;rounded=1;orthogonalLoop=1;jettySize=auto;exitX=1;exitY=0.5;exitDx=0;exitDy=0;entryX=0;entryY=0.5;entryDx=0;entryDy=0;fontSize=9;fontColor=#718096;strokeColor=#2563EB;strokeWidth=1;"),
                new XAttribute("edge", "1"),
                new XAttribute("parent", "1"),
                new XAttribute("source", tableIds[parentKey]),
                new XAttribute("target", tableIds[refKey]),
                new XElement("mxGeometry",
                    new XAttribute("relative", "1"),
                    new XAttribute("as", "geometry"))
            ));
        }

        var doc = new XDocument(
            new XElement("mxfile",
                new XAttribute("host", "SchemaDoc"),
                new XElement("diagram",
                    new XAttribute("name", schema.DatabaseName),
                    new XElement("mxGraphModel",
                        new XAttribute("dx", "0"),
                        new XAttribute("dy", "0"),
                        new XAttribute("grid", "1"),
                        new XAttribute("gridSize", "10"),
                        new XAttribute("guides", "1"),
                        root)
                )
            )
        );

        using var ms = new MemoryStream();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static int EstimateMaxHeight(IReadOnlyList<SchemaTable> tables, int row)
    {
        var start = row * TablesPerRow;
        var end = Math.Min(start + TablesPerRow, tables.Count);
        var maxCols = 0;
        for (var i = start; i < end; i++)
            maxCols = Math.Max(maxCols, tables[i].Columns.Count);
        return HeaderHeight + maxCols * RowHeight;
    }

    private static string FormatType(SchemaColumn col)
    {
        var t = col.DataType;
        if (!string.IsNullOrEmpty(col.MaxLength) && col.MaxLength != "-1")
            t += $"({col.MaxLength})";
        else if (col.NumericPrecision.HasValue && col.NumericScale.HasValue)
            t += $"({col.NumericPrecision},{col.NumericScale})";
        return t;
    }
}
