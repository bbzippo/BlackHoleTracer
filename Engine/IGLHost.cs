using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlackHole
{
    public interface IGLHost
    {
        // Called before any GL calls each frame (GLWpfControl.MakeCurrent() or GameWindow.MakeCurrent()).
        void MakeCurrent();

        // Swap back/front buffers. On WPF, this can be a no-op.
        void SwapBuffers();

        // Current pixel size for viewport and aspect (WPF should return DPI-corrected pixels).
        (int Width, int Height) GetPixelSize();
    }


}
