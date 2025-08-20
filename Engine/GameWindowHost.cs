using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlackHole
{
    public class GameWindowHost : IGLHost
    {
        private readonly GameWindow _gw;
        public GameWindow GameWindow => _gw;
        public GameWindowHost(OpenTK.Windowing.Desktop.GameWindow gw) => _gw = gw;
        public void MakeCurrent() => _gw.MakeCurrent();
        public void SwapBuffers() => _gw.SwapBuffers();
        public (int Width, int Height) GetPixelSize() => (_gw.Size.X, _gw.Size.Y);

    }
    
}
