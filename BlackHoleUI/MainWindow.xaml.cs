using BlackHole;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Formats.Tar;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BlackHoleUI
{
    public partial class MainWindow : System.Windows.Window
    {
        private readonly Func<GameWindowHost>? _getHost; 
        private readonly Action<Action<BlackHoleEngine>> _postToEngine; 
        volatile Action? _requestCloseGameWindow = null; 

        public MainWindow(
            Func<GameWindowHost>? getHost,
            Action<Action<BlackHoleEngine>> postToEngine,
            Action? requestCloseGameWindow = null)
        {
            InitializeComponent();

            _getHost = getHost;
            _postToEngine = postToEngine;
            _requestCloseGameWindow = requestCloseGameWindow;
        }
        
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var host = _getHost?.Invoke();
            if (host != null)
            {   
                this.Left = host.GameWindow.Bounds.Min.X / dpi.DpiScaleX - this.Width;
                if (this.Left < 0) this.Left = 0;
                this.Top = host.GameWindow.Bounds.Min.Y / dpi.DpiScaleY;
            }
            
            this.Activate();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _requestCloseGameWindow?.Invoke();
        }

        private void cbShowDisk_Click(object sender, RoutedEventArgs e)
        {
            var b = cbShowDisk.IsChecked == true;
            _postToEngine(engine =>
            {
                engine.GameSetup.ShowDisk = b;
                engine.Invalidate();
            });
        }

        private void cbShowBricks_Click(object sender, RoutedEventArgs e)
        {
            var b = cbShowBricks.IsChecked == true;
            _postToEngine(engine =>
            {
                engine.GameSetup.ShowBricks = b;
                engine.Invalidate();
            });
        }

        private void cbbHorizon_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_postToEngine == null) return;

            int i = cbbHorizon.SelectedIndex;
            _postToEngine(engine =>
            {
                engine.GameSetup.HorizonHandling = (HorizonHandling)i;
                engine.Invalidate();
            });

        }

        private void btnBackgroundFile_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new Microsoft.Win32.OpenFileDialog();
            
            if (fileDialog.ShowDialog() == true)
            {
                tbBackground.Text = fileDialog.FileName;
            }
        }

        private void tbBackground_TextChanged(object sender, TextChangedEventArgs e)
        {   
            if (_postToEngine == null) return;
            var s = tbBackground.Text;
            _postToEngine(engine =>
            {
                engine.GameSetup.BgImage = s;
                engine.Invalidate();
            });
        }

        private void tbTIles_TextChanged(object sender, TextChangedEventArgs e)
        {   
            var s = tbTIles.Text;
            if (!int.TryParse(s, out int t) || t < 1 || t > 8)
            {   
                MessageBox.Show("Number of tiles must be int from 1 to 8");
                return;
            }

            if (_postToEngine == null) return;
            _postToEngine(engine =>
            {
                engine.GameSetup.BgTiles = t;
                engine.Invalidate();
            });
        }

        private void txtCompW_LostFocus(object sender, RoutedEventArgs e)
        {
            if (txtCompW == null || txtCompH == null) return;

            if (!int.TryParse(txtCompW.Text, out int w) || w < 1 || w > 2000)
            {
                MessageBox.Show("Bad compute width");
                return;
            }
            if (!int.TryParse(txtCompH.Text, out int h) || h < 1 || h > 2000)
            {
                MessageBox.Show("Bad compute height");
                return;
            }
            if (_postToEngine == null) return;
            _postToEngine(engine =>
            {
                engine.GameSetup.ComputeWidth = w;
                engine.GameSetup.ComputeHeight = h;
                engine.Invalidate();
            });
        }
    }
}