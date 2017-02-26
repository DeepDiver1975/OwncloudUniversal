﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.System.Power;
using Windows.UI.Core;
using Windows.UI.Notifications;
using OwncloudUniversal.Model;
using OwncloudUniversal.Shared.Model;

namespace OwncloudUniversal.Shared.Synchronisation
{
    public class BackgroundSyncProcess
    {
        private List<BaseItem> _itemIndex;
        private List<BaseItem> _deletions;
        private int _uploadCount;
        private int _downloadCount;
        private int _deletedCount;
        private bool _errorsOccured;
        private Stopwatch _watch;

        private readonly AbstractAdapter _sourceEntityAdapter;
        private readonly AbstractAdapter _targetEntityAdapter;
        private readonly bool _isBackgroundTask;

        public BackgroundSyncProcess(AbstractAdapter sourceEntityAdapter, AbstractAdapter targetEntityAdapter,
            bool isBackgroundTask)
        {
            _sourceEntityAdapter = sourceEntityAdapter;
            _targetEntityAdapter = targetEntityAdapter;
            _isBackgroundTask = isBackgroundTask;
            _errorsOccured = false;

        }

        public async Task Run()
        {
            _watch = Stopwatch.StartNew();
            SQLite.SQLiteClient.Init();
            _itemIndex = null;
            _deletions = null;
            _uploadCount = 0;
            _downloadCount = 0;
            _deletedCount = 0;
            _errorsOccured = false;
            await GetChanges();
            await ProcessItems();
            await Finish();
        }

        private async Task Finish()
        {
            await
                LogHelper.Write(
                    $"Finished synchronization cycle. Duration: {_watch.Elapsed} BackgroundTask: {_isBackgroundTask}");
            if (_deletedCount != 0 || _downloadCount != 0 || _uploadCount != 0)
                ToastHelper.SendToast(_isBackgroundTask
                    ? $"BackgroundTask: {_uploadCount} Files Uploaded, {_downloadCount} Files Downloaded {_deletedCount} Files Deleted. Duration: {_watch.Elapsed}"
                    : $"ManualSync: {_uploadCount} Files Uploaded, {_downloadCount} Files Downloaded {_deletedCount} Files Deleted. Duration: {_watch.Elapsed}");
            _watch.Stop();
            if (!_errorsOccured)
                Configuration.LastSync = DateTime.UtcNow.ToString("yyyy\\-MM\\-dd\\THH\\:mm\\:ss\\Z");
            await SetExecutionStatus(ExecutionStatus.Finished);
        }

        private async Task GetChanges()
        {
            var associations = FolderAssociationTableModel.GetDefault().GetAllItems();
            foreach (FolderAssociation association in associations)
            {
                await
                    LogHelper.Write(
                        $"Scanning {association.LocalFolderPath} and {association.RemoteFolderFolderPath} BackgroundTask: {_isBackgroundTask}");
                if (_watch.Elapsed.Minutes >= 9)
                    break;
                await SetExecutionStatus(ExecutionStatus.Scanning);

                var getUpdatedTargetTask = _targetEntityAdapter.GetUpdatedItems(association);
                var getUpdatedSourceTask = _sourceEntityAdapter.GetUpdatedItems(association);
                var getDeletedTargetTask = _targetEntityAdapter.GetDeletedItemsAsync(association);
                var getDeletedSourceTask = _sourceEntityAdapter.GetDeletedItemsAsync(association);

                _deletions = await getDeletedTargetTask;
                _deletions.AddRange(await getDeletedSourceTask);

                _itemIndex = await getUpdatedSourceTask;
                _itemIndex.AddRange(await getUpdatedTargetTask);

                await ProcessDeletions();
                await _UpdateFileIndexes(association);
                await LogHelper.Write($"Updating: {_itemIndex.Count} Deleting: {_deletions.Count} BackgroundTask: {_isBackgroundTask}");
            }
        }

