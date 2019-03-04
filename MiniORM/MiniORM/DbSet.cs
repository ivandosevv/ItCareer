namespace MiniORM
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    public class DbSet<T> : ICollection<T>
        where T : class, new()
    {
        private readonly IList<T> _entities;

        internal IReadOnlyCollection<T> Entities => new ReadOnlyCollection<T>(this._entities);
        internal ChangeTracker<T> ChangeTracker { get; }

        public int Count => this._entities.Count;
        public bool IsReadOnly => this._entities.IsReadOnly;

        internal DbSet(IEnumerable<T> entites)
        {
            this._entities = new List<T>(entites);
            this.ChangeTracker = new ChangeTracker<T>(entites);
        }

        public void Add(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item), "Item cannot be null!");

            this._entities.Add(item);
            this.ChangeTracker.Add(item);
        }

        public bool Remove(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item), "Item cannot be null!");

            bool isRemoved = this._entities.Remove(item);

            if (isRemoved)
                this.ChangeTracker.Remove(item);

            return isRemoved;
        }

        public void RemoveRange(IEnumerable<T> items)
        {
            foreach (T item in items)
                this.Remove(item);
        }

        public void Clear()
        {
            for (int i = 0; i < this.Count; i++)
                this.Remove(this._entities[i]);
        }

        public bool Contains(T item)
            => this._entities
                .Contains(item);

        public void CopyTo(T[] array, int arrayIndex)
            => this._entities
                .CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator()
            => this._entities
                .GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => this.GetEnumerator();
    }
}