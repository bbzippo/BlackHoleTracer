C# and OpenTK 

Raytracing around a blackhole in the honest Schwarzschild metric using Runge-Kutta 4.
Inspired by and based on https://github.com/kavan010/black_hole .
Demonstrates lensing of a disk and of the background texture.
Has the experimental capability to visualize actual light ray geodesics.

./Engine:

The engine and a stable cross-platform implementation using OpenTK GameWindow. No GUI or CLI for tweaking settings.
Tested on RTX4060. Smooth mouse control at 16fps and 640x480 compute resolution. Can render much higher res too. 
Contains a ready to run VisualStuio solution.

./WpfControlUI:

A slow and almost abandoned Windows implementation attempt with an embedded GL window based on OpenTK.Wpf.GLWpfControl.
Instructive, because it exposed an issue with vsync: you can't smoothly render if you don't align frame rate and refresh rate. 
If you want to render at slow FPS, you need to skip frames in multiples of the monitor refresh rate. 
Also, the mouse input pipeline adds latency. 

./BlackHoleUI:

Ongoing. Windows UI for tweaking settings that will show side-by-side with the GL GameWindow. Hopefully will come out smoother than the Wpf-synced rendering.

The whole ray tracing math is contained in the compute shader Engine/geodesicTracer.js (js because javascript intellisense in VisualStudio works well for me with glsl).


BUG: The divide by zero at the diameter.

Plan:

Finally build UI for tweaking settings.
Allow to change compute res on the fly.

Later:

Explore regularized coordinates. 
Explore camera basis as a tetrad.

