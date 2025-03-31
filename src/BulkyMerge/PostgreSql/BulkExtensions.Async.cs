using System.Collections.Generic;
using System.Threading.Tasks;
using BulkyMerge.Root;
using Npgsql;

namespace BulkyMerge.PostgreSql;

public static partial class NpgsqlBulkExtensions
{
    public static Task BulkCopyAsync<T>(this NpgsqlConnection connection,
        IEnumerable<T> items,
        string tableName = default,
        NpgsqlTransaction transaction = default,
        IEnumerable<string> excludeColumns = default,
        int timeout = int.MaxValue,
        int batchSize = BulkExtensions.DefaultBatchSize)
    => BulkExtensions.BulkCopyAsync(BulkWriter, connection, transaction, items, tableName, excludeColumns, timeout, batchSize);

    public static Task BulkInsertOrUpdateAsync<T>(this NpgsqlConnection connection,
        IList<T> items,
        string tableName = default,
        NpgsqlTransaction transaction = default,
        int batchSize = BulkExtensions.DefaultBatchSize,
        IEnumerable<string> excludeProperties = default,
        IEnumerable<string> primaryKeys = default,
        int timeout = int.MaxValue,
        bool mapIdentity = true)
        => BulkExtensions.BulkInsertOrUpdateAsync(BulkWriter, 
            Dialect, 
            connection, 
            items, 
            tableName, 
            transaction,
            batchSize, 
            excludeProperties, 
            primaryKeys, 
            timeout,
            mapIdentity);
     
     public static Task BulkInsertAsync<T>(this NpgsqlConnection connection,
         IList<T> items,
         string tableName = default,
         NpgsqlTransaction transaction = default,
         int batchSize =  BulkExtensions.DefaultBatchSize,
         string[] excludeProperties = default,
         IEnumerable<string> primaryKeys = default,
         int timeout = int.MaxValue,
         bool mapIdentity = true)
     => BulkExtensions.BulkInsertAsync(BulkWriter, 
         Dialect, 
         connection, 
         items, 
         tableName, 
         transaction,
         batchSize, 
         excludeProperties, 
         primaryKeys, 
         timeout,
         mapIdentity);
     
     public static Task BulkUpdateAsync<T>(this NpgsqlConnection connection,
         IEnumerable<T> items,
         string tableName = default,
         NpgsqlTransaction transaction = default,
         int batchSize = BulkExtensions.DefaultBatchSize,
         string[] excludeProperties = default,
         IEnumerable<string> primaryKeys = default,
         int timeout = int.MaxValue)
     => BulkExtensions.BulkUpdateAsync(BulkWriter, 
         Dialect, 
         connection, 
         items, 
         tableName, 
         transaction,
         batchSize, 
         excludeProperties, 
         primaryKeys, 
         timeout);
     
     public static Task BulkDeleteAsync<T>(this NpgsqlConnection connection,
         IEnumerable<T> items,
         string tableName = default,
         NpgsqlTransaction transaction = default,
         int batchSize = BulkExtensions.DefaultBatchSize,
         int bulkCopyTimeout = int.MaxValue,
         IEnumerable<string> primaryKeys = default,
         int timeout = int.MaxValue)
         => BulkExtensions.BulkDeleteAsync(BulkWriter, 
             Dialect, 
             connection, 
             items, 
             tableName, 
             transaction,
             batchSize, 
             primaryKeys, 
             timeout);
}