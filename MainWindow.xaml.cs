using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ShellIconOverlayIdentifierSorter
{
    /// <summary>
    ///     Stores all the data from
    ///     Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers
    /// </summary>
    internal class overlay : INotifyPropertyChanged
    {
        private int _nr;

        /// <summary>
        ///     Row number.
        /// </summary>
        public int nr
        {
            get => _nr;
            set
            {
                _nr = value;
                NotifyPropertyChanged("nr");
                NotifyPropertyChanged("active");
            }
        }

        /// <summary>
        ///     Keyname. This need to match the filename if you want an icon
        /// </summary>
        public string name { get; set; }

        /// <summary>
        ///     The icon
        /// </summary>
        public BitmapImage icon { get; set; }

        /// <summary>
        ///     The path to the dll that contains the icon (just for show)
        /// </summary>
        public string dll { get; set; }

        /// <summary>
        ///     How many spaces of indentation is necessary to achieve the right sort order
        /// </summary>
        public int indent { get; set; }

        /// <summary>
        ///     Grays out the background for the rest of the rows
        /// </summary>
        public bool active => nr > 14;

        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
    }

    /// <summary>
    ///     Stores whatever icons and pictures it can find in the "icons" folder
    /// </summary>
    internal class icon
    {
        public string name { get; set; }
        public BitmapImage image { get; set; }
    }

    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ListViewDragDropManager<overlay> _dragMgr;
        private readonly List<icon> _iconList = new List<icon>();
        private readonly ObservableCollection<overlay> _overlayList = new ObservableCollection<overlay>();
        private const string rootKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers";

        public MainWindow()
        {
            InitializeComponent();

            if (!IsElevated)
            {
                MessageBox.Show("Need to run as administrator. You can look, but you can't save", "Run as admin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                saveButton.IsEnabled = false;
            }

            RefreshList();

            // Enable drag and drop
            _dragMgr = new ListViewDragDropManager<overlay>(listView)
            {
                ShowDragAdorner = true,
                DragAdornerOpacity = 0.5
            };
            _dragMgr.ProcessDrop += dragMgr_ProcessDrop;
        }

        /// <summary>
        ///     Check that the program is running with elevated rights
        /// </summary>
        public bool IsElevated =>
            new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        private void RefreshList()
        {
            GetIconFiles();

            GetRegistryKeys();

            listView.ItemsSource = _overlayList;
        }

        /// <summary>
        ///     Get all the registry keys from
        ///     HKLM\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers
        /// </summary>
        private void GetRegistryKeys()
        {
            _overlayList.Clear();
            var nr = 0;
            var key = Registry.LocalMachine.OpenSubKey(rootKey);
            foreach (var v in key.GetSubKeyNames())
            {
                nr++;
                var keyValue = (string)Registry.LocalMachine.OpenSubKey($"{rootKey}\\{v}").GetValue(null);

                // Try to find the DLL file providing the icon
                var inprocValue = "";
                try
                {
                    var clsid = @"CLSID\" + keyValue + @"\InProcServer32";
                    var inproc = Registry.ClassesRoot.OpenSubKey(clsid);
                    inprocValue = (string)inproc.GetValue(null);
                }
                catch
                {
                    // ignored
                }

                var overlay = new overlay
                {
                    nr = nr,
                    name = v.Trim(),
                    icon = _iconList.FirstOrDefault(c => c.name.Trim() == v.Trim())?.image,
                    dll = inprocValue
                };

                _overlayList.Add(overlay);
            }
        }

        /// <summary>
        ///     Get icon files from the icons folder. The file names will later be matched with the registry key name
        /// </summary>
        private void GetIconFiles()
        {
            _iconList.Clear();
            var files = Directory.GetFiles("icons", "*", SearchOption.AllDirectories);
            foreach (var file in files)
                try
                {
                    var icon = new icon
                    {
                        name = Path.GetFileNameWithoutExtension(file),
                        image = new BitmapImage(new Uri(Path.GetFullPath(file)))
                    };
                    _iconList.Add(icon);
                }
                catch
                {
                    // ignored
                }
        }

        /// <summary>
        ///     Handles the drag and drop in the list
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void dragMgr_ProcessDrop(object sender, ProcessDropEventArgs<overlay> e)
        {
            // This shows how to customize the behavior of a drop.
            // Here we perform a swap, instead of just moving the dropped item.

            var higherIdx = Math.Max(e.OldIndex, e.NewIndex);
            var lowerIdx = Math.Min(e.OldIndex, e.NewIndex);

            if (lowerIdx < 0)
            {
                // The item came from the lower ListView
                // so just insert it.
                e.ItemsSource.Insert(higherIdx, e.DataItem);
            }
            else
            {
                // null values will cause an error when calling Move.
                // It looks like a bug in ObservableCollection to me.
                if (e.ItemsSource[lowerIdx] == null ||
                    e.ItemsSource[higherIdx] == null)
                    return;

                // The item came from the ListView into which
                // it was dropped, so swap it with the item
                // at the target index.
                if (e.OldIndex > -1)
                    e.ItemsSource.Move(e.OldIndex, e.NewIndex);
                else
                    e.ItemsSource.Insert(e.NewIndex, e.DataItem);
            }

            var nr = 0;
            foreach (var item in _overlayList)
                item.nr = ++nr;

            // Set this to 'Move' so that the OnListViewDrop knows to
            // remove the item from the other ListView.
            e.Effects = DragDropEffects.Move;
        }

        /// <summary>
        ///     Saves the changes
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveOnClick(object sender, RoutedEventArgs e)
        {
            // Calculate how many spaces that needs to be added to keep the requested sort order
            var indent = 0;
            for (var index = 0; index < _overlayList.Count - 1; index++)
            {
                _overlayList[index].indent = indent;
                // if the next entry is less than the current, increase the index
                if (string.CompareOrdinal(_overlayList[index].name, _overlayList[index + 1].name) >= 0)
                    indent++;
            }

            _overlayList[_overlayList.Count - 1].indent = indent;

            // Update the registry
            foreach (var f in _overlayList)
            {
                var newKeyName = "".PadLeft(indent - f.indent, ' ') + f.name;

                var subKeyName = Registry.LocalMachine.OpenSubKey(rootKey).GetSubKeyNames().FirstOrDefault(c => c.Trim() == f.name);
                if (subKeyName == null) continue;

                var oldSubKeyName = Registry.LocalMachine.OpenSubKey(rootKey + "\\" + subKeyName);
                if (newKeyName == subKeyName) continue;

                var keyValue = (string)oldSubKeyName.GetValue(null);

                Registry.LocalMachine.CreateSubKey(rootKey + "\\" + newKeyName).SetValue(null, keyValue);

                try
                {
                    Registry.LocalMachine.DeleteSubKey(rootKey + "\\" + subKeyName);
                }
                catch (Exception)
                {
                    Registry.LocalMachine.DeleteSubKey(rootKey + "\\" + newKeyName);
                }
            }

            RestartExplorer();
        }

        private static void RestartExplorer()
        {
            var answer = MessageBox.Show(
                "Would you like to restart explorer to make the changes come into effect?",
                "Restart explorer",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            var ps = Process.GetProcessesByName("explorer").ToList();

            foreach (var p in ps)
                p.Kill();
        }

        private void DeleteDuplicates_Click(object sender, RoutedEventArgs e)
        {
            var seenList = new List<string>();
            var key = Registry.LocalMachine.OpenSubKey(rootKey);
            foreach (var v in key.GetSubKeyNames())
                if (seenList.Any(c => c == v.Trim()))
                    try
                    {
                        Registry.LocalMachine.DeleteSubKey(rootKey + "\\" + v);
                    }
                    catch
                    {
                    }
                else
                    seenList.Add(v.Trim());

            RefreshList();
        }
    }
}