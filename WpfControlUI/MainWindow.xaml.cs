using BlackHole;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Wpf;
using System.Runtime.InteropServices.JavaScript;
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
using System.Windows.Threading;
using System.Xml.Linq;

namespace BlackHoleUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {   
        private bool _initialized;
        
        private BlackHoleEngine? blackHoleEngine;
        private WpfGlHost _host;
        
        private Point ? _pendingSize;
        private bool _dragging = false;
        private DpiScale dpi;

        const int FPS = 16;
        int vsyncDiv = (int)Math.Round( 60d /FPS); // need to align with refresh rate for non-60 ???
        int frameCount = 0;

        

        public MainWindow()
        {
            InitializeComponent();

            // Set up OpenTK GLWpfControl

            var settings = new GLWpfControlSettings
            {
                MajorVersion = 4,
                MinorVersion = 3,
                
                RenderContinuously = true, //!!
            };

            GlControl.Start(settings);

            dpi = VisualTreeHelper.GetDpi(GlControl);

            _host = new WpfGlHost(GlControl, dpi);
            
            GlControl.Render += GlControl_Render;
            
            var timer = new System.Windows.Threading.DispatcherTimer(DispatcherPriority.Render);
            if (!settings.RenderContinuously)
            {
                // Constant framerate, rather than on mouse move or resize
                timer.Interval = TimeSpan.FromSeconds(1.0 / (FPS));
                timer.Tick += (_, __) => GlControl.InvalidateVisual();  // request a frame
                timer.Start();
            }

            GlControl.SizeChanged += (_, __) =>
            {
                // Queue the size; apply it in Render (when context is current)
                _pendingSize = new(
                    Math.Max(1, (int)(GlControl.ActualWidth * dpi.DpiScaleX)),
                    Math.Max(1, (int)(GlControl.ActualHeight * dpi.DpiScaleY))
                );
            };

            HookMouseInput();

            //// Keyboard events 
            //GlControl.KeyDown += (s, e) =>
            //{
            //    if (e.Key == Key.Escape) blackHoleEngine?.Input_KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape);
            //};
        }

        // pressed buttons tracker
        readonly HashSet<MouseButton> _mouseButtonsDown = new();
        static OpenTK.Windowing.GraphicsLibraryFramework.MouseButton MapBtn(MouseButton b)
                => b switch
                {
                    MouseButton.Left => OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left,
                    MouseButton.Middle => OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle,
                    _ => OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right
                };
        
        void HookMouseInput()
        {
            // Ensure control can receive keyboard/mouse
            GlControl.Focusable = true;

            static OpenTK.Windowing.GraphicsLibraryFramework.MouseButton MapBtn(MouseButton b)
                => b switch
                {
                    MouseButton.Left => OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left,
                    MouseButton.Middle => OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Middle,
                    _ => OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Right
                };

            GlControl.MouseDown += (s, e) =>
            {
                GlControl.Focus();
                Mouse.Capture(GlControl, CaptureMode.SubTree);
                if (_mouseButtonsDown.Add(e.ChangedButton))
                    blackHoleEngine?.InputMouseDown(MapBtn(e.ChangedButton));
            };

            // Use BOTH bubbling and tunneling to avoid missed mouse ups: they clear the DIRTY state of the engine!
            GlControl.MouseUp += GlControl_MouseUp;
            GlControl.PreviewMouseUp += GlControl_MouseUp;

            
            GlControl.MouseMove += (s, e) =>
            {
                var p = e.GetPosition(GlControl);  // DIPs
                // PIXELS, not DIPs
                blackHoleEngine?.InputMouseMove((float)(p.X * dpi.DpiScaleX), (float)(p.Y * dpi.DpiScaleY));
            };

            GlControl.MouseWheel += (s, e) =>
            {
                blackHoleEngine?.InputMouseWheel(0, e.Delta / 120f);
            };

            GlControl.LostMouseCapture += (s, e) => ForceReleaseAll();
            this.Deactivated += (s, e) => ForceReleaseAll(); // Window-level safety
        }

        void ForceReleaseAll()
        {
            if (_mouseButtonsDown.Count == 0) return;
            foreach (var b in _mouseButtonsDown.ToArray())
                blackHoleEngine?.InputMouseUp(MapBtn(b));
            _mouseButtonsDown.Clear();
            if (Mouse.Captured == GlControl) 
                Mouse.Capture(null);
        }

        private void GlControl_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_mouseButtonsDown.Remove(e.ChangedButton))
                blackHoleEngine?.InputMouseUp(MapBtn(e.ChangedButton));

            if (_mouseButtonsDown.Count == 0 && Mouse.Captured == GlControl)
                Mouse.Capture(null);
        }


        int refreshRateSampleCount = 0;
        int refreshRateSampleMax = 10000;
        // Framerate control
        double accumulatedSeconds = 0;
        readonly TimeSpan minFrame = TimeSpan.FromSeconds(1.0 / FPS);
        private void GlControl_Render(TimeSpan span)
        {
            if (!_initialized)
            {
                blackHoleEngine = new BlackHoleEngine(new GameSetup(), _host);
                blackHoleEngine.Load();
                _initialized = true;
            }
            if (_pendingSize != null)
            {
                blackHoleEngine?.Resize((int)_pendingSize.Value.X, (int)_pendingSize.Value.Y);
                _pendingSize = null;
            }

            accumulatedSeconds += span.TotalSeconds;

            //  doesn't work well: you need to stay aligned to vsync!
            //if (accumulatedTime < minFrame)
            //{
            //    // Skip this frame
            //    return;
            //}

            // adaptive refresh estimate attempt -- poor
            //if (++refreshRateSampleCount >= refreshRateSampleMax && accumulatedSeconds > 0.5)
            //{
            //    double refreshHz = refreshRateSampleCount / accumulatedSeconds; // measured display/WPF tick rate
            //    int desiredDiv = (int)Math.Max(1, Math.Round(refreshHz / FPS));

            //    // avoid jittery flips
            //    if (Math.Abs(desiredDiv - vsyncDiv) >= 1)
            //        vsyncDiv = desiredDiv;

            //    refreshRateSampleCount = 0;
            //    accumulatedSeconds = 0;
            //}

            // skip at constant rate, not by accum time! vsync!
            if (++frameCount % vsyncDiv != 0)
            {
                // Skip this frame
                return;
            }

            blackHoleEngine?.Render();

        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (blackHoleEngine == null) return;
            blackHoleEngine.GameSetup.ShowDisk = !blackHoleEngine.GameSetup.ShowDisk;
            blackHoleEngine.Invalidate();
            GlControl.InvalidateVisual();
        }
    }
}