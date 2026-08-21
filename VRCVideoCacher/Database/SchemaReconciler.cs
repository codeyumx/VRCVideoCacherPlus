using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Serilog;

namespace VRCVideoCacher.Database;

/// <summary>
/// Adds columns that the EF model declares but the database on disk is missing.
///
/// This exists because the schema is created with EnsureCreated() and there are no
/// migrations. EnsureCreated only ever creates a database that is absent — it never touches
/// one that already exists — so a property added to History or VideoInfoCache would simply
/// not exist for anybody who had already run the application, and the first query touching
/// it would fail at runtime with "no such column" rather than at startup.
///
/// Only ever adds columns. It will not drop, rename or retype anything, so it cannot lose
/// data; a change that needs more than an added column still needs real migrations, which
/// remain the proper long-term answer.
/// </summary>
public static class SchemaReconciler
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(SchemaReconciler));

    public static void Run(DbContext db)
    {
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName) || !TableExists(db, tableName))
                continue;

            var existingColumns = GetColumns(db, tableName);
            var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var columnName = property.GetColumnName(storeObject);
                if (string.IsNullOrEmpty(columnName) || existingColumns.Contains(columnName))
                    continue;

                AddColumn(db, tableName, columnName, property);
            }
        }
    }

    private static void AddColumn(DbContext db, string tableName, string columnName, IProperty property)
    {
        var columnType = property.GetColumnType() ?? "TEXT";

        // SQLite can only add a column that is nullable or has a constant default, so a
        // non-nullable one needs a type-appropriate filler for the existing rows.
        var nullability = property.IsNullable ? "NULL" : $"NOT NULL DEFAULT {DefaultLiteralFor(columnType)}";

        try
        {
            // EF1002: DDL cannot take bound parameters for identifiers. Every value here
            // comes from the compiled EF model — table names, column names and store types
            // are all compile-time constants of this assembly, never user input.
#pragma warning disable EF1002
            db.Database.ExecuteSqlRaw($"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnType} {nullability}");
#pragma warning restore EF1002
            Log.Information("Added missing column {Table}.{Column} ({Type}).", tableName, columnName, columnType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add missing column {Table}.{Column}.", tableName, columnName);
        }
    }

    private static string DefaultLiteralFor(string columnType)
    {
        var type = columnType.ToUpperInvariant();
        if (type.Contains("INT") || type.Contains("REAL") || type.Contains("NUMERIC") ||
            type.Contains("DOUBLE") || type.Contains("FLOAT") || type.Contains("DECIMAL"))
            return "0";

        if (type.Contains("BLOB"))
            return "x''";

        return "''";
    }

    private static bool TableExists(DbContext db, string tableName) =>
        db.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master WHERE type = 'table' AND name = {0}", tableName)
            .AsEnumerable()
            .Any();

    private static HashSet<string> GetColumns(DbContext db, string tableName)
    {
        // EF1002: pragma_table_info does not accept a bound parameter for the table name.
        // tableName comes from the compiled EF model, and quotes are escaped regardless.
#pragma warning disable EF1002
        return db.Database
            .SqlQueryRaw<string>($"SELECT name AS Value FROM pragma_table_info('{tableName.Replace("'", "''")}')")
            .AsEnumerable()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
#pragma warning restore EF1002
    }
}
