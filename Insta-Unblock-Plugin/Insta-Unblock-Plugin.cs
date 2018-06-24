using FolderTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using TaskbarIconHost;

namespace InstaUnblock
{
    public class InstaUnblockPlugin : TaskbarIconHost.IPluginClient
    {
        #region Plugin
        public string Name
        {
            get { return PluginDetails.Name; }
        }

        public Guid Guid
        {
            get { return PluginDetails.Guid; }
        }

        public bool RequireElevated
        {
            get { return false; }
        }

        public bool HasClickHandler
        {
            get { return false; }
        }

        public void Initialize(bool isElevated, Dispatcher dispatcher, TaskbarIconHost.IPluginSettings settings, TaskbarIconHost.IPluginLogger logger)
        {
            IsElevated = isElevated;
            Dispatcher = dispatcher;
            Settings = settings;
            Logger = logger;

            InitFileUnblockManager();

            InitializeCommand("Unblock",
                              isVisibleHandler: () => true,
                              isEnabledHandler: () => true,
                              isCheckedHandler: () => IsUnblocking,
                              commandHandler:   () => ChangeUnblockMode(!IsUnblocking));
        }

        private void InitializeCommand(string header, Func<bool> isVisibleHandler, Func<bool> isEnabledHandler, Func<bool> isCheckedHandler, Action commandHandler)
        {
            ICommand Command = new RoutedUICommand();
            CommandList.Add(Command);
            MenuHeaderTable.Add(Command, header);
            MenuIsVisibleTable.Add(Command, isVisibleHandler);
            MenuIsEnabledTable.Add(Command, isEnabledHandler);
            MenuIsCheckedTable.Add(Command, isCheckedHandler);
            MenuHandlerTable.Add(Command, commandHandler);
        }

        public List<ICommand> CommandList { get; private set; } = new List<ICommand>();

        public bool GetIsMenuChanged()
        {
            bool Result = IsMenuChanged;
            IsMenuChanged = false;

            return Result;
        }

        public string GetMenuHeader(ICommand command)
        {
            return MenuHeaderTable[command];
        }

        public bool GetMenuIsVisible(ICommand command)
        {
            return MenuIsVisibleTable[command]();
        }

        public bool GetMenuIsEnabled(ICommand command)
        {
            return MenuIsEnabledTable[command]();
        }

        public bool GetMenuIsChecked(ICommand command)
        {
            return MenuIsCheckedTable[command]();
        }

        public Bitmap GetMenuIcon(ICommand command)
        {
            return null;
        }

        public void OnMenuOpening()
        {
        }

        public void OnExecuteCommand(ICommand command)
        {
            MenuHandlerTable[command]();
        }

        public bool GetIsIconChanged()
        {
            bool Result = IsIconChanged;
            IsIconChanged = false;

            return Result;
        }

        public void OnIconClicked()
        {
        }

        public Icon Icon
        {
            get
            {
                if (IsUnblocking)
                    return LoadEmbeddedResource<Icon>("Unblocking-Enabled.ico");
                else
                    return LoadEmbeddedResource<Icon>("Idle-Enabled.ico");
            }
        }

        public Bitmap SelectionBitmap
        {
            get { return LoadEmbeddedResource<Bitmap>("Insta-Unblock.png"); }
        }

        public bool GetIsToolTipChanged()
        {
            return false;
        }

        public string ToolTip { get { return "Unblock downloaded files"; } }

        public void OnActivated()
        {
        }

        public void OnDeactivated()
        {
        }

        public bool CanClose(bool canClose)
        {
            return true;
        }

        public void BeginClose()
        {
            StopFileUnblockManager();
        }

        public bool IsClosed
        {
            get { return true; }
        }

        public bool IsElevated { get; private set; }
        public Dispatcher Dispatcher { get; private set; }
        public TaskbarIconHost.IPluginSettings Settings { get; private set; }
        public TaskbarIconHost.IPluginLogger Logger { get; private set; }

