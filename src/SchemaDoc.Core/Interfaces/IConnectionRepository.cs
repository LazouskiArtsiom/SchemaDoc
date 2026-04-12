using SchemaDoc.Core.Models;

namespace SchemaDoc.Core.Interfaces;

public interface IConnectionRepository
{
    Task<IReadOnlyList<ConnectionProfile>> GetAllAsync();
    Task<string?> GetConnectionStringAsync(int connectionId);
    Task<int> AddAsync(string name, DatabaseProvider provider, string connectionString);
    Task UpdateAsync(int id, string name, DatabaseProvider provider, string connectionString);
    Task DeleteAsync(int id);
    Task TouchLastConnectedAsync(int id, string databaseName);
}
