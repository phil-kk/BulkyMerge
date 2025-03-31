using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FastMember;
using System;

namespace BulkyMerge.Root;

public record ColumnInfo(string Name, string DataType, bool IsIdentity, bool IsPrimaryKey);

public class MergeOptions
{
    public string Schema { get; set; }
    public string TableName { get; set; }
    public int BatchSize { get; set; }
    public IEnumerable<string> ExcludeProperties { get; set; }
    public IEnumerable<string> PrimaryKeys { get; set; }
    public int Timeout { get; set; } = int.MaxValue;
    public string IdentityColumnName { get; set; }
    public bool MapOutputIdentity { get; set; }
}

internal static partial class BulkExtensions
{
    private static async Task<MergeContext<T>> BuildContextAsync<T>(ISqlDialect sqlDialect,
            DbConnection connection,
            IEnumerable<T> items,
            DbTransaction transaction,
            MergeOptions options)
    {
        var type = typeof(T);
        var typeAccessor = TypeAccessor.Create(type);
        var tableAttribute = type.GetCustomAttribute<TableAttribute>(true);
        var tableName = options.TableName ?? tableAttribute?.Name ?? type.Name;
        var memberSet = typeAccessor.GetMembers().Where(x => x.GetAttribute(typeof(NotMappedAttribute), true) is null).ToList();
        var primaryKeys = options.PrimaryKeys ?? memberSet.Where(x => x.GetAttribute(typeof(KeyAttribute), true) is not null).Select(x =>
            x.GetAttribute(typeof(ColumnAttribute), true) is ColumnAttribute column ? column?.Name : x.Name).ToList();
        var columns = sqlDialect != null ? await FindColumnsAsync(sqlDialect, connection, transaction, tableName) : null;
        if (!primaryKeys.Any())
        {
            primaryKeys = columns?.Where(x => x.IsPrimaryKey).Select(x => x.Name);
        }

        var columnsToProperties = memberSet.ToDictionary(x =>
            x.GetAttribute(typeof(ColumnAttribute), true) is ColumnAttribute column ? column?.Name : x.Name);
        var tempTableName = sqlDialect?.GetTempTableName(tableName);
        return new MergeContext<T>(connection,
            transaction,
            items,
            typeAccessor,
            tableName,
            options.Schema ?? tableAttribute?.Schema ?? sqlDialect.DefaultScheme,
            tempTableName,
            columnsToProperties.Where(x => options.ExcludeProperties?.Any(c => c == x.Key) != true).ToDictionary(),
            columns?.ToDictionary(x => x.Name?.ToLower()),
            columns?.FirstOrDefault(x => x.IsIdentity),
            primaryKeys.ToList(),
            options.BatchSize,
            options.Timeout);
    }

    internal static async Task BulkCopyAsync<T>(IBulkWriter bulkWriter, DbConnection connection,
        DbTransaction transaction,
        IEnumerable<T> items,
        string tableName = default,
        IEnumerable<string> excludeColumns = default,
        int timeout = int.MaxValue,
        int batchSize = DefaultBatchSize)
    {
        var shouldCloseConnection =  await OpenConnectionAsync(connection);

        var context = await BuildContextAsync(null, connection, items, transaction,
            new MergeOptions
            {
                TableName = tableName,
                BatchSize = batchSize,
                Timeout = timeout,
                MapOutputIdentity = false
            } );
        await bulkWriter.WriteAsync(context.TableName, context);
        if (shouldCloseConnection) await connection.CloseAsync();
    }

     internal static Task BulkInsertOrUpdateAsync<T>(IBulkWriter bulkWriter, ISqlDialect dialect, DbConnection connection,
            IEnumerable<T> items,
            string tableName = default,
            DbTransaction transaction = default,
            int batchSize = DefaultBatchSize,
            IEnumerable<string> excludeProperties = default,
            IEnumerable<string> primaryKeys = default,
            int timeout = int.MaxValue,
            bool mapIdentity = true)
    => ExecuteInternalAsync(
            (dialect, context) => dialect.GetInsertOrUpdateMergeStatement(context.ColumnsToProperty.Keys, context.TableName, context.TempTableName, context.PrimaryKeys, context.Identity),
            bulkWriter, dialect, connection, items, tableName, transaction, batchSize, excludeProperties, primaryKeys, timeout,  mapIdentity);

    internal static async Task ExecuteInternalAsync<T>(
            Func<ISqlDialect, MergeContext<T>, string> dialectCall,
            IBulkWriter bulkWriter, 
            ISqlDialect dialect, 
            DbConnection connection,
            IEnumerable<T> items,
            string tableName = default,
            DbTransaction transaction = default,
            int batchSize = DefaultBatchSize,
            IEnumerable<string> excludeProperties = default,
            IEnumerable<string> primaryKeys = default,
            int timeout = int.MaxValue,
            bool mapIdentity = true)
    {
        var shouldCloseConnection = await OpenConnectionAsync(connection);

        var context = await BuildContextAsync(dialect, connection, items, transaction,
            new MergeOptions
            {
                TableName = tableName,
                ExcludeProperties = excludeProperties,
                PrimaryKeys = primaryKeys,
                Timeout = timeout,
                MapOutputIdentity = mapIdentity
            });

        await WriteToTempAsync(bulkWriter,
            dialect,
            context);

        var merge = dialectCall(dialect, context);

        if (!mapIdentity || context.Identity is null)
        {
            await ExecuteAsync(connection, merge, transaction);
            if (shouldCloseConnection) await connection.CloseAsync();
            return;
        }

        await using (var reader = await ExecuteReaderAsync(connection, merge, transaction))
        {
            await MapIdentityAsync(reader, context);
        }
        if (shouldCloseConnection) await connection.CloseAsync();
    }
     
