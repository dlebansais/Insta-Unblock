using FolderTools;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using SchedulerTools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TaskbarTools;

namespace InstaUnblock
{
    public partial class App : Application
    {
        #region Init
        static App()
        {
            App.AddLog("Starting");

            InitSettings();
        }

        public App()
        {
            // Ensure only one instance is running at a time.
            App.AddLog("Checking uniqueness");
            try
            {
                bool createdNew;
                InstanceEvent = new EventWaitHandle(false, EventResetMode.ManualReset, "{6FCCC808-F32A-4CFA-9AE0-0D50AA4DDB61}", out createdNew);
                if (!createdNew)
                {
                    App.AddLog("Another instance is already running");
                    InstanceEvent.Close();
                    InstanceEvent = null;
                    Shutdown();
                    return;
                }
            }
            catch (Exception e)
            {
                App.AddLog($"(from App) {e.Message}");

                Shutdown();
                return;
            }

            Startup += OnStartup;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        private EventWaitHandle InstanceEvent;
        #endregion

        #region Properties
        public bool IsElevated
        {
            get
            {
                if (_IsElevated == null)
                {
                    WindowsIdentity wi = WindowsIdentity.GetCurrent();
                    if (wi != null)
                    {
                        WindowsPrincipal wp = new WindowsPrincipal(wi);
                        if (wp != null)
                            _IsElevated = wp.IsInRole(WindowsBuiltInRole.Administrator);
                        else
                            _IsElevated = false;
                    }
                    else
                        _IsElevated = false;

                    App.AddLog($"IsElevated={_IsElevated}");
                }

                return _IsElevated.Value;
            }
        }
        private bool? _IsElevated;

        public string ToolTipText
        {
            get
            {
                if (IsElevated)
                    return "Unblock downloaded files";
                else
                    return "Unblock downloaded files (Requires administrator mode)";
            }
        }
        #endregion

        #region Settings
        private static void InitSettings()
        {
            try
            {
                App.AddLog("InitSettings starting");

                RegistryKey Key = Registry.CurrentUser.OpenSubKey(@"Software", true);
                Key = Key.CreateSubKey("InstaUnblock");
                SettingKey = Key.CreateSubKey("Settings");

                App.AddLog("InitSettings done");
            }
            catch (Exception e)
            {
                App.AddLog($"(from InitSettings) {e.Message}");
            }
        }

        private static object GetSettingKey(string valueName)
        {
            try
            {
                return SettingKey?.GetValue(valueName);
            }
            catch
            {
                return null;
            }
        }

        private static void SetSettingKey(string valueName, object value, RegistryValueKind kind)
        {
            try
            {
                SettingKey?.SetValue(valueName, value, kind);
            }
            catch
            {
            }
        }

        private static void DeleteSetting(string valueName)
        {
            try
            {
                SettingKey?.DeleteValue(valueName, false);
            }
            catch
            {
            }
        }

        public static bool IsBoolKeySet(string valueName)
        {
            int? value = GetSettingKey(valueName) as int?;
            return value.HasValue;
        }

        public static bool GetSettingBool(string valueName, bool defaultValue)
        {
            int? value = GetSettingKey(valueName) as int?;
            return value.HasValue ? (value.Value != 0) : defaultValue;
        }

        public static void SetSettingBool(string valueName, bool value)
        {
            SetSettingKey(valueName, value ? 1 : 0, RegistryValueKind.DWord);
        }

        public static int GetSettingInt(string valueName, int defaultValue)
        {
            int? value = GetSettingKey(valueName) as int?;
            return value.HasValue ? value.Value : defaultValue;
        }

        public static void SetSettingInt(string valueName, int value)
        {
            SetSettingKey(valueName, value, RegistryValueKind.DWord);
        }

        public static string GetSettingString(string valueName, string defaultValue)
        {
            string value = GetSettingKey(valueName) as string;
            return value != null ? value : defaultValue;
        }

        public static void SetSettingString(string valueName, string value)
        {
            if (value == null)
                DeleteSetting(valueName);
            else
                SetSettingKey(valueName, value, RegistryValueKind.String);
        }

        private static RegistryKey SettingKey = null;
        #endregion

        #region Taskbar Icon
        private void InitTaskbarIcon()
        {
            App.AddLog("InitTaskbarIcon starting");

            MenuHeaderTable = new Dictionary<ICommand, string>();
            LoadAtStartupCommand = InitMenuCommand("LoadAtStartupCommand", LoadAtStartupHeader, OnCommandLoadAtStartup);
            UnblockCommand = InitMenuCommand("UnblockCommand", "Unblock", OnCommandUnblock);
            ExitCommand = InitMenuCommand("ExitCommand", "Exit", OnCommandExit);

            Icon Icon;
            ContextMenu ContextMenu = LoadContextMenu(out Icon);

            TaskbarIcon = TaskbarIcon.Create(Icon, ToolTipText, ContextMenu, ContextMenu);
            TaskbarIcon.MenuOpening += OnMenuOpening;

            App.AddLog("InitTaskbarIcon done");
        }

        private ICommand InitMenuCommand(string commandName, string header, ExecutedRoutedEventHandler executed)
        {
            ICommand Command = FindResource(commandName) as ICommand;
            MenuHeaderTable.Add(Command, header);

            // Bind the command to the corresponding handler. Requires the menu to be the target of notifications in TaskbarIcon.
            CommandManager.RegisterClassCommandBinding(typeof(ContextMenu), new CommandBinding(Command, executed));

            return Command;
        }

        private Icon LoadIcon(string iconName)
        {
            // Loads an "Embedded Resource" icon.
            foreach (string ResourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (ResourceName.EndsWith(iconName))
                {
                    using (Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    {
                        Icon Result = new Icon(rs);
                        App.AddLog($"Resource {iconName} loaded");

                        return Result;
                    }
                }

            App.AddLog($"Resource {iconName} not found");
            return null;
        }

        private Bitmap LoadBitmap(string bitmapName)
        {
            // Loads an "Embedded Resource" bitmap.
            foreach (string ResourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (ResourceName.EndsWith(bitmapName))
                {
                    using (Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    {
                        Bitmap Result = new Bitmap(rs);
                        App.AddLog($"Resource {bitmapName} loaded");

                        return Result;
                    }
                }

            App.AddLog($"Resource {bitmapName} not found");
            return null;
        }

        private ContextMenu LoadContextMenu(out Icon Icon)
        {
            ContextMenu Result = new ContextMenu();

            MenuItem LoadAtStartup;
            string ExeName = Assembly.GetExecutingAssembly().Location;
            if (Scheduler.IsTaskActive(ExeName))
            {
                if (IsElevated)
                {
                    LoadAtStartup = LoadNotificationMenuItem(LoadAtStartupCommand);
                    LoadAtStartup.IsChecked = true;
                }
                else
                {
                    LoadAtStartup = LoadNotificationMenuItem(LoadAtStartupCommand, RemoveFromStartupHeader);
                    LoadAtStartup.Icon = LoadBitmap("UAC-16.png");
                }
            }
            else
            {
                LoadAtStartup = LoadNotificationMenuItem(LoadAtStartupCommand);

                if (!IsElevated)
                    LoadAtStartup.Icon = LoadBitmap("UAC-16.png");
            }

            MenuItem UnblockMenu = LoadNotificationMenuItem(UnblockCommand);
            MenuItem ExitMenu = LoadNotificationMenuItem(ExitCommand);

            UnblockMenu.IsChecked = IsUnblocking;

            AddContextMenu(Result, LoadAtStartup, true, IsElevated);
            AddContextMenu(Result, UnblockMenu, true, IsElevated);
            AddContextMenuSeparator(Result);
            AddContextMenu(Result, ExitMenu, true, true);

            Icon = LoadCurrentIcon(UnblockMenu.IsChecked);

            App.AddLog("Menu created");

            return Result;
        }

        private Icon LoadCurrentIcon(bool isUnblockEnabled)
        {
            if (IsElevated)
                if (isUnblockEnabled)
                    return LoadIcon("Unblocking-Enabled.ico");
                else
                    return LoadIcon("Idle-Enabled.ico");
            else
                if (isUnblockEnabled)
                    return LoadIcon("Unblocking-Disabled.ico");
                else
                    return LoadIcon("Idle-Disabled.ico");
        }

        private MenuItem LoadNotificationMenuItem(ICommand command)
        {
            MenuItem Result = new MenuItem();
            Result.Header = MenuHeaderTable[command];
            Result.Command = command;
            Result.Icon = null;

            return Result;
        }

        private MenuItem LoadNotificationMenuItem(ICommand command, string header)
        {
            MenuItem Result = new MenuItem();
            Result.Header = header;
            Result.Command = command;
            Result.Icon = null;

            return Result;
        }

        private void AddContextMenu(ContextMenu menu, MenuItem item, bool isVisible, bool isEnabled)
        {
            TaskbarIcon.PrepareMenuItem(item, isVisible, isEnabled);
            menu.Items.Add(item);
        }

        private void AddContextMenuSeparator(ContextMenu menu)
        {
            menu.Items.Add(new Separator());
        }

        private void OnMenuOpening(object sender, EventArgs e)
        {
            App.AddLog("OnMenuOpening");

            TaskbarIcon SenderIcon = sender as TaskbarIcon;
            string ExeName = Assembly.GetExecutingAssembly().Location;

            if (IsElevated)
                SenderIcon.SetCheck(LoadAtStartupCommand, Scheduler.IsTaskActive(ExeName));
            else
            {
                if (Scheduler.IsTaskActive(ExeName))
                    SenderIcon.SetText(LoadAtStartupCommand, RemoveFromStartupHeader);
                else
                    SenderIcon.SetText(LoadAtStartupCommand, LoadAtStartupHeader);
            }
        }

        public TaskbarIcon TaskbarIcon { get; private set; }
        private static readonly string LoadAtStartupHeader = "Load at startup";
        private static readonly string RemoveFromStartupHeader = "Remove from startup";
        private ICommand LoadAtStartupCommand;
        private ICommand UnblockCommand;
        private ICommand ExitCommand;
        private Dictionary<ICommand, string> MenuHeaderTable;
        #endregion

        #region Events
        private void OnStartup(object sender, StartupEventArgs e)
        {
            App.AddLog("OnStartup");

            InitFileUnblockManager();
            InitTaskbarIcon();

            Exit += OnExit;
        }

        private void OnCommandLoadAtStartup(object sender, ExecutedRoutedEventArgs e)
        {
            App.AddLog("OnCommandLoadAtStartup");

            TaskbarIcon.ToggleChecked(LoadAtStartupCommand, out bool Install);
            InstallLoad(Install);
        }

        private void OnCommandUnblock(object sender, ExecutedRoutedEventArgs e)
        {
            App.AddLog("OnCommandUnblock");

            TaskbarIcon.ToggleChecked(UnblockCommand, out bool Unblock);
            ChangeUnblockMode(Unblock);

            Icon Icon = LoadCurrentIcon(Unblock);
            TaskbarIcon.UpdateIcon(Icon);

            App.AddLog($"Unblock mode: {Unblock}");
        }

        private void OnCommandExit(object sender, ExecutedRoutedEventArgs e)
        {
            App.AddLog("OnCommandExit");

            Shutdown();
        }

        private void OnExit(object sender, ExitEventArgs e)
        {
            App.AddLog("Exiting application");

            StopFileUnblockManager();

            if (InstanceEvent != null)
            {
                InstanceEvent.Close();
                InstanceEvent = null;
            }

            using (TaskbarIcon Icon = TaskbarIcon)
            {
                TaskbarIcon = null;
            }

            App.AddLog("Done");
        }
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

        private bool IsUnblocking { get { return GetSettingBool(UnblockingSettingName, true); } }

        private void ChangeUnblockMode(bool unblock)
        {
            SetSettingBool(UnblockingSettingName, unblock);
        }

        private void WatchFiles(string folderPath)
        {
            App.AddLog("Monitoring " + folderPath);

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
                App.AddLog("OnChanged " + e.FullPath + ", " + e.ChangeType);

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
                UnblockTimerOperation = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new System.Action(OnUnblockTimer));
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
                        App.AddLog("Forgetting " + Path + " (after " + ((int)Elapsed.TotalMilliseconds).ToString() + ")");
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
                                App.AddLog("Unblocking " + Path + " (after " + ((int)Elapsed.TotalMilliseconds).ToString() + ")");
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

            PrintDebugLine();
        }

        private string DebugCurrentTime
        {
            get
            {
                DateTime Now = DateTime.UtcNow;
                return Now.Second.ToString("D2") + ":" + Now.Millisecond.ToString("D3");
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

            App.AddLog(path + " unblocked");
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

        #region Load at startup
        private void InstallLoad(bool isInstalled)
        {
            string ExeName = Assembly.GetExecutingAssembly().Location;

            if (isInstalled)
                Scheduler.AddTask("Unblock downloaded files", ExeName, TaskRunLevel.Highest);
            else
                Scheduler.RemoveTask(ExeName, out bool IsFound);
        }
        #endregion

        #region Debugging
        public static void AddLog(string text)
        {
#if DEBUG
            lock (GlobalLock)
            {
                DateTime UtcNow = DateTime.UtcNow;
                string TimeLog = UtcNow.ToString(CultureInfo.InvariantCulture) + UtcNow.Millisecond.ToString("D3");

                string Line = $"InstaUnblock - {TimeLog}: {text}\n";

                if (DebugString == null)
                    DebugString = Line;
                else
                    DebugString += Line;
            }
#endif
        }

        private static void PrintDebugLine()
        {
#if DEBUG
            lock (GlobalLock)
            {
                if (DebugString != null)
                {
                    string[] Lines = DebugString.Split('\n');
                    foreach (string Line in Lines)
                        OutputDebugString(Line);

                    DebugString = null;
                }
            }
#endif
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        public static extern void OutputDebugString([In][MarshalAs(UnmanagedType.LPWStr)] string message);

#if DEBUG
        private static string DebugString = null;
#endif
        private static object GlobalLock = "";
        #endregion
    }
}
