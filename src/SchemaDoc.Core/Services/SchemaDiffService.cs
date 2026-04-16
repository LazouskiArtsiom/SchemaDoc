using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Services;

public class SchemaDiffService
{
    /// <summary>
    /// Compares two database schemas (baseline vs. current) and returns what changed.
    /// </summary>
    public SchemaDiffResult Compare(DatabaseSchema baseline, DatabaseSchema current)
    {
        var baselineTables = baseline.Tables.ToDictionary(t => t.FullName, t => t);
        var currentTables  = current.Tables.ToDictionary(t => t.FullName, t => t);

        // Group FKs by parent table for per-table diffing
        var baselineFks = GroupForeignKeys(baseline.ForeignKeys);
        var currentFks = GroupForeignKeys(current.ForeignKeys);

        var addedTables = currentTables.Values
            .Where(t => !baselineTables.ContainsKey(t.FullName))
            .OrderBy(t => t.FullName)
            .ToList();

        var removedTables = baselineTables.Values
            .Where(t => !currentTables.ContainsKey(t.FullName))
            .OrderBy(t => t.FullName)
            .ToList();

        var modifiedTables = new List<TableDiff>();

        foreach (var key in baselineTables.Keys.Where(k => currentTables.ContainsKey(k)))
        {
            var baseTable    = baselineTables[key];
            var currentTable = currentTables[key];
            var baseFks      = baselineFks.TryGetValue(key, out var bf) ? bf : new List<ForeignKeyGroup>();
            var currFks      = currentFks.TryGetValue(key, out var cf) ? cf : new List<ForeignKeyGroup>();
            var diff         = DiffTable(baseTable, currentTable, baseFks, currFks);
            if (diff.TotalChanges > 0)
                modifiedTables.Add(diff);
        }

        // Triggers are a database-level concept, diff them separately
        var baselineTriggers = (baseline.Triggers ?? []).ToDictionary(t => t.FullName, t => t);
        var currentTriggers = (current.Triggers ?? []).ToDictionary(t => t.FullName, t => t);

        var addedTriggers = currentTriggers.Values
            .Where(t => !baselineTriggers.ContainsKey(t.FullName))
            .OrderBy(t => t.FullName).ToList();
        var removedTriggers = baselineTriggers.Values
            .Where(t => !currentTriggers.ContainsKey(t.FullName))
            .OrderBy(t => t.FullName).ToList();
        var modifiedTriggers = new List<TriggerDiff>();
        foreach (var key in baselineTriggers.Keys.Where(k => currentTriggers.ContainsKey(k)))
        {
            var changes = DiffTrigger(baselineTriggers[key], currentTriggers[key]);
            if (changes.Count > 0)
                modifiedTriggers.Add(new TriggerDiff(key, changes));
        }

        return new SchemaDiffResult(
            addedTables,
            removedTables,
            modifiedTables.OrderBy(t => t.FullName).ToList(),
            addedTriggers,
            removedTriggers,
            modifiedTriggers.OrderBy(t => t.FullName).ToList()
        );
    }

    /// <summary>Groups flat FK rows by (parentSchema.parentTable) -> ForeignKeyGroup list.</summary>
    public static Dictionary<string, List<ForeignKeyGroup>> GroupForeignKeys(IReadOnlyList<ForeignKeyRelation> fks)
    {
        return fks
            .GroupBy(f => f.ConstraintName + "|" + f.ParentSchema + "|" + f.ParentTable)
            .Select(g =>
            {
                var first = g.First();
                var parentCols = g.Select(f => f.ParentColumn).ToList();
                var refCols = g.Select(f => f.ReferencedColumn).ToList();
                return new ForeignKeyGroup(
                    first.ConstraintName,
                    first.ParentSchema,
                    first.ParentTable,
                    parentCols,
                    first.ReferencedSchema,
                    first.ReferencedTable,
                    refCols,
                    first.OnDelete,
                    first.OnUpdate);
            })
            .GroupBy(g => $"{g.ParentSchema}.{g.ParentTable}")
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static TableDiff DiffTable(
        SchemaTable baseline,
        SchemaTable current,
        List<ForeignKeyGroup> baselineFks,
        List<ForeignKeyGroup> currentFks)
    {
        // ── Column diffs ──────────────────────────────────────────
        var baselineCols = baseline.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var currentCols  = current.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var addedCols = currentCols.Values
            .Where(c => !baselineCols.ContainsKey(c.Name))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        var removedCols = baselineCols.Values
            .Where(c => !currentCols.ContainsKey(c.Name))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        var modifiedCols = new List<ColumnDiff>();
        foreach (var name in baselineCols.Keys.Where(n => currentCols.ContainsKey(n)))
        {
            var changes = DiffColumn(baselineCols[name], currentCols[name]);
            if (changes.Count > 0)
                modifiedCols.Add(new ColumnDiff(name, changes));
        }

        // ── Primary Key ──────────────────────────────────────────
        var pkChanges = DiffPrimaryKey(baseline.PrimaryKey, current.PrimaryKey);

        // ── Unique Constraints ──────────────────────────────────────
        var (addedUq, removedUq, modifiedUq) = DiffUniqueConstraints(
            baseline.UniqueConstraints, current.UniqueConstraints);

        // ── Check Constraints ──────────────────────────────────────
        var (addedCk, removedCk, modifiedCk) = DiffCheckConstraints(
            baseline.CheckConstraints, current.CheckConstraints);

        // ── Indexes ──────────────────────────────────────
        var (addedIx, removedIx, modifiedIx) = DiffIndexes(
            baseline.Indexes, current.Indexes);

        // ── Foreign Keys ──────────────────────────────────
        var (addedFk, removedFk, modifiedFk) = DiffForeignKeys(baselineFks, currentFks);

        return new TableDiff(
            baseline.Schema,
            baseline.Name,
            addedCols,
            removedCols,
            modifiedCols,
            PrimaryKeyChanges: pkChanges.Count > 0 ? pkChanges : null,
            AddedUniqueConstraints: addedUq.Count > 0 ? addedUq : null,
            RemovedUniqueConstraints: removedUq.Count > 0 ? removedUq : null,
            ModifiedUniqueConstraints: modifiedUq.Count > 0 ? modifiedUq : null,
            AddedCheckConstraints: addedCk.Count > 0 ? addedCk : null,
            RemovedCheckConstraints: removedCk.Count > 0 ? removedCk : null,
            ModifiedCheckConstraints: modifiedCk.Count > 0 ? modifiedCk : null,
            AddedIndexes: addedIx.Count > 0 ? addedIx : null,
            RemovedIndexes: removedIx.Count > 0 ? removedIx : null,
            ModifiedIndexes: modifiedIx.Count > 0 ? modifiedIx : null,
            AddedForeignKeys: addedFk.Count > 0 ? addedFk : null,
            RemovedForeignKeys: removedFk.Count > 0 ? removedFk : null,
            ModifiedForeignKeys: modifiedFk.Count > 0 ? modifiedFk : null
        );
    }

    private static (List<ForeignKeyGroup> Added, List<ForeignKeyGroup> Removed, List<ConstraintDiff> Modified)
        DiffForeignKeys(List<ForeignKeyGroup> baseline, List<ForeignKeyGroup> current)
    {
        var b = baseline.ToDictionary(f => f.ConstraintName, f => f, StringComparer.OrdinalIgnoreCase);
        var c = current.ToDictionary(f => f.ConstraintName, f => f, StringComparer.OrdinalIgnoreCase);

        var added = c.Values.Where(f => !b.ContainsKey(f.ConstraintName)).ToList();
        var removed = b.Values.Where(f => !c.ContainsKey(f.ConstraintName)).ToList();

        // Structural match for auto-generated names: same parent cols + same referenced table/cols
        string FkStructKey(ForeignKeyGroup f) =>
            $"{string.Join(",", f.ParentColumns)}→{f.ReferencedSchema}.{f.ReferencedTable}({string.Join(",", f.ReferencedColumns)})";

        foreach (var r in removed.Where(x => IsAutoGeneratedName(x.ConstraintName)).ToList())
        {
            var rKey = FkStructKey(r);
            var match = added.Where(a => IsAutoGeneratedName(a.ConstraintName)
                && FkStructKey(a) == rKey).FirstOrDefault();
            if (match is not null)
            {
                removed.Remove(r);
                added.Remove(match);
            }
        }

        var modified = new List<ConstraintDiff>();
        foreach (var name in b.Keys.Where(n => c.ContainsKey(n)))
        {
            var bf = b[name];
            var cf = c[name];
            var changes = new List<string>();

            var baseCols = string.Join(", ", bf.ParentColumns);
            var currCols = string.Join(", ", cf.ParentColumns);
            if (baseCols != currCols)
                changes.Add($"Columns: ({baseCols}) → ({currCols})");

            var baseRef = $"{bf.ReferencedSchema}.{bf.ReferencedTable}({string.Join(", ", bf.ReferencedColumns)})";
            var currRef = $"{cf.ReferencedSchema}.{cf.ReferencedTable}({string.Join(", ", cf.ReferencedColumns)})";
            if (baseRef != currRef)
                changes.Add($"References: {baseRef} → {currRef}");

            if (!StringsEq(bf.OnDelete, cf.OnDelete))
                changes.Add($"On Delete: {FormatVal(bf.OnDelete)} → {FormatVal(cf.OnDelete)}");

            if (!StringsEq(bf.OnUpdate, cf.OnUpdate))
                changes.Add($"On Update: {FormatVal(bf.OnUpdate)} → {FormatVal(cf.OnUpdate)}");

            if (changes.Count > 0)
                modified.Add(new ConstraintDiff(name, changes));
        }
        return (added, removed, modified);
    }

    private static List<string> DiffColumn(SchemaColumn baseline, SchemaColumn current)
    {
        var changes = new List<string>();

        string BaseType(SchemaColumn c) => c.MaxLength is not null
            ? $"{c.DataType}({c.MaxLength})" : c.DataType;

        if (!string.Equals(BaseType(baseline), BaseType(current), StringComparison.OrdinalIgnoreCase))
            changes.Add($"Type: {BaseType(baseline)} → {BaseType(current)}");

        if (baseline.IsNullable != current.IsNullable)
            changes.Add($"Nullable: {baseline.IsNullable} → {current.IsNullable}");

        if (baseline.IsPrimaryKey != current.IsPrimaryKey)
            changes.Add($"Primary key: {baseline.IsPrimaryKey} → {current.IsPrimaryKey}");

        if (baseline.IsIdentity != current.IsIdentity)
            changes.Add($"Identity: {baseline.IsIdentity} → {current.IsIdentity}");

        if (!StringsEq(baseline.DefaultValue, current.DefaultValue))
            changes.Add($"Default: {FormatVal(baseline.DefaultValue)} → {FormatVal(current.DefaultValue)}");

        if (baseline.IsComputed != current.IsComputed)
            changes.Add($"Computed: {baseline.IsComputed} → {current.IsComputed}");

        if (!StringsEq(baseline.ComputedExpression, current.ComputedExpression))
            changes.Add($"Computed expression: {FormatVal(baseline.ComputedExpression)} → {FormatVal(current.ComputedExpression)}");

        return changes;
    }

    private static List<string> DiffPrimaryKey(PrimaryKeyInfo? baseline, PrimaryKeyInfo? current)
    {
        var changes = new List<string>();
        if (baseline is null && current is null) return changes;
        if (baseline is null)
        {
            changes.Add($"Added: {current!.Name} on ({string.Join(", ", current.Columns)})");
            return changes;
        }
        if (current is null)
        {
            changes.Add($"Removed: {baseline.Name} on ({string.Join(", ", baseline.Columns)})");
            return changes;
        }

        // If both names are auto-generated, don't flag the name itself as a change —
        // only the underlying structure (columns, type) matters semantically.
        var bothAutoGen = IsAutoGeneratedName(baseline.Name) && IsAutoGeneratedName(current.Name);
        if (!bothAutoGen && !string.Equals(baseline.Name, current.Name, StringComparison.OrdinalIgnoreCase))
            changes.Add($"Name: {baseline.Name} → {current.Name}");

        var baseCols = string.Join(", ", baseline.Columns);
        var currCols = string.Join(", ", current.Columns);
        if (baseCols != currCols)
            changes.Add($"Columns: ({baseCols}) → ({currCols})");

        if (!StringsEq(baseline.IndexType, current.IndexType))
            changes.Add($"Type: {FormatVal(baseline.IndexType)} → {FormatVal(current.IndexType)}");

        return changes;
    }

    /// <summary>
    /// Matches SQL Server / PG / MySQL auto-generated constraint names so diffs ignore
    /// cosmetic name-only differences between otherwise-identical constraints.
    /// Patterns: PK__Table__HEX, FK__T1__Col__HEX, SYS_C0012345, schema_pkey, etc.
    /// </summary>
    public static bool IsAutoGeneratedName(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        // SQL Server: PK__table__HEX (8+ hex)
        if (System.Text.RegularExpressions.Regex.IsMatch(name,
            @"^(PK|UQ|CK|FK|DF|IX)__.+__[0-9A-F]{8,}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;
        // PostgreSQL: table_pkey, table_colname_key, table_colname_fkey, table_colname_check
        if (System.Text.RegularExpressions.Regex.IsMatch(name,
            @"(_pkey|_key|_fkey|_check|_idx)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;
        // Oracle-style: SYS_C001234
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^SYS_C\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    private static (List<UniqueConstraint> Added, List<UniqueConstraint> Removed, List<ConstraintDiff> Modified)
        DiffUniqueConstraints(IReadOnlyList<UniqueConstraint>? baseline, IReadOnlyList<UniqueConstraint>? current)
    {
        var bList = (baseline ?? []).ToList();
        var cList = (current ?? []).ToList();

        // First pass: match by name
        var b = bList.ToDictionary(u => u.Name, u => u, StringComparer.OrdinalIgnoreCase);
        var c = cList.ToDictionary(u => u.Name, u => u, StringComparer.OrdinalIgnoreCase);

        var added = c.Values.Where(u => !b.ContainsKey(u.Name)).ToList();
        var removed = b.Values.Where(u => !c.ContainsKey(u.Name)).ToList();

        // Second pass: among remaining adds/removes with auto-gen names,
        // match by structure (column list). Those pairs are semantically equivalent.
        var matchByStruct = new List<(UniqueConstraint Removed, UniqueConstraint Added)>();
        foreach (var r in removed.Where(x => IsAutoGeneratedName(x.Name)).ToList())
        {
            var rKey = string.Join(",", r.Columns);
            var match = added.Where(a => IsAutoGeneratedName(a.Name)
                && string.Join(",", a.Columns) == rKey).FirstOrDefault();
            if (match is not null)
            {
                matchByStruct.Add((r, match));
                removed.Remove(r);
                added.Remove(match);
            }
        }

        var modified = new List<ConstraintDiff>();
        foreach (var name in b.Keys.Where(n => c.ContainsKey(n)))
        {
            var baseCols = string.Join(", ", b[name].Columns);
            var currCols = string.Join(", ", c[name].Columns);
            if (baseCols != currCols)
                modified.Add(new ConstraintDiff(name, [$"Columns: ({baseCols}) → ({currCols})"]));
        }
        return (added, removed, modified);
    }

    private static (List<CheckConstraint> Added, List<CheckConstraint> Removed, List<ConstraintDiff> Modified)
        DiffCheckConstraints(IReadOnlyList<CheckConstraint>? baseline, IReadOnlyList<CheckConstraint>? current)
    {
        var b = (baseline ?? []).ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var c = (current ?? []).ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var added = c.Values.Where(ck => !b.ContainsKey(ck.Name)).ToList();
        var removed = b.Values.Where(ck => !c.ContainsKey(ck.Name)).ToList();

        // Structural match for auto-generated names
        foreach (var r in removed.Where(x => IsAutoGeneratedName(x.Name)).ToList())
        {
            var match = added.Where(a => IsAutoGeneratedName(a.Name)
                && NormalizeExpr(a.Expression) == NormalizeExpr(r.Expression)).FirstOrDefault();
            if (match is not null)
            {
                removed.Remove(r);
                added.Remove(match);
            }
        }

        var modified = new List<ConstraintDiff>();
        foreach (var name in b.Keys.Where(n => c.ContainsKey(n)))
        {
            if (NormalizeExpr(b[name].Expression) != NormalizeExpr(c[name].Expression))
                modified.Add(new ConstraintDiff(name, [$"Expression: {b[name].Expression} → {c[name].Expression}"]));
        }
        return (added, removed, modified);
    }

    private static string NormalizeExpr(string? expr) =>
        (expr ?? "").Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "");

    private static (List<SchemaIndex> Added, List<SchemaIndex> Removed, List<IndexDiff> Modified)
        DiffIndexes(IReadOnlyList<SchemaIndex>? baseline, IReadOnlyList<SchemaIndex>? current)
    {
        var b = (baseline ?? []).ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);
        var c = (current ?? []).ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);

        var added = c.Values.Where(i => !b.ContainsKey(i.Name)).ToList();
        var removed = b.Values.Where(i => !c.ContainsKey(i.Name)).ToList();

        var modified = new List<IndexDiff>();
        foreach (var name in b.Keys.Where(n => c.ContainsKey(n)))
        {
            var bi = b[name];
            var ci = c[name];
            var changes = new List<string>();

            var baseCols = string.Join(", ", bi.Columns.Select(FormatIndexCol));
            var currCols = string.Join(", ", ci.Columns.Select(FormatIndexCol));
            if (baseCols != currCols)
                changes.Add($"Columns: ({baseCols}) → ({currCols})");

            var baseIncl = bi.IncludedColumns is null ? "" : string.Join(", ", bi.IncludedColumns);
            var currIncl = ci.IncludedColumns is null ? "" : string.Join(", ", ci.IncludedColumns);
            if (baseIncl != currIncl)
                changes.Add($"Included columns: ({baseIncl}) → ({currIncl})");

            if (bi.IsUnique != ci.IsUnique)
                changes.Add($"Unique: {bi.IsUnique} → {ci.IsUnique}");

            if (!StringsEq(bi.IndexType, ci.IndexType))
                changes.Add($"Type: {FormatVal(bi.IndexType)} → {FormatVal(ci.IndexType)}");

            if (!StringsEq(bi.FilterExpression, ci.FilterExpression))
                changes.Add($"Filter: {FormatVal(bi.FilterExpression)} → {FormatVal(ci.FilterExpression)}");

            if (changes.Count > 0)
                modified.Add(new IndexDiff(name, changes));
        }
        return (added, removed, modified);
    }

    private static List<string> DiffTrigger(SchemaTrigger baseline, SchemaTrigger current)
    {
        var changes = new List<string>();
        if (baseline.TableSchema != current.TableSchema || baseline.TableName != current.TableName)
            changes.Add($"Table: {baseline.TableSchema}.{baseline.TableName} → {current.TableSchema}.{current.TableName}");
        if (baseline.Event != current.Event)
            changes.Add($"Event: {baseline.Event} → {current.Event}");
        if (baseline.Timing != current.Timing)
            changes.Add($"Timing: {baseline.Timing} → {current.Timing}");
        if (!StringsEq(baseline.Definition, current.Definition))
            changes.Add("Definition changed");
        return changes;
    }

    private static string FormatIndexCol(IndexColumn c) => c.IsDescending ? $"{c.Name} DESC" : c.Name;

    private static bool StringsEq(string? a, string? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);
    }

    private static string FormatVal(string? v) => string.IsNullOrEmpty(v) ? "(none)" : v;
}
