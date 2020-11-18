namespace InstaUnblock
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Windows.Input;
    using System.Windows.Threading;
    using Contracts;
    using FolderTools;
    using RegistryTools;
    using ResourceTools;
    using TaskbarIconHost;
    using Tracing;

    /// <summary>
    /// Represents a plugin that automatically unblock files downloaded from the Internet.
    /// </summary>
    public class InstaUnblockPlugin : IPluginClient, IDisposable
    {
        #region Plugin
        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public string Name
        {
            get { return "Insta-Unblock"; }
        }

        /// <summary>
        /// Gets the plugin unique ID.
        /// </summary>
        public Guid Guid
        {
            get { return new Guid("{6FCCC808-F32A-4CFA-9AE0-0D50AA4DDB61}"); }
        }

        /// <summary>
        /// Gets the plugin assembly name.
        /// </summary>
        public string AssemblyName { get; } = "Insta-Unblock-Plugin";

        /// <summary>
        ///  Gets a value indicating whether the plugin require elevated (administrator) mode to operate.
        /// </summary>
        public bool RequireElevated
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether the plugin want to handle clicks on the taskbar icon.
        /// </summary>
        public bool HasClickHandler
        {
            get { return false; }
        }

        /// <summary>
        /// Called once at startup, to initialize the plugin.
        /// </summary>
        /// <param name="isElevated">True if the caller is executing in administrator mode.</param>
        /// <param name="dispatcher">A dispatcher that can be used to synchronize with the UI.</param>
        /// <param name="settings">An interface to read and write settings in the registry.</param>
        /// <param name="logger">An interface to log events asynchronously.</param>
        public void Initialize(bool isElevated, Dispatcher dispatcher, Settings settings, ITracer logger)
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
                              commandHandler: () => ChangeUnblockMode(!IsUnblocking));
        }

        private void InitializeCommand(string header, Func<bool> isVisibleHandler, Func<bool> isEnabledHandler, Func<bool> isCheckedHandler, Action commandHandler)
        {
            string LocalizedText = Properties.Resources.ResourceManager.GetString(header, CultureInfo.CurrentCulture) !;
            ICommand Command = new RoutedUICommand(LocalizedText, header, GetType());

            CommandList.Add(Command);
            MenuHeaderTable.Add(Command, LocalizedText);
            MenuIsVisibleTable.Add(Command, isVisibleHandler);
            MenuIsEnabledTable.Add(Command, isEnabledHandler);
            MenuIsCheckedTable.Add(Command, isCheckedHandler);
            MenuHandlerTable.Add(Command, commandHandler);
        }

        /// <summary>
        /// Gets the list of commands that the plugin can receive when an item is clicked in the context menu.
        /// </summary>
        public List<ICommand> CommandList { get; private set; } = new List<ICommand>();

        /// <summary>
        /// Reads a flag indicating if the state of a menu item has changed. The flag should be reset upon return until another change occurs.
        /// </summary>
        /// <param name="beforeMenuOpening">True if this function is called right before the context menu is opened by the user; otherwise, false.</param>
        /// <returns>True if a menu item state has changed since the last call; otherwise, false.</returns>
        public bool GetIsMenuChanged(bool beforeMenuOpening)
        {
            bool Result = IsMenuChanged;
            IsMenuChanged = false;

            return Result;
        }

        /// <summary>
        /// Reads the text of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>The menu text.</returns>
        public string GetMenuHeader(ICommand command)
        {
            return MenuHeaderTable[command];
        }

        /// <summary>
        /// Reads the state of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>True if the menu item should be visible to the user, false if it should be hidden.</returns>
        public bool GetMenuIsVisible(ICommand command)
        {
            return MenuIsVisibleTable[command]();
        }

        /// <summary>
        /// Reads the state of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>True if the menu item should appear enabled, false if it should be disabled.</returns>
        public bool GetMenuIsEnabled(ICommand command)
        {
            return MenuIsEnabledTable[command]();
        }

        /// <summary>
        /// Reads the state of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>True if the menu item is checked, false otherwise.</returns>
        public bool GetMenuIsChecked(ICommand command)
        {
            return MenuIsCheckedTable[command]();
        }

        /// <summary>
        /// Reads the icon of a menu item associated to command.
        /// </summary>
        /// <param name="command">The command associated to the menu item.</param>
        /// <returns>The icon to display with the menu text, null if none.</returns>
        public Bitmap? GetMenuIcon(ICommand command)
        {
            return null;
        }

        /// <summary>
        /// This method is called before the menu is displayed, but after changes in the menu have been evaluated.
        /// </summary>
        public void OnMenuOpening()
        {
        }

        /// <summary>
        /// Requests for command to be executed.
        /// </summary>
        /// <param name="command">The command to execute.</param>
        public void OnExecuteCommand(ICommand command)
        {
            MenuHandlerTable[command]();
        }

        /// <summary>
        /// Reads a flag indicating if the plugin icon, that might reflect the state of the plugin, has changed.
        /// </summary>
        /// <returns>True if the icon has changed since the last call, false otherwise.</returns>
        public bool GetIsIconChanged()
        {
            bool Result = IsIconChanged;
            IsIconChanged = false;

            return Result;
        }

        /// <summary>
        /// Gets the icon displayed in the taskbar.
        /// </summary>
        public Icon Icon
        {
            get
            {
                Icon Result;

                if (IsUnblocking)
                    ResourceLoader.LoadIcon("Unblocking-Enabled.ico", string.Empty, out Result);
                else
                    ResourceLoader.LoadIcon("Idle-Enabled.ico", string.Empty, out Result);

                return Result;
            }
        }

        /// <summary>
        /// Gets the bitmap displayed in the preferred plugin menu.
        /// </summary>
        public Bitmap SelectionBitmap
        {
            get
            {
                ResourceLoader.LoadBitmap("Insta-Unblock.png", string.Empty, out Bitmap Result);
                return Result;
            }
        }

        /// <summary>
        /// Requests for the main plugin operation to be executed.
        /// </summary>
        public void OnIconClicked()
        {
        }

        /// <summary>
        /// Reads a flag indicating if the plugin tooltip, that might reflect the state of the plugin, has changed.
        /// </summary>
        /// <returns>True if the tooltip has changed since the last call, false otherwise.</returns>
        public bool GetIsToolTipChanged()
        {
            return false;
        }

        /// <summary>
        /// Gets the free text that indicate the state of the plugin.
        /// </summary>
        public string ToolTip { get { return "Lock/Unlock Windows updates"; } }

        /// <summary>
        /// Called when the taskbar is getting the application focus.
        /// </summary>
        public void OnActivated()
        {
        }

        /// <summary>
        /// Called when the taskbar is loosing the application focus.
        /// </summary>
        public void OnDeactivated()
        {
        }

        /// <summary>
        /// Requests to close and terminate a plugin.
        /// </summary>
        /// <param name="canClose">True if no plugin called before this one has returned false, false if one of them has.</param>
        /// <returns>True if the plugin can be safely terminated, false if the request is denied.</returns>
        public bool CanClose(bool canClose)
        {
            return true;
        }

        /// <summary>
        /// Requests to begin closing the plugin.
        /// </summary>
        public void BeginClose()
        {
            StopFileUnblockManager();
        }

        /// <summary>
        /// Gets a value indicating whether the plugin is closed.
        /// </summary>
        public bool IsClosed
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether the caller is executing in administrator mode.
        /// </summary>
        public bool IsElevated { get; private set; }

        /// <summary>
        /// Gets a dispatcher that can be used to synchronize with the UI.
        /// </summary>
        public Dispatcher Dispatcher { get; private set; } = null!;

        /// <summary>
        /// Gets an interface to read and write settings in the registry.
        /// </summary>
        public Settings Settings { get; private set; } = null!;

        /// <summary>
        /// Gets an interface to log events asynchronously.
        /// </summary>
        public ITracer Logger { get; private set; } = null!;

        private void AddLog(string message)
        {
            Logger.Write(Category.Information, message);
        }

        private Dictionary<ICommand, string> MenuHeaderTable = new Dictionary<ICommand, string>();
        private Dictionary<ICommand, Func<bool>> MenuIsVisibleTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsEnabledTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsCheckedTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Action> MenuHandlerTable = new Dictionary<ICommand, Action>();
        private bool IsIconChanged;
        private bool IsMenuChanged;
        #endregion

        #region File Unblock Manager
        private void InitFileUnblockManager()
        {
            string DownloadPath = NativeMethods.GetPath(KnownFolder.Downloads, false);
            WatchFiles(DownloadPath);

            UnblockTimer = new Timer(new TimerCallback(UnblockTimerCallback));
            UnblockTimer.Change(CheckInterval, CheckInterval);
        }

        private void StopFileUnblockManager()
        {
            using (UnblockTimer)
            {
                UnblockTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            foreach (FileSystemWatcher Watcher in WatcherList)
            {
                Watcher.EnableRaisingEvents = false;
                Watcher.Changed -= OnChanged;
            }

            WatcherList.Clear();
        }

        private bool IsUnblocking
        {
            get
            {
                Settings.GetBool(UnblockingSettingName, true, out bool Result);
                return Result;
            }
        }

        private void ChangeUnblockMode(bool unblock)
        {
            Settings.SetBool(UnblockingSettingName, unblock);
            IsIconChanged = true;
            IsMenuChanged = true;
        }

        private void WatchFiles(string folderPath)
        {
            AddLog("Monitoring " + folderPath);

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
                AddLog("OnChanged " + e.FullPath + ", " + e.ChangeType);

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

        private void UnblockTimerCallback(object? parameter)
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
                        AddLog($"Forgetting {Path} (after {(int)Elapsed.TotalMilliseconds})");
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
                                AddLog($"Unblocking {Path} (after {(int)Elapsed.TotalMilliseconds})");
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

            Contract.RequireNotNull(Path.GetDirectoryName(path), out string DirectoryName);
            string FileName = Path.GetFileName(path);

            using Process p = new Process();
            p.StartInfo.FileName = "cmd.exe";
            p.StartInfo.Arguments = "/C echo.>\"" + FileName + "\":Zone.Identifier";
            p.StartInfo.WorkingDirectory = DirectoryName;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.Start();

            AddLog(path + " unblocked");
        }

        private const string UnblockingSettingName = "Unblocking";
        private List<FileSystemWatcher> WatcherList = new List<FileSystemWatcher>();
        private Dictionary<string, Stopwatch> CreateFileTable = new Dictionary<string, Stopwatch>();
        private Timer UnblockTimer = new Timer(new TimerCallback((object? state) => { }));
        private TimeSpan CheckInterval = TimeSpan.FromSeconds(0.1);
        private TimeSpan MinElapsedTimeForUnblock = TimeSpan.FromSeconds(1.0);
        private TimeSpan MinElapsedTimeForForget = TimeSpan.FromSeconds(5.0);
        private DispatcherOperation? UnblockTimerOperation;
        private Dictionary<string, Stopwatch> UnlockedFileTable = new Dictionary<string, Stopwatch>();
        #endregion

        #region Implementation of IDisposable
        /// <summary>
        /// Called when an object should release its resources.
        /// </summary>
        /// <param name="isDisposing">Indicates if resources must be disposed now.</param>
        protected virtual void Dispose(bool isDisposing)
        {
            if (!IsDisposed)
            {
                IsDisposed = true;

                if (isDisposing)
                    DisposeNow();
            }
        }

        /// <summary>
        /// Called when an object should release its resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="InstaUnblockPlugin"/> class.
        /// </summary>
        ~InstaUnblockPlugin()
        {
            Dispose(false);
        }

        /// <summary>
        /// True after <see cref="Dispose(bool)"/> has been invoked.
        /// </summary>
        private bool IsDisposed;

        /// <summary>
        /// Disposes of every reference that must be cleaned up.
        /// </summary>
        private void DisposeNow()
        {
            using (Settings)
            {
            }

            using (UnblockTimer)
            {
            }
        }
        #endregion
    }
}
