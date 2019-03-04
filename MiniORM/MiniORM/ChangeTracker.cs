using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace MiniORM
{
    internal class ChangeTracker<T>
        where T : class, new()
    {
        private readonly IList<T> _entities;
        private readonly IList<T> _addedEntities = new List<T>();
        private readonly IList<T> _removedEntities = new List<T>();

        public IReadOnlyCollection<T> Entities => new ReadOnlyCollection<T>(this._entities);
        public IReadOnlyCollection<T> AddedEntities => new ReadOnlyCollection<T>(this._addedEntities);
        public IReadOnlyCollection<T> RemovedEntities => new ReadOnlyCollection<T>(this._removedEntities);

        internal ChangeTracker(IEnumerable<T> enities)
            => this._entities = CloneEntities(enities);

        internal void Add(T entity)
            => this._addedEntities.Add(entity);

        internal void Remove(T entity)
            => this._removedEntities.Add(entity);

        internal IEnumerable<T> GetModifiedEntities(DbSet<T> dbSet)
        {
            ICollection<T> modifiedEntities = new List<T>();

            foreach (T originalEntity in this._entities)
            {
                T entity = dbSet.Entities
                    .SingleOrDefault(e =>
                    {
                        return GetPrimaryKeyValues(originalEntity)
                            .SequenceEqual(GetPrimaryKeyValues(e));
                    });

                if (entity != null && IsModified(originalEntity, entity))
                    modifiedEntities.Add(entity);
            }

            return modifiedEntities;
        }

        private static IList<T> CloneEntities(IEnumerable<T> entities)
        {
            IList<T> clonedEntities = new List<T>();

            PropertyInfo[] propertiesToClone = typeof(T).GetProperties()
                .Where(p => DbContext.AllowedTypes.Contains(p.PropertyType))
                .ToArray();

            foreach (T entity in entities)
            {
                T clonedEntity = Activator.CreateInstance<T>();

                foreach (PropertyInfo property in propertiesToClone)
                {
                    object value = property.GetValue(entity);
                    property.SetValue(clonedEntity, value);
                }

                clonedEntities.Add(clonedEntity);
            }

            return clonedEntities;
        }

        private static IEnumerable<object> GetPrimaryKeyValues(T entity)
            => typeof(T).GetProperties()
                .Where(p => p.HasAttribute<KeyAttribute>())
                .Select(p => p.GetValue(entity));

        private static bool IsModified(T originalEntity, T proxyEntity)
            => typeof(T).GetProperties()
                .Where(p => Equals(p.GetValue(originalEntity), p.GetValue(proxyEntity)) == false
                    && DbContext.AllowedTypes.Contains(p.PropertyType))
                .Any();
    }
}
