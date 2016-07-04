﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using SQLitePCL;

namespace owncloud_universal.Model
{
    public abstract class AbstractTableModelBase<TItem, TKey>
    {
        protected abstract TItem CreateInstance(ISQLiteStatement query);

        protected abstract string GetSelectItemQuery();
        protected abstract void BindSelectItemQuery(ISQLiteStatement query, TKey key);

        protected abstract string GetInsertItemQuery();
        protected abstract void BindInsertItemQuery(ISQLiteStatement query, TItem item);

        protected abstract string GetUpdateItemQuery();
        protected abstract void BindUpdateItemQuery(ISQLiteStatement query, TItem item, TKey key);

        protected abstract string GetDeleteItemQuery();
        protected abstract void BindDeleteItemQuery(ISQLiteStatement query, TKey key);

        protected abstract string GetSelectAllQuery();
        protected abstract void BindSelectAllQuery(ISQLiteStatement query);

        private ISQLiteConnection connection { get { return SQLiteClient.Connection; } }

        public TItem GetItem(TKey key)
        {
            using(var query = connection.Prepare(GetSelectItemQuery()))
            {
                BindSelectItemQuery(query, key);
                if(query.Step() == SQLiteResult.ROW)
                {
                    var item = CreateInstance(query);
                    return item;
                }
            }
            throw new ArgumentOutOfRangeException("Key not found");
        }

        public void InsertItem(TItem item)
        {
            using (var query = connection.Prepare(GetInsertItemQuery()))
            {
                BindInsertItemQuery(query, item);
                query.Step();
            }
        }

        public void UpdateItem(TItem item, TKey key)
        {
            using(var query = connection.Prepare(GetUpdateItemQuery()))
            {
                BindUpdateItemQuery(query, item, key);
            }
        }

        public void DeleteItem(TKey key)
        {
            using(var query = connection.Prepare(GetDeleteItemQuery()))
            {
                BindDeleteItemQuery(query, key);
                query.Step();
            }
        }

        public TItem GetLastInsertItem()
        {
            using (var query = connection.Prepare(GetLastInsertRowIdQuery()))
            {
                if (query.Step() == SQLiteResult.ROW)
                {
                    var item = GetItem((TKey)query[0]);
                    return item;
                }

            }
            throw new ArgumentOutOfRangeException("Key not found");
        }

        public ObservableCollection<TItem> GetAllItems()
        {
            var items = new ObservableCollection<TItem>();
            using(var query = connection.Prepare(GetSelectAllQuery()))
            {
                BindSelectAllQuery(query);
                while(query.Step() == SQLiteResult.ROW)
                {
                    var item = CreateInstance(query);
                    items.Add(item);
                }
            }
            return items;
        }

        public ObservableCollection<TItem> SelectByPath(string path, TKey folderId)
        {
            var items = new ObservableCollection<TItem>();
            using(var query = connection.Prepare(GetSelectByPathQuery()))
            {
                BindSelectByPathQuery(query, path, folderId);
                while (query.Step() == SQLiteResult.ROW)
                {
                    var item = CreateInstance(query);
                    items.Add(item);
                }
            }
            return items;
        }

        protected abstract string GetLastInsertRowIdQuery();
        protected abstract string GetSelectByPathQuery();
        protected abstract void BindSelectByPathQuery(ISQLiteStatement query, string path, TKey folderId);

        protected abstract string GetGetInsertsQuery();
        protected abstract void BindGetInsertsQuery(ISQLiteStatement query, TKey key, TKey folderId);
        protected abstract string GetGetUpdatesQuery();
        protected abstract void BindGetUpdatesQuery(ISQLiteStatement query, object value, TKey folderId);
        protected abstract string GetGetDeletesQuery();
        protected abstract void BindGetDeletesQuery();

        public ObservableCollection<TItem> GetInserts(TKey key, TKey association)
        {
            var items = new ObservableCollection<TItem>();
            using (var query = connection.Prepare(GetInsertItemQuery()))
            {
                BindGetInsertsQuery(query, key, association);
                while (query.Step() == SQLiteResult.ROW)
                {
                    var item = CreateInstance(query);
                    items.Add(item);
                }
            }
            return items;
        }
        public ObservableCollection<TItem> GetUpdates(TKey associatrion)
        {
            var items = new ObservableCollection<TItem>();
            using (var query = connection.Prepare(GetGetUpdatesQuery()))
            {
                BindGetUpdatesQuery(query,null, associatrion);
                while (query.Step() == SQLiteResult.ROW)
                {
                    var item = CreateInstance(query);
                    items.Add(item);
                }
            }
            return items;
        }
        public ObservableCollection<TItem> GetDeletes()
        {
            var items = new ObservableCollection<TItem>();
            using (var query = connection.Prepare(GetGetDeletesQuery()))
            {
                BindGetDeletesQuery();
                while (query.Step() == SQLiteResult.ROW)
                {
                    var item = CreateInstance(query);
                    items.Add(item);
                }
            }
            return items;
        }
    }
}