        private T LoadEmbeddedResource<T>(string resourceName)
        {
            // Loads an "Embedded Resource" of type T (ex: Bitmap for a PNG file).
            foreach (string ResourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (ResourceName.EndsWith(resourceName))
                {
                    using (Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    {
                        T Result = (T)Activator.CreateInstance(typeof(T), rs);
                        Logger.AddLog($"Resource {resourceName} loaded");

                        return Result;
                    }
                }

            Logger.AddLog($"Resource {resourceName} not found");
            return default(T);
        }

        private Dictionary<ICommand, string> MenuHeaderTable = new Dictionary<ICommand, string>();
        private Dictionary<ICommand, Func<bool>> MenuIsVisibleTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsEnabledTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsCheckedTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Action> MenuHandlerTable = new Dictionary<ICommand, Action>();
        private bool IsMenuChanged;
        private bool IsIconChanged;
        #endregion

        #region File Unblock Manager
        private void InitFileUnblockManager()
        {
            string DownloadPath = KnownFolders.GetPath(KnownFolder.Downloads, false);
            WatchFiles(DownloadPath);

            UnblockTimer = new Timer(new TimerCallback(UnblockTimerCallback));
            UnblockTimer.Change(CheckInterval, CheckInterval);
        }

        private void StopFileUnblockManager()
        {
            UnblockTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            UnblockTimer = null;

            foreach (FileSystemWatcher Watcher in WatcherList)
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Changed -= OnChanged;
            }

            WatcherList.Clear();
        }

        private bool IsUnblocking { get { return Settings.GetSettingBool(UnblockingSettingName, true); } }

        private void ChangeUnblockMode(bool unblock)
        {
            Settings.SetSettingBool(UnblockingSettingName, unblock);
            IsIconChanged = true;
            IsMenuChanged = true;
        }

        private void WatchFiles(string folderPath)
        {
            Logger.AddLog("Monitoring " + folderPath);

            FileSystemWatcher Watcher = new FileSystemWatcher();
            Watcher.Path = folderPath;
            Watcher.NotifyFilter = NotifyFilters.LastWrite;
            Watcher.Filter = "*.*";
            Watcher.Changed += OnChanged;
            Watcher.EnableRaisingEvents = true;
            WatcherList.Add(Watcher);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            lock (CreateFileTable)
            {
                Logger.AddLog("OnChanged " + e.FullPath + ", " + e.ChangeType);

                if (!CreateFileTable.ContainsKey(e.FullPath))
                {
                    Stopwatch NewWatch = new Stopwatch();
                    CreateFileTable.Add(e.FullPath, NewWatch);
                    NewWatch.Start();
                }
                else
                {
                    Stopwatch Watch = CreateFileTable[e.FullPath];
                    Watch.Restart();
                }
            }
        }

        private void UnblockTimerCallback(object parameter)
        {
            if (UnblockTimerOperation == null || UnblockTimerOperation.Status == DispatcherOperationStatus.Completed)
                UnblockTimerOperation = Dispatcher.BeginInvoke(DispatcherPriority.Normal, new System.Action(OnUnblockTimer));
        }

        private void OnUnblockTimer()
        {
            bool? Unblock = null;
            List<string> EntryToForget = new List<string>();
            List<string> EntryToRemove = new List<string>();

            lock (CreateFileTable)
            {
                foreach (KeyValuePair<string, Stopwatch> Entry in UnlockedFileTable)
                {
                    string Path = Entry.Key;
                    TimeSpan Elapsed = Entry.Value.Elapsed;

                    if (Elapsed >= MinElapsedTimeForForget)
                    {
                        Logger.AddLog("Forgetting " + Path + " (after " + ((int)Elapsed.TotalMilliseconds).ToString() + ")");
                        EntryToForget.Add(Path);
                    }
                }

                foreach (string Path in EntryToForget)
                    UnlockedFileTable.Remove(Path);

                foreach (KeyValuePair<string, Stopwatch> Entry in CreateFileTable)
                {
                    string Path = Entry.Key;
                    TimeSpan Elapsed = Entry.Value.Elapsed;

                    if (Elapsed >= MinElapsedTimeForUnblock)
                    {
                        if (!Unblock.HasValue)
                            Unblock = IsUnblocking;

                        if (Unblock.Value)
                        {
                            EntryToRemove.Add(Path);

                            if (!UnlockedFileTable.ContainsKey(Path))
                            {
                                Logger.AddLog("Unblocking " + Path + " (after " + ((int)Elapsed.TotalMilliseconds).ToString() + ")");
                                UnblockFile(Path);

                                Stopwatch NewWatch = new Stopwatch();
                                UnlockedFileTable.Add(Path, NewWatch);
                                NewWatch.Start();
                            }
                        }
                    }
                }

                foreach (string Path in EntryToRemove)
                    CreateFileTable.Remove(Path);
            }
        }

        private void UnblockFile(string path)
        {
            if (!File.Exists(path))
                return;

            string DirectoryName = Path.GetDirectoryName(path);
            string FileName = Path.GetFileName(path);

            Process p = new Process();
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = "/C echo.>\"" + FileName + "\":Zone.Identifier";
            p.StartInfo.WorkingDirectory = DirectoryName;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.Start();

            Logger.AddLog(path + " unblocked");
        }

        private static readonly string UnblockingSettingName = "Unblocking";
        private List<FileSystemWatcher> WatcherList = new List<FileSystemWatcher>();
        private Dictionary<string, Stopwatch> CreateFileTable = new Dictionary<string, Stopwatch>();
        private Timer UnblockTimer;
        private TimeSpan CheckInterval = TimeSpan.FromSeconds(0.1);
        private TimeSpan MinElapsedTimeForUnblock = TimeSpan.FromSeconds(1.0);
        private TimeSpan MinElapsedTimeForForget = TimeSpan.FromSeconds(5.0);
        private DispatcherOperation UnblockTimerOperation = null;
        private Dictionary<string, Stopwatch> UnlockedFileTable = new Dictionary<string, Stopwatch>();
        #endregion
    }
}
