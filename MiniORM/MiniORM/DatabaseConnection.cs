namespace MiniORM
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Data.SqlClient;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Used for accessing a database, inserting/updating/deleting entities
    /// and mapping database columns to entity classes.
    /// </summary>
    internal class DatabaseConnection
    {
        private readonly SqlConnection connection;

        private SqlTransaction transaction;

        public DatabaseConnection(string connectionString)
            => this.connection = new SqlConnection(connectionString);

        private SqlCommand CreateCommand(string queryText, params SqlParameter[] parameters)
        {
            SqlCommand command = new SqlCommand(queryText, this.connection, this.transaction);

            foreach (SqlParameter param in parameters)
                command.Parameters.Add(param);

            return command;
        }

        public int ExecuteNonQuery(string queryText, params SqlParameter[] parameters)
        {
            using (var query = this.CreateCommand(queryText, parameters))
            {
                var result = query.ExecuteNonQuery();

                return result;
            }
        }

        public IEnumerable<string> FetchColumnNames(string tableName)
        {
            var rows = new List<string>();

            var queryText = $@"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}'";

            using (var query = this.CreateCommand(queryText))
            {
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var column = reader.GetString(0);

                        rows.Add(column);
                    }
                }
            }

            return rows;
        }

        public IEnumerable<T> ExecuteQuery<T>(string queryText)
        {
            var rows = new List<T>();

            using (var query = this.CreateCommand(queryText))
            {
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var columnValues = new object[reader.FieldCount];
                        reader.GetValues(columnValues);

                        var obj = reader.GetFieldValue<T>(0);
                        rows.Add(obj);
                    }
                }
            }

            return rows;
        }

        public IEnumerable<T> FetchResultSet<T>(string tableName, params string[] columnNames)
        {
            var rows = new List<T>();

            var escapedColumns = string.Join(", ", columnNames.Select(EscapeColumn));
            var queryText = $@"SELECT {escapedColumns} FROM {tableName}";

            using (var query = this.CreateCommand(queryText))
            {
                using (var reader = query.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var columnValues = new object[reader.FieldCount];
                        reader.GetValues(columnValues);

                        var obj = MapColumnsToObject<T>(columnNames, columnValues);
                        rows.Add(obj);
                    }
                }
            }

            return rows;
        }

        public void InsertEntities<T>(IEnumerable<T> entities, string tableName, string[] columns)
            where T : class
        {
            var identityColumns = this.GetIdentityColumns(tableName);

            var columnsToInsert = columns.Except(identityColumns).ToArray();

            var escapedColumns = columnsToInsert.Select(EscapeColumn).ToArray();

            var rowValues = entities
                .Select(entity => columnsToInsert
                    .Select(c => entity.GetType().GetProperty(c).GetValue(entity))
                    .ToArray())
                .ToArray();

            var rowParameterNames = Enumerable.Range(1, rowValues.Length)
                .Select(i => columnsToInsert.Select(c => c + i).ToArray())
                .ToArray();

            var sqlColumns = string.Join(", ", escapedColumns);

            var sqlRows = string.Join(", ",
                rowParameterNames.Select(p =>
                    string.Format("({0})",
                        string.Join(", ", p.Select(a => $"@{a}")))));

            var query = string.Format(
                "INSERT INTO {0} ({1}) VALUES {2}",
                tableName,
                sqlColumns,
                sqlRows
            );

            var parameters = rowParameterNames
                .Zip(rowValues, (@params, values) =>
                    @params.Zip(values, (param, value) =>
                        new SqlParameter(param, value ?? DBNull.Value)))
                .SelectMany(p => p)
                .ToArray();

            var insertedRows = this.ExecuteNonQuery(query, parameters);

            if (insertedRows != entities.Count())
            {
                throw new InvalidOperationException($"Could not insert {entities.Count() - insertedRows} rows.");
            }
        }

        public void UpdateEntities<T>(IEnumerable<T> modifiedEntities, string tableName, string[] columns)
            where T : class
        {
            var identityColumns = this.GetIdentityColumns(tableName);

            var columnsToUpdate = columns.Except(identityColumns).ToArray();

            var primaryKeyProperties = typeof(T).GetProperties()
                .Where(pi => pi.HasAttribute<KeyAttribute>())
                .ToArray();

            foreach (var entity in modifiedEntities)
            {
                var primaryKeyValues = primaryKeyProperties
                    .Select(c => c.GetValue(entity))
                    .ToArray();

                var primaryKeyParameters = primaryKeyProperties
                    .Zip(primaryKeyValues, (param, value) => new SqlParameter(param.Name, value))
                    .ToArray();

                var rowValues = columnsToUpdate
                    .Select(c => entity.GetType().GetProperty(c).GetValue(entity) ?? DBNull.Value)
                    .ToArray();

                var columnsParameters = columnsToUpdate.Zip(rowValues, (param, value) => new SqlParameter(param, value))
                    .ToArray();

                var columnsSql = string.Join(", ",
                    columnsToUpdate.Select(c => $"{c} = @{c}"));

                var primaryKeysSql = string.Join(" AND ",
                    primaryKeyProperties.Select(pk => $"{pk.Name} = @{pk.Name}"));

                var query = string.Format("UPDATE {0} SET {1} WHERE {2}",
                    tableName,
                    columnsSql,
                    primaryKeysSql);

                var updatedRows = this.ExecuteNonQuery(query, columnsParameters.Concat(primaryKeyParameters).ToArray());

                if (updatedRows != 1)
                {
                    throw new InvalidOperationException($"Update for table {tableName} failed.");
                }
            }
        }

        public void DeleteEntities<T>(IEnumerable<T> entitiesToDelete, string tableName)
            where T : class
        {
            PropertyInfo[] primaryKeyProperties = typeof(T).GetProperties()
                .Where(pi => pi.HasAttribute<KeyAttribute>())
                .ToArray();

            foreach (T entity in entitiesToDelete)
            {
                object[] primaryKeyValues = primaryKeyProperties
                    .Select(c => c.GetValue(entity))
                    .ToArray();

                SqlParameter[] primaryKeyParameters = primaryKeyProperties
                    .Zip(primaryKeyValues, (property, value) => new SqlParameter(property.Name, value))
                    .ToArray();

                string primaryKeysSql = string.Join(" AND ",
                    primaryKeyProperties.Select(pk => $"{pk.Name} = @{pk.Name}"));

                string query = string.Format("DELETE FROM {0} WHERE {1}",
                    tableName,
                    primaryKeysSql);

                int updatedRows = this.ExecuteNonQuery(query, primaryKeyParameters);

                if (updatedRows != 1)
                    throw new InvalidOperationException($"Delete for table {tableName} failed.");
            }
        }

        private IEnumerable<string> GetIdentityColumns(string tableName)
        {
            const string identityColumnsSql =
                "SELECT COLUMN_NAME FROM (SELECT COLUMN_NAME, COLUMNPROPERTY(OBJECT_ID(TABLE_NAME), COLUMN_NAME, 'IsIdentity') AS IsIdentity FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{0}') AS IdentitySpecs WHERE IsIdentity = 1";

            string parametrizedSql = string.Format(identityColumnsSql, tableName);

            IEnumerable<string> identityColumns = this.ExecuteQuery<string>(parametrizedSql);

            return identityColumns;
        }

        public SqlTransaction StartTransaction()
        {
            this.transaction = this.connection.BeginTransaction();
            return this.transaction;
        }

        public void Open()
            => this.connection.Open();

        public void Close()
            => this.connection.Close();

        private static string EscapeColumn(string c)
            => $"[{c}]";

        private static T MapColumnsToObject<T>(string[] columnNames, object[] columns)
        {
            T obj = Activator.CreateInstance<T>();

            for (int i = 0; i < columns.Length; i++)
            {
                string columnName = columnNames[i];
                object columnValue = columns[i];

                if (columnValue is DBNull)
                    columnValue = null;

                PropertyInfo property = typeof(T).GetProperty(columnName);
                property.SetValue(obj, columnValue);
            }

            return obj;
        }
    }
}