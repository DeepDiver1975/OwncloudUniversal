﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using OwncloudUniversal.Synchronization.Configuration;
using OwncloudUniversal.Synchronization.SQLite;
using System.Diagnostics;

namespace OwncloudUniversal.BackgroundTasks
{
    public sealed class AppUpdateTask : IBackgroundTask
    {
        public void Run(IBackgroundTaskInstance taskInstance)
        {
            if (Configuration.IsBackgroundTaskEnabled)
            {
                var reg = new BackgroundTaskConfiguration();
                reg.Enabled = true;
            }
            SQLiteClient.Reset();
            SQLiteClient.Init();
        }
    }
}
