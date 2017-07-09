﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using OwncloudUniversal.Synchronization.Model;
using OwncloudUniversal.Synchronization.LocalFileSystem;

namespace OwncloudUniversal.Synchronization.Model
{
    public class LocalItem : BaseItem
    {
        public LocalItem() { }
        

        public LocalItem(FolderAssociation association,IStorageItem storageItem, BasicProperties basicProperties)
        {
            Association = association;
            IsCollection = storageItem is StorageFolder;
            ChangeKey = SQLite.DateTimeHelper.DateTimeSQLite(basicProperties.DateModified.UtcDateTime);
            EntityId = storageItem.Path;
            ChangeNumber = 0;
            Size = basicProperties.Size;
            ContentType = (storageItem as StorageFile)?.ContentType;
        }

        public LocalItem(FolderAssociation association, IStorageItem storageItem, IDictionary<string, object> properties )
        {
            Association = association;
            IsCollection = storageItem is StorageFolder;
            var s = properties["System.DateModified"];
            ChangeKey = SQLite.DateTimeHelper.DateTimeSQLite(((DateTimeOffset)properties["System.DateModified"]).UtcDateTime);
            EntityId = storageItem.Path;
            ChangeNumber = 0;
            if(!IsCollection)
                Size = (ulong)properties["System.Size"];
            ContentType = (storageItem as StorageFile)?.ContentType;
        }
      
        private string Path { get; set; }
        public override ulong Size { get; set; }

        public override string EntityId
        {
            get
            {
                return Path;
            }

            set
            {
                Path = value;
            }
        }

        public override string ChangeKey
        {
            get
            {
                return SQLite.DateTimeHelper.DateTimeSQLite(LastModified);
            }

            set
            {
                LastModified = Convert.ToDateTime(value);
            }
        }

        public override string DisplayName => EntityId;

        public override Type AdapterType => typeof(FileSystemAdapter);
    }
}