     private static async Task WriteToTempAsync<T>(IBulkWriter bulkWriter, 
         ISqlDialect dialect,
         MergeContext<T> context,
         bool excludePrimaryKeys = false)
     {
         await CreateTemporaryTableAsync(dialect, context.Connection, context.Transaction, context.Identity, context.TableName, context.TempTableName, context.ColumnsToProperty.Select(x => x.Key));
         await bulkWriter.WriteAsync(context.TempTableName, context);
     }
    internal static Task BulkInsertAsync<T>(IBulkWriter bulkWriter, ISqlDialect dialect, 
         DbConnection connection,
         IEnumerable<T> items,
         string tableName = default,
         DbTransaction transaction = default,
         int batchSize = DefaultBatchSize,
         string[] excludeProperties = default,
         IEnumerable<string> primaryKeys = default,
         int timeout = int.MaxValue,
         bool mapIdentity = true)
     => ExecuteInternalAsync(
            (dialect, context) => dialect.GetInsertQuery(context.ColumnsToProperty.Keys, context.TableName, context.TempTableName, context.PrimaryKeys, context.Identity),
            bulkWriter, dialect, connection, items, tableName, transaction, batchSize, excludeProperties, primaryKeys, timeout,  mapIdentity);

    internal static Task BulkUpdateAsync<T>(IBulkWriter bulkWriter, ISqlDialect dialect, DbConnection connection,
         IEnumerable<T> items,
         string tableName = default,
         DbTransaction transaction = default,
         int batchSize = DefaultBatchSize,
         string[] excludeProperties = default,
         IEnumerable<string> primaryKeys = default,
         int timeout = int.MaxValue)
     => ExecuteInternalAsync(
            (dialect, context) => dialect.GetUpdateQuery(context.ColumnsToProperty.Keys, context.TableName, context.TempTableName, context.PrimaryKeys, context.Identity),
            bulkWriter, dialect, connection, items, tableName, transaction, batchSize, excludeProperties, primaryKeys, timeout, false);

    internal static Task BulkDeleteAsync<T>(IBulkWriter bulkWriter, 
         ISqlDialect dialect, 
         DbConnection connection,
         IEnumerable<T> items,
         string tableName = default,
         DbTransaction transaction = default,
         int batchSize = DefaultBatchSize,
         IEnumerable<string> primaryKeys = default,
         int timeout = int.MaxValue)
     => ExecuteInternalAsync(
            (dialect, context) => dialect.GetDeleteQuery(context.TableName, context.TempTableName, context.PrimaryKeys, context.Identity),
            bulkWriter, dialect, connection, items, tableName, transaction, batchSize, null, primaryKeys, timeout, false);

    private static Task CreateTemporaryTableAsync(ISqlDialect dialect, DbConnection connection, 
        DbTransaction transaction, 
        ColumnInfo identity, 
        string tableName, 
        string tempTableName, 
        IEnumerable<string> columnNames = default)
    {
        var queryString = new StringBuilder(dialect.GetCreateTempTableQuery(tempTableName, tableName, columnNames));
        if (identity is not null)
        {
            queryString.AppendLine(dialect.GetAlterIdentityColumnQuery(tempTableName, identity));
        }

        return ExecuteAsync(connection, queryString.ToString(), transaction);
    }

    private static async Task<List<ColumnInfo>> FindColumnsAsync(ISqlDialect dialect, DbConnection connection, DbTransaction transaction, string tableName)
    {
        var result = new List<ColumnInfo>();
        await using var reader = await ExecuteReaderAsync(connection, dialect.GetColumnsQuery(connection.Database, tableName), transaction);
        while (await reader.ReadAsync())
        {
            result.Add(new ColumnInfo(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1, reader.GetInt32(3) == 1));
        }
        return result;
    }
    
    private static async Task<bool> OpenConnectionAsync(DbConnection connection)
    {
        if (connection.State is ConnectionState.Open) return false;
        await connection.OpenAsync();
        return true;
    }
    
    private static async Task ExecuteAsync(DbConnection connection, string sql, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync();
    }
    
    private static async Task<DbDataReader> ExecuteReaderAsync(DbConnection connection, string sql, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return await command.ExecuteReaderAsync();
    }

    private static async Task MapIdentityAsync<T>(DbDataReader reader, MergeContext<T> context)
    {
        if (context.Identity is null) return;
        var identityTypeCacheItem = context.ColumnsToProperty.Where(x =>
            x.Key.Equals(context.Identity.Name, StringComparison.InvariantCultureIgnoreCase)).Select(x => x.Value).First();
        var identityType = identityTypeCacheItem.Type;
        var defaultIdentityValue = !identityType.IsGenericType ? Activator.CreateInstance(identityType) : null;
        var identityName = identityTypeCacheItem.Name;
        foreach (var item in context.Items)
        {
            var value = context.TypeAccessor[item, identityName];
            if (!object.Equals(defaultIdentityValue, value))
                continue;
            if (await reader.ReadAsync())
            {
                context.TypeAccessor[item, identityName] = System.Convert.ChangeType(reader[0], identityType);
            }
        }
    }
}