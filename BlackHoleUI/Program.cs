using BlackHole;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BlackHoleUI
{   
    public static class Program
    {
        // for passing state between threads
        static volatile Action? requestCloseGw = null;
        static volatile Func<GameWindowHost>? getHost = null;

        [STAThread]
        public static void Main()
        {
            // Thread-safe queue for WPF -> GameWindow actions (run on GW thread)
            var queue = new BlockingCollection<Action<BlackHoleEngine>>();
            
            // GameWindow ON MAIN THREAD 
            var gws = GameWindowSettings.Default;
            gws.UpdateFrequency = 16; 
            var nws = new NativeWindowSettings
            {
                Title = "Black Hole",
                ClientSize = new Vector2i(1600, 1200),
                API = ContextAPI.OpenGL,
                APIVersion = new Version(4, 3),
                Profile = ContextProfile.Core,
                Vsync = VSyncMode.On,
                Flags = ContextFlags.ForwardCompatible
            };

            using var gw = new GameWindow(gws, nws);
            var host = new GameWindowHost(gw);                 
            using var engine = new BlackHoleEngine(new GameSetup(), host);

            requestCloseGw = () => queue.Add(_ => gw.Close());
            
            getHost = () => { return host; };
            
            gw.Load += () =>
            {
                engine.Load();
                // initial window size
                engine.Resize(gw.Size.X, gw.Size.Y);
            };

            gw.Resize += e => engine.Resize(e.Size.X, e.Size.Y);

            gw.MouseMove += e => engine.InputMouseMove(e.X, e.Y);
            gw.MouseWheel += e => engine.InputMouseWheel(e.OffsetX, e.OffsetY);

            gw.MouseDown += e =>
            {
                if (e.Action == InputAction.Press)
                    engine.InputMouseDown(e.Button);
            };
            gw.MouseUp += e =>
            {
                if (e.Action == InputAction.Release)
                    engine.InputMouseUp(e.Button);
            };

            gw.KeyDown += e =>
                engine.InputKeyDown(e.Key);

            //  cross-thread commands from WPF 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void ProcessPostedActions()
            {   
                if (engine.IsCameraMoving)
                {   
                    return;
                }

                // run queued actions
                while (queue.TryTake(out var action))
                    action(engine);
            }

            gw.RenderFrame += e =>
            {
                ProcessPostedActions();
                engine.Render();
            };

            // Start WPF on its own STA thread
            var wpfThread = new Thread(() =>
            {
                var app = new System.Windows.Application();
                var win = new MainWindow(
                    getHost: () => getHost?.Invoke(),
                    postToEngine: action => queue.Add(action),
                    requestCloseGameWindow: () => requestCloseGw?.Invoke()
                );
                app.Run(win);
            });
            wpfThread.IsBackground = true;
            wpfThread.SetApartmentState(ApartmentState.STA);
            wpfThread.Start();

            // When GW closes, exit process (WPF thread is background)
            gw.Run();
        }
    }


}
