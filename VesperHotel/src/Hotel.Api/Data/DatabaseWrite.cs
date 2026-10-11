using Hotel.Api.Models;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Hotel.Api.Data;

// Locks live in the database, so separate API/worker processes obey the same rules.
// Every inventory mutation takes this lock before reading mutable inventory.
public class DatabaseWrite : IAsyncDisposable
{
    private readonly IDbContextTransaction transaction;
    private readonly DbTransaction? sqliteTransaction;
    private DatabaseWrite(IDbContextTransaction transaction, DbTransaction? sqliteTransaction)
    { this.transaction = transaction; this.sqliteTransaction = sqliteTransaction; }

    public static async Task<DatabaseWrite> BeginAsync(HotelDbContext db, string? resourceId = null, CancellationToken cancellationToken = default, string? userId = null)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        DatabaseWrite scope;
        if (db.Database.IsSqlite())
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            // deferred:false means BEGIN IMMEDIATE, acquiring the write reservation now.
            var raw = connection.BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: false);
            var wrapped = await db.Database.UseTransactionAsync(raw, cancellationToken);
            scope = new DatabaseWrite(wrapped!, raw);
        }
        else
        {
            var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
            scope = new DatabaseWrite(transaction, null);
        }
        if (userId != null && !db.Database.IsSqlite())
        {
            // Always lock user before resource. This also serializes the user's idempotency keys across resources.
            await using var userCommand = db.Database.GetDbConnection().CreateCommand();
            userCommand.Transaction = scope.transaction.GetDbTransaction();
            userCommand.CommandText = "SELECT Id FROM AspNetUsers WHERE Id = @userId FOR UPDATE";
            var userParameter = userCommand.CreateParameter(); userParameter.ParameterName = "@userId"; userParameter.Value = userId;
            userCommand.Parameters.Add(userParameter);
            await userCommand.ExecuteScalarAsync(cancellationToken);
        }
        if (resourceId != null && !db.Database.IsSqlite())
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = scope.transaction.GetDbTransaction();
            command.CommandText = "SELECT Id FROM Resources WHERE Id = @resourceId FOR UPDATE";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@resourceId"; parameter.Value = resourceId;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(cancellationToken);
        }
        return scope;
    }
    public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        if (sqliteTransaction != null) await sqliteTransaction.DisposeAsync();
    }
}
