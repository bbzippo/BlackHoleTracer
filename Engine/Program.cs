using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
namespace BlackHole
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
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
            
            gw.Load += () =>
            {
                engine.Load();
                // make sure the viewport matches the initial window size
                engine.Resize(gw.Size.X, gw.Size.Y);
            };

            gw.Resize += e => // implicit type lambda syntax for fun!
            {
                engine.Resize(e.Size.X, e.Size.Y);
            };

            gw.RenderFrame += e =>
            {
                // GameWindowHost.MakeCurrent() will get called by engine.Render()
                engine.Render();
            };

            // --- input passthrough (1:1, no behavior change) ---
            gw.MouseDown += (MouseButtonEventArgs e) =>
            {
                if (e.Action == InputAction.Press)
                    engine.InputMouseDown(e.Button);
            };

            gw.MouseUp += (MouseButtonEventArgs e) =>
            {
                if (e.Action == InputAction.Release)
                    engine.InputMouseUp(e.Button);
            };

            gw.MouseMove += (MouseMoveEventArgs e) =>
            {
                engine.InputMouseMove(e.X, e.Y);
            };

            gw.MouseWheel += (MouseWheelEventArgs e) =>
            {
                engine.InputMouseWheel(e.OffsetX, e.OffsetY);
            };

            gw.KeyDown += (KeyboardKeyEventArgs e) =>
            {
                engine.Input_KeyDown(e.Key);
            };

            // optional: if you use dt on CPU side
            //gw.UpdateFrame += (FrameEventArgs e) =>
            //{
            //    engine.Update(e.Time);
            //};

            gw.Run();
        }
    }
}
