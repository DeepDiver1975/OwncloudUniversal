﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace owncloud_universal.Model
{
    abstract class AbstractAdapter
    {
        //gibt das neue item zurück
        public abstract Task<AbstractItem> AddItem(AbstractItem item);

        //gibt das aktualisierte item zurück
        public abstract Task<AbstractItem> UpdateItem(AbstractItem item);

        public abstract Task DeleteItem(AbstractItem item);

        public abstract Task<List<AbstractItem>> GetAllItems(FolderAssociation association);
    }
}
