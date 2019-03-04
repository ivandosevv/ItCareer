namespace MiniORM
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Data.SqlClient;
    using System.Linq;
    using System.Reflection;

    public abstract class DbContext
    {
        private readonly DatabaseConnection _connection;
        private readonly Dictionary<Type, PropertyInfo> _dbSetProperties;

        internal static readonly Type[] AllowedTypes = new Type[8]
        {
            typeof(string), typeof(int), typeof(uint), typeof(long),
            typeof(ulong), typeof(decimal), typeof(bool), typeof(DateTime)
        };

        protected DbContext(string connectionString)
        {
            this._connection = new DatabaseConnection(connectionString);
            this._dbSetProperties = this.DiscoverDbSets();

            using (new ConnectionManager(this._connection))
                this.InitializeDbSets();

            this.MapAllRelations();
        }

        public void SaveChanges()
        {
            IEnumerable<object> dbSets = this._dbSetProperties
                .Select(kvp => kvp.Value.GetValue(this));

            foreach (object dbSet in dbSets)
            {
                Type dbSetType = dbSet.GetType().GetGenericArguments()[0];
                MethodInfo checkMethod = typeof(DbContext)
                    .GetMethod(nameof(CheckForInvalidEntities), BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(dbSetType);

                checkMethod.Invoke(this, new object[] { dbSet });
            }

            using (new ConnectionManager(this._connection))
            {
                using (var transaction = this._connection.StartTransaction())
                {
                    foreach (object dbSet in dbSets)
                    {
                        Type dbSetType = dbSet.GetType().GetGenericArguments().First();
                        MethodInfo persistMethod = typeof(DbContext)
                            .GetMethod(nameof(Persist), BindingFlags.Instance | BindingFlags.NonPublic)
                            .MakeGenericMethod(dbSetType);

                        try
                        {
                            persistMethod.Invoke(this, new object[] { dbSet });
                        }
                        catch (TargetInvocationException tie)
                        {
                            throw tie.InnerException;
                        }
                        catch (InvalidOperationException)
                        {

                            transaction.Rollback();
                            throw;
                        }
                        catch (SqlException)
                        {
                            transaction.Rollback();
                            throw;
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        private void CheckForInvalidEntities<T>(DbSet<T> dbSet)
            where T : class, new()
        {
            int invalidEntities = dbSet
                .Where(e => this.IsValidObject(e) == false)
                .Count();

            if (invalidEntities > 0)
            {
                string tableName = this.GetTableName(dbSet.GetType().GetGenericArguments()[0]);
                throw new InvalidOperationException(
                    $"{invalidEntities} invalid entities found in {tableName}!");
            }
        }

        private void Persist<T>(DbSet<T> dbSet)
            where T : class, new()
        {
            string tableName = this.GetTableName(typeof(T));
            string[] columns = this._connection
                .FetchColumnNames(tableName)
                .ToArray();

            if (dbSet.ChangeTracker.AddedEntities.Any())
                this._connection.InsertEntities(dbSet.ChangeTracker.AddedEntities, tableName, columns);

            IEnumerable<T> modifiedEntities = dbSet.ChangeTracker
                .GetModifiedEntities(dbSet);
            if (modifiedEntities.Any())
                this._connection.UpdateEntities(modifiedEntities, tableName, columns);

            if (dbSet.ChangeTracker.RemovedEntities.Any())
                this._connection.DeleteEntities(dbSet.ChangeTracker.RemovedEntities, tableName);
        }

        private string GetTableName(Type type)
        {
            //var tableName = ((TableAttribute)Attribute.GetCustomAttribute(type, typeof(TableAttribute)))?.Name;
            TableAttribute tableAttribute = type.GetCustomAttribute<TableAttribute>();
            if (tableAttribute == null)
                return this._dbSetProperties[type].Name;

            return tableAttribute.Name;
        }

        private bool IsValidObject(object e)
        {
            var validationContext = new ValidationContext(e);
            var validationErrors = new List<ValidationResult>();

            return Validator.TryValidateObject(e, validationContext, validationErrors, true);
        }

        private void MapAllRelations()
        {
            foreach (var dbSetProperty in this._dbSetProperties)
            {
                var dbSetType = dbSetProperty.Key;

                var mapRelationsGeneric = typeof(DbContext)
                    .GetMethod("MapRelations", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(dbSetType);

                var dbSet = dbSetProperty.Value.GetValue(this);
                mapRelationsGeneric.Invoke(this, new[] { dbSet });
            }
        }

        private void MapRelations<TEntity>(DbSet<TEntity> dbSet)
            where TEntity : class, new()
        {
            var entityType = typeof(TEntity);

            this.MapNavigationProperties(dbSet);
            var collections = entityType.GetProperties().Where(pi =>
                    pi.PropertyType.IsGenericType &&
                    pi.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>))
                .ToArray();
            foreach (var collection in collections)
            {
                var collectionType = collection.PropertyType.GenericTypeArguments.First();
                var mapCollectionMethod = typeof(DbContext)
                    .GetMethod("MapCollection", BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(entityType, collectionType);

                mapCollectionMethod.Invoke(this, new object[] { dbSet, collection });
            }
        }

        private void MapCollection<TDbSet, TCollection>(DbSet<TDbSet> dbSet, PropertyInfo collectionProperty)
             where TDbSet : class, new() where TCollection : class, new()
        {
            var entityType = typeof(TDbSet);
            var collectionType = typeof(TCollection);

            var primaryKeys = collectionType.GetProperties().Where(pi => pi.HasAttribute<KeyAttribute>()).ToArray();
            var primaryKey = primaryKeys.First();

            var foreignKey = entityType.GetProperties().First(pi => pi.HasAttribute<KeyAttribute>());
            var isManyToMany = primaryKeys.Length >= 2;
            if (isManyToMany)
            {
                primaryKey = collectionType.GetProperties().First(
                    pi => collectionType.GetProperty(pi.GetCustomAttribute<ForeignKeyAttribute>().Name).PropertyType ==
                          entityType);
            }

            var navigationDbSet = (DbSet<TCollection>)this._dbSetProperties[collectionType].GetValue(this);

            foreach (var entity in dbSet)
            {
                var primaryKeyValue = foreignKey.GetValue(entity);

                var navigationEntities = navigationDbSet
                    .Where(navigationEntity => primaryKey.GetValue(navigationEntity).Equals(primaryKeyValue)).ToArray();
                ReflectionHelper.ReplaceBackingField(entity, collectionProperty.Name, navigationEntities);
            }
        }

        private void MapNavigationProperties<TEntity>(DbSet<TEntity> dbSet)
            where TEntity : class, new()
        {
            Type entityType = typeof(TEntity);
            IEnumerable<PropertyInfo> foreignKeys = entityType.GetProperties()
                .Where(pi => pi.HasAttribute<ForeignKeyAttribute>());

            foreach (PropertyInfo foreignKey in foreignKeys)
            {
                var navigationPropertyName = foreignKey
                    .GetCustomAttribute<ForeignKeyAttribute>()
                    .Name;
                var navigationProperty = entityType.GetProperty(navigationPropertyName);

                var navigationDbSet = this._dbSetProperties[navigationProperty.PropertyType].GetValue(this);
                var navigationPrimaryKey = navigationProperty.PropertyType.GetProperties()
                    .First(pi => pi.HasAttribute<KeyAttribute>());

                foreach (var entity in dbSet)
                {
                    var foreignKeyValue = foreignKey.GetValue(entity);

                    var navogationPropertyValue = ((IEnumerable<object>)navigationDbSet)
                        .First(currentNavigationProperty =>
                            navigationPrimaryKey.GetValue(currentNavigationProperty).Equals(foreignKeyValue));

                    navigationProperty.SetValue(entity, navogationPropertyValue);
                }
            }
        }

        private void InitializeDbSets()
        {
            foreach (KeyValuePair<Type, PropertyInfo> dbSet in this._dbSetProperties)
            {
                Type dbSetType = dbSet.Key;
                PropertyInfo dbsetProperty = dbSet.Value;

                MethodInfo populateDbSetGeneric = typeof(DbContext)
                    .GetMethod(nameof(PopulateDbSet), BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(dbSetType);

                populateDbSetGeneric.Invoke(this, new object[] { dbsetProperty });
            }
        }

        private void PopulateDbSet<T>(PropertyInfo dbSet)
           where T : class, new()
        {
            IEnumerable<T> tableEntities = this.LoadTableEntities<T>();
            DbSet<T> dbSetInstance = new DbSet<T>(tableEntities);
            ReflectionHelper.ReplaceBackingField(this, dbSet.Name, dbSetInstance);
        }

        private IEnumerable<T> LoadTableEntities<T>()
            where T : class
        {
            Type table = typeof(T);

            string tableName = this.GetTableName(table);
            var columns = this.GetColumnsNames(table);

            IEnumerable<T> fetchedRows = this._connection
                .FetchResultSet<T>(tableName, columns);

            return fetchedRows;
        }

        private string[] GetColumnsNames(Type table)
        {
            string tableName = this.GetTableName(table);
            IEnumerable<string> dbColumns = this._connection
                .FetchColumnNames(tableName);

            string[] columns = table.GetProperties()
                .Where(p => dbColumns.Contains(p.Name))
                //&& p.HasAttribute<NotMappedAttribute>() == false
                //&& AllowedTypes.Contains(p.PropertyType))
                .Select(p => p.Name)
                .ToArray();

            return columns;
        }

        private Dictionary<Type, PropertyInfo> DiscoverDbSets()
            => this.GetType()
                .GetProperties()
                .Where(p => p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .ToDictionary(p => p.PropertyType.GetGenericArguments()[0], p => p);
    }
}