using Microsoft.EntityFrameworkCore;
using SchemaDoc.Core.Interfaces;
using SchemaDoc.Core.Models;
using SchemaDoc.Infrastructure.Data;

namespace SchemaDoc.Infrastructure.Repositories;

public class AnnotationRepository(AppDbContext db) : IAnnotationRepository
{
    public async Task<TableAnnotationDto?> GetTableAnnotationAsync(
        string connectionName, string databaseName, string schemaName, string tableName)
    {
        var entity = await db.TableAnnotations
            .Include(t => t.ColumnAnnotations)
            .FirstOrDefaultAsync(t =>
                t.ConnectionName == connectionName &&
                t.DatabaseName == databaseName &&
                t.SchemaName == schemaName &&
                t.TableName == tableName);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task UpsertTableAnnotationAsync(TableAnnotationDto dto)
    {
        var entity = await db.TableAnnotations
            .Include(t => t.ColumnAnnotations)
            .FirstOrDefaultAsync(t =>
                t.ConnectionName == dto.ConnectionName &&
                t.DatabaseName == dto.DatabaseName &&
                t.SchemaName == dto.SchemaName &&
                t.TableName == dto.TableName);

        if (entity is null)
        {
            entity = new TableAnnotation
            {
                ConnectionName = dto.ConnectionName,
                DatabaseName = dto.DatabaseName,
                SchemaName = dto.SchemaName,
                TableName = dto.TableName,
            };
            db.TableAnnotations.Add(entity);
        }

        entity.Description = dto.Description;
        entity.Tags = dto.Tags;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task UpsertColumnAnnotationAsync(
        string connectionName, string databaseName, string schemaName,
        string tableName, string columnName, string? description)
    {
        // Ensure parent table annotation exists
        var tableEntity = await db.TableAnnotations
            .Include(t => t.ColumnAnnotations)
            .FirstOrDefaultAsync(t =>
                t.ConnectionName == connectionName &&
                t.DatabaseName == databaseName &&
                t.SchemaName == schemaName &&
                t.TableName == tableName);

        if (tableEntity is null)
        {
            tableEntity = new TableAnnotation
            {
                ConnectionName = connectionName,
                DatabaseName = databaseName,
                SchemaName = schemaName,
                TableName = tableName,
                UpdatedAt = DateTime.UtcNow
            };
            db.TableAnnotations.Add(tableEntity);
            await db.SaveChangesAsync();
        }

        var colEntity = tableEntity.ColumnAnnotations
            .FirstOrDefault(c => c.ColumnName == columnName);

        if (colEntity is null)
        {
            colEntity = new ColumnAnnotation { ColumnName = columnName, TableAnnotationId = tableEntity.Id };
            tableEntity.ColumnAnnotations.Add(colEntity);
        }

        colEntity.Description = description;
        colEntity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<TableAnnotationDto>> GetAllForConnectionAsync(
        string connectionName, string databaseName)
    {
        var entities = await db.TableAnnotations
            .Include(t => t.ColumnAnnotations)
            .Where(t => t.ConnectionName == connectionName && t.DatabaseName == databaseName)
            .ToListAsync();

        return entities.Select(MapToDto).ToList();
    }

    private static TableAnnotationDto MapToDto(TableAnnotation entity) =>
        new(
            ConnectionName: entity.ConnectionName,
            DatabaseName: entity.DatabaseName,
            SchemaName: entity.SchemaName,
            TableName: entity.TableName,
            Description: entity.Description,
            Tags: entity.Tags,
            ColumnAnnotations: entity.ColumnAnnotations
                .Select(c => new ColumnAnnotationDto(c.ColumnName, c.Description))
                .ToList()
        );
}
