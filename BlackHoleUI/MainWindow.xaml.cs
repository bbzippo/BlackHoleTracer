using BlackHole;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
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
        private readonly Func<GameWindowHost> _getHost; 
        private readonly Action<Action<BlackHoleEngine>> _postToEngine; 
        volatile Action? _requestCloseGameWindow = null; 

        public MainWindow(
            Func<GameWindowHost> getHost,
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
            var host = _getHost();
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
    }
}