        private async Task ProcessDeletions()
        {
            await SetExecutionStatus(ExecutionStatus.Deletions);
            foreach (var item in _deletions)
            {
                try
                {
                    if (item.AdapterType == _targetEntityAdapter.GetType() &&
                        item.Association.SyncDirection == SyncDirection.FullSync)
                    {
                        await _sourceEntityAdapter.DeleteItem(item);
                    }

                    if (item.AdapterType == _sourceEntityAdapter.GetType() &&
                        item.Association.SyncDirection == SyncDirection.FullSync)
                    {
                        await _targetEntityAdapter.DeleteItem(item);
                    }

                    var link = LinkStatusTableModel.GetDefault().GetItem(item);
                    if (link != null && item.Association.SyncDirection == SyncDirection.FullSync)
                    {
                        //the linked item should only be deleted from the database if its a full sync
                        //otherwise changes might not be tracked anymore if the user makes changes to the sync direction
                        var linkedItem = ItemTableModel.GetDefault().GetItem(link.TargetItemId);
                        if (linkedItem != null)
                        {
                            if (linkedItem.IsCollection)
                            {
                                var childItems = ItemTableModel.GetDefault().GetFilesForFolder(linkedItem.EntityId);
                                foreach (var childItem in childItems)
                                {
                                    var childLink = LinkStatusTableModel.GetDefault().GetItem(childItem);
                                    if (childLink != null)
                                        LinkStatusTableModel.GetDefault().DeleteItem(childLink.Id);
                                    ItemTableModel.GetDefault().DeleteItem(childItem.Id);
                                }
                            }
                            ItemTableModel.GetDefault().DeleteItem(linkedItem.Id);
                        }
                    }

                    LinkStatusTableModel.GetDefault().DeleteItem(link.Id);

                    if (item.IsCollection)
                    {
                        var childItems = ItemTableModel.GetDefault().GetFilesForFolder(item.EntityId);
                        foreach (var childItem in childItems)
                        {
                            ItemTableModel.GetDefault().DeleteItem(childItem.Id);
                            _deletedCount++;
                        }
                    }
                    ItemTableModel.GetDefault().DeleteItem(item.Id);
                    _deletedCount++;
                }
                catch (Exception e)
                {
                    _errorsOccured = true;
                    ToastHelper.SendToast(string.Format("Message: {0}, EntitityId: {1}", e.Message, item.EntityId));
                    await
                        LogHelper.Write(string.Format("Message: {0}, EntitityId: {1} StackTrace:\r\n{2}", e.Message,
                            item.EntityId, e.StackTrace));
                }
            }
        }

        private async Task ProcessItems()
        {
            await LogHelper.Write($"Starting Sync.. BackgroundTask: {_isBackgroundTask}");
            await SetExecutionStatus(ExecutionStatus.Active);
            _itemIndex = ItemTableModel.GetDefault().GetAllItems().ToList();
            var links = LinkStatusTableModel.GetDefault().GetAllItems().ToList();

            var itemsToProcess = 
                from baseItem in _itemIndex
                        .Where(i =>
                         i.Association.SyncDirection == SyncDirection.DownloadOnly &&
                         i.AdapterType == _targetEntityAdapter.GetType() ||
                         i.Association.SyncDirection == SyncDirection.UploadOnly &&
                         i.AdapterType == _sourceEntityAdapter.GetType() ||
                         i.Association.SyncDirection == SyncDirection.FullSync)
                from link in links
                    .Where(x => x.SourceItemId == baseItem.Id || x.TargetItemId == baseItem.Id)
                    .DefaultIfEmpty()
                select new {LinkStatus = link, BaseItem = baseItem};

            var itemsToAdd = 
                from item in itemsToProcess
                where item.LinkStatus == null
                select item.BaseItem;

            var itemsToUpdate = 
                from item in itemsToProcess
                where item.BaseItem.ChangeNumber > item.LinkStatus.ChangeNumber
                select item;

            await ProcessAdds(itemsToAdd);
            await ProcessUpdates(itemsToUpdate);
        }

