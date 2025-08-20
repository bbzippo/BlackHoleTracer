using BlackHole;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Desktop;
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
using MouseButtonEventArgs = OpenTK.Windowing.Common.MouseButtonEventArgs;

namespace BlackHoleUI
{
    public partial class MainWindow : System.Windows.Window
    {

        private readonly Action<Action<BlackHoleEngine>> _postToEngine;
        volatile Action? _requestCloseGameWindow = null; 

        public MainWindow(Action<Action<BlackHoleEngine>> postToEngine,
                      Action? requestCloseGameWindow = null)
        {
            InitializeComponent();
            _postToEngine = postToEngine;
            _requestCloseGameWindow = requestCloseGameWindow;
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            _postToEngine(engine =>
            {
                engine.GameSetup.ShowDisk = !engine.GameSetup.ShowDisk;
                engine.Invalidate();            
            });
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _requestCloseGameWindow?.Invoke();
        }
    }
}