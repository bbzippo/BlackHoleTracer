using BlackHole;
using OpenTK.Wpf;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace BlackHoleUI
{
    public class WpfGlHost : IGLHost
    {
        private readonly GLWpfControl _ctrl;
        private readonly DpiScale _dpi;
        public WpfGlHost(GLWpfControl ctrl, DpiScale dpi) 
        {
            _ctrl = ctrl;
            _dpi = dpi;
        }

        public void MakeCurrent()
        {
            // no-op in WPF context. GLWpfControl manages context internally
        }
        public void SwapBuffers() 
        {
            // no-op in WPF context. GLWpfControl handles swap internally
        }

        public (int Width, int Height) GetPixelSize()
        {   
            int w = Math.Max(1, (int)(_ctrl.ActualWidth * _dpi.DpiScaleX));
            int h = Math.Max(1, (int)(_ctrl.ActualHeight * _dpi.DpiScaleY));
            return (w, h);
        }
    }
}