        private async Task ProcessUpdates(IEnumerable<dynamic> itemsToUpdate)
        {
            if(itemsToUpdate == null)
                return;
            foreach (var item in itemsToUpdate)
            {
                try
                {
                    if (ExecutionContext.Instance.Status == ExecutionStatus.Stopped)
                        break;
                    await SetExectuingFileName(item.BaseItem.EntityId);
                    //get the linked item
                    var linkedItem =
                        ItemTableModel.GetDefault()
                            .GetItem(item.BaseItem.Id == item.LinkStatus.SourceItemId
                                ? item.LinkStatus.TargetItemId
                                : item.LinkStatus.SourceItemId);
                    //if both (item and the linkedItem) have a higher changenum than the link we have a conflict.
                    //That means both item have been updated since the last time we checked.
                    //so we check which one has the latest change and if it is the current item we update ititem.LinkStatus
                    if (linkedItem != null && item.BaseItem.ChangeNumber > item.LinkStatus.ChangeNumber &&
                        linkedItem.ChangeNumber > item.LinkStatus.ChangeNumber)
                    {
                        if (item.BaseItem.LastModified > linkedItem.LastModified)
                        {
                            var result = await Update(item.BaseItem);
                            AfterUpdate(item.BaseItem, result);
                        }
                    }
                    else
                    {
                        var result = await Update(item.BaseItem);
                        AfterUpdate(item.BaseItem, result);
                    }
                    if (await TimeIsOver())
                        break;
                }
                catch (Exception e)
                {
                    _errorsOccured = true;
                    ToastHelper.SendToast(string.Format("Message: {0}, EntitityId: {1}", e.Message, item.EntityId));
                    await
                        LogHelper.Write(string.Format("Message: {0}, EntitityId: {1} StackTrace:\r\n{2}", e.Message,
                            item.EntityId, e.StackTrace));
                }
            }
        }

        private async Task ProcessAdds(IEnumerable<BaseItem> itemsToAdd)
        {

            foreach (var item in itemsToAdd)
            {
                try
                {
                    if (ExecutionContext.Instance.Status == ExecutionStatus.Stopped)
                        break;
                    await SetExectuingFileName(item.EntityId);
                    string targetEntitiyId = null;

                    if (item.AdapterType == _targetEntityAdapter.GetType())
                    {
                        targetEntitiyId = _sourceEntityAdapter.BuildEntityId(item);
                    }

                    if (item.AdapterType == _sourceEntityAdapter.GetType())
                    {
                        targetEntitiyId = _targetEntityAdapter.BuildEntityId(item);
                    }

                    //if a new item is added which already exists on the other side we just assume
                    //that they both have the same content and just create a link but do not upload/download anything
                    //this the could be the case at the initial sync. The initial sync should never update 
                    //items on one side because we can not compare the contents of files
                    var foundItem = ItemTableModel.GetDefault().GetItemFromEntityId(targetEntitiyId);
                    var result = foundItem ?? await Insert(item);
                    AfterInsert(item, result);
                    if (await TimeIsOver())
                        break;
                }
                catch (Exception e)
                {
                    _errorsOccured = true;
                    ToastHelper.SendToast(string.Format("Message: {0}, EntitityId: {1}", e.Message, item.EntityId));
                    await
                        LogHelper.Write(string.Format("Message: {0}, EntitityId: {1} StackTrace:\r\n{2}", e.Message,
                            item.EntityId, e.StackTrace));
                }
            }
        }

        private async Task<BaseItem> Insert(BaseItem item)
        {

            BaseItem targetItem = null;
            item.SyncPostponed = false;
            if (item.AdapterType == _targetEntityAdapter.GetType())
            {
                targetItem = await _sourceEntityAdapter.AddItem(item);
                if (!item.IsCollection)
                    _downloadCount++;
            }
            else if (item.AdapterType == _sourceEntityAdapter.GetType())
            {
                targetItem = await _targetEntityAdapter.AddItem(item);
                if (!item.IsCollection)
                    _uploadCount++;
            }
            return targetItem;

        }

