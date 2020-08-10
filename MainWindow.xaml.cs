using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ShellIconOverlayIdentifierSorter
{
    // Stores all the data from Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers
    internal class overlay
    {
        // Row number.
        public int nr { get; set; }

        // Keyname. This need to match the filename if you want an icon
        public string name { get; set; }

        // The icon
        public BitmapImage icon { get; set; }

        // The path to the dll that contains the icon (just for show)
        public string dll { get; set; }

        // How many spaces of identation is necessary to achieve the right sort order
        public int indent { get; set; }

        // Grays out the background for the rest of the rows
        public bool active => nr > 14;
    }

    // Stores whatever icons and pictures it can find in the "icons" folder
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

        public MainWindow()
        {
            InitializeComponent();

            if (!IsElevated)
            {
                MessageBox.Show("Need to run as administrator", "Run as admin", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
            }

            var files = Directory.GetFiles("icons", "*", SearchOption.AllDirectories);

            foreach (var file1 in files)
            {
                var i = new icon
                {
                    name = Path.GetFileNameWithoutExtension(file1),
                    image = new BitmapImage(new Uri(Path.GetFullPath(file1)))
                };
                _iconList.Add(i);
            }

            var nr = 0;
            var key = Registry.LocalMachine.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers");
            foreach (var v in key.GetSubKeyNames())
            {
                nr++;
                var keyValue = (string)Registry.LocalMachine
                    .OpenSubKey(
                        "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers\\" + v)
                    .GetValue(null);
                var clsid = @"CLSID\" + keyValue + @"\InProcServer32";
                var inproc = Registry.ClassesRoot.OpenSubKey(clsid);
                var inprocValue = (string)inproc.GetValue(null);

                var o = new overlay
                {
                    nr = nr,
                    name = v.Trim(),
                    icon = _iconList.FirstOrDefault(c => c.name.Trim() == v.Trim())?.image,
                    dll = inprocValue
                };

                _overlayList.Add(o);
            }

            listView.ItemsSource = _overlayList;

            _dragMgr = new ListViewDragDropManager<overlay>(listView)
            {
                ShowDragAdorner = true,
                DragAdornerOpacity = 0.5
            };
            _dragMgr.ProcessDrop += dragMgr_ProcessDrop;
        }

        public bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

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
                //e.ItemsSource.Move(higherIdx - 1, lowerIdx);
            }

            var nr = 0;
            foreach (var item in _overlayList)
                item.nr = ++nr;

            // Set this to 'Move' so that the OnListViewDrop knows to
            // remove the item from the other ListView.
            e.Effects = DragDropEffects.Move;
        }

        private void SaveOnClick(object sender, RoutedEventArgs e)
        {
            var rootKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\ShellIconOverlayIdentifiers";

            var indent = 0;
            for (var index = 0; index < _overlayList.Count - 1; index++)
            {
                _overlayList[index].indent = indent;
                if (string.CompareOrdinal(_overlayList[index].name, _overlayList[index + 1].name) >= 0)
                    indent++;
            }

            _overlayList[_overlayList.Count - 1].indent = indent;

            foreach (var f in _overlayList)
            {
                var newKey = "".PadLeft(indent - f.indent, ' ') + f.name;

                var subkey = Registry.LocalMachine.OpenSubKey(rootKey).GetSubKeyNames().FirstOrDefault(c => c.Trim() == f.name);
                if (subkey == null) continue;

                var oldSubKey = Registry.LocalMachine.OpenSubKey(rootKey + "\\" + subkey);
                if (newKey == subkey) continue;

                var keyValue = (string)oldSubKey.GetValue(null);

                Registry.LocalMachine.CreateSubKey(rootKey + "\\" + newKey).SetValue(null, keyValue);

                try
                {
                    Registry.LocalMachine.DeleteSubKey(rootKey + "\\" + subkey);
                }
                catch (Exception)
                {
                    Registry.LocalMachine.DeleteSubKey(rootKey + "\\" + newKey);
                }
            }
        }
    }
}