        private async Task<BaseItem> Update(BaseItem item)
        {
            BaseItem result = null;
            item.SyncPostponed = false;
            if (item.AdapterType == _targetEntityAdapter.GetType())
            {
                result = await _sourceEntityAdapter.UpdateItem(item);
                if (!item.IsCollection)
                    _downloadCount++;
            }
            else if (item.AdapterType == _sourceEntityAdapter.GetType())
            {
                result = await _targetEntityAdapter.UpdateItem(item);
                if (!item.IsCollection)
                    _uploadCount++;
            }
            return result;
        }

        private async Task _UpdateFileIndexes(FolderAssociation association)
        {
            await SetExecutionStatus(ExecutionStatus.UpdatingIndex);
            var itemTableModel = ItemTableModel.GetDefault();
            
            foreach (BaseItem t in _itemIndex)
            {
                t.Association = association;
                var foundItem = itemTableModel.GetItem(t);
                if (foundItem == null)
                {
                    itemTableModel.InsertItem(t);
                    t.Id = itemTableModel.GetLastInsertItem().Id;
                }
                else
                {
                    if (foundItem.ChangeKey != t.ChangeKey)
                    {
                        t.ChangeNumber = foundItem.ChangeNumber + 1;
                        itemTableModel.UpdateItem(t, foundItem.Id);
                    }
                    t.Id = foundItem.Id;

                }
            }
            association.LastSync = DateTime.UtcNow;
            FolderAssociationTableModel.GetDefault().UpdateItem(association, association.Id);
        }

        private void AfterInsert(BaseItem sourceItem, BaseItem targetItem)
        {
            if (targetItem.Association == null)
                targetItem.Association = sourceItem.Association;
            //check if item with same path already exists
            var existingItem = ItemTableModel.GetDefault().GetItem(targetItem);
            if (existingItem != null)
            {
                ItemTableModel.GetDefault().UpdateItem(targetItem, existingItem.Id);
                targetItem.Id = existingItem.Id;
            }
            else
            {
                ItemTableModel.GetDefault().InsertItem(targetItem);
                targetItem = ItemTableModel.GetDefault().GetLastInsertItem();
            }

            if (LinkStatusTableModel.GetDefault().GetItem(sourceItem) == null)
            {
                var link = new LinkStatus(sourceItem, targetItem);
                LinkStatusTableModel.GetDefault().InsertItem(link);
            }
        }

        private void AfterUpdate(BaseItem sourceItem, BaseItem targetItem)
        {
            if (targetItem.Association == null)
                targetItem.Association = sourceItem.Association;
            targetItem.ChangeNumber = sourceItem.ChangeNumber;
            var link = LinkStatusTableModel.GetDefault().GetItem(sourceItem);
            if (link != null)
            {
                link.ChangeNumber = sourceItem.ChangeNumber;
                LinkStatusTableModel.GetDefault().UpdateItem(link, link.Id);
                targetItem.Id = link.TargetItemId;
                ItemTableModel.GetDefault().UpdateItem(sourceItem, sourceItem.Id);
                ItemTableModel.GetDefault().UpdateItem(targetItem, targetItem.Id);
            }
        }

        private async Task SetExecutionStatus(ExecutionStatus status)
        {
            try
            {
                if(_isBackgroundTask)
                    return;
                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    ExecutionContext.Instance.Status = status;
                });
            }
            catch (Exception)
            {
                //supress all errors here
            }
        }

        private async Task SetExectuingFileName(string entityId)
        {
            if (!_isBackgroundTask)
            {
                try
                {
                    await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        ExecutionContext.Instance.TotalFileCount = _itemIndex.Count;
                        ExecutionContext.Instance.CurrentFileNumber++;
                        ExecutionContext.Instance.CurrentFileName = entityId;
                    });
                }
                catch (Exception)
                {
                    //supress all errors here
                }

            }
        }

        private async Task<bool> TimeIsOver()
        {
            //we have 10 Minutes in total for each background task cycle
            //after 10 minutes windows will terminate the task
            //so after 9 minutes we stop the sync and just wait for the next cycle
            if (_watch.Elapsed.Minutes >= 9 && _isBackgroundTask)
            {
                await LogHelper.Write("Stopping sync-cycle. Please wait for the next cycle to complete the sync");
                return true;
            }
            return false;
        }
    }
}