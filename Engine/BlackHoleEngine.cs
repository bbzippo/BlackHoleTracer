using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlackHole
{
    using BlackHole;
    using OpenTK.Graphics.OpenGL;
    using OpenTK.Mathematics;
    using StbImageSharp;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    public class BlackHoleEngine : IDisposable
    {
        // opengl bindings
        int quadVao = 0, quadVbo = 0;
        int lineVAO, lineVBO = 0;
        int pointsVAO, pointsVBO = 0;
        int outputTex = 0;
        int envTex = 0;

        int cameraUBO = 0; // binding=1
        int diskUBO = 0;   // binding=2
        int objectsUBO = 0; // binding=3
        int pathsSSBO = 0; // binding=4

        // todo: move to gamesetup
        int COMPUTE_WIDTH = 20 * 16;
        int COMPUTE_HEIGHT = 20 * 16;

        int WIDTH, HEIGHT;        

        CameraState camera = new();
        
        readonly GameSetup _gameSetup;
        public GameSetup GameSetup => _gameSetup;

        // for visualizaing geodesics
        const int MaxTracePaths = 5;
        const int MaxTraceSamples = 5000;

        List<System.Drawing.Point> pathTraceSeedPixels = new();

        private Shaders _shaders = new();

        volatile bool _isDirty = true;
        public void Invalidate() => _isDirty = true;
        public void Validate() => _isDirty = false;

        readonly IGLHost _host;

        public BlackHoleEngine(GameSetup gameSetup, IGLHost host)
        {
            _gameSetup = gameSetup;
            _host = host;

            WIDTH = gameSetup.WindowWidth;
            HEIGHT = gameSetup.WindowHeight;

            camera.radius = 15.5 * _gameSetup.RS;
            camera.elevation = Math.PI / 2.2;
            camera.azimuth = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        float ScaleToShader(float meters) => meters / _gameSetup.LengthUnit;

        public void Load() 
        {   
            var debug = false;
            if (debug)
            {
                try
                {
                    GL.Enable(EnableCap.DebugOutput);
                    GL.Enable(EnableCap.DebugOutputSynchronous);
                    GL.DebugMessageCallback((src, type, id, sev, len, msg, usr) =>
                    {
                        string s = Marshal.PtrToStringAnsi(msg, len);
                        Debug.WriteLine($"GL DEBUG [{sev}] {type} {id}: {s}");
                    }, IntPtr.Zero);
                }
                catch { }
            }

            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Enable(EnableCap.DepthTest);

            _shaders.Load();

            CreateQuadAndTexture();
            CreateBGBitmap();

            // UBOs/SSBO
            cameraUBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer, cameraUBO);
            GL.BufferData(BufferTarget.UniformBuffer, 80, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 1, cameraUBO);

            diskUBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer, diskUBO);
            GL.BufferData(BufferTarget.UniformBuffer, 16, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 2, diskUBO);

            objectsUBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.UniformBuffer, objectsUBO);
            GL.BufferData(BufferTarget.UniformBuffer, 784, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 3, objectsUBO);

            pathsSSBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, pathsSSBO);
            int bytes = (4 * sizeof(float)) * MaxTracePaths * MaxTraceSamples + (sizeof(int) * MaxTracePaths);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, bytes, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4, pathsSSBO);

            GL.Enable(EnableCap.ProgramPointSize);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // set initial viewport from host size
            var (w, h) = _host.GetPixelSize();
            WIDTH = w; HEIGHT = h;
            GL.Viewport(0, 0, WIDTH, HEIGHT);

        }

        public void Resize(int width, int height) // was: OnResize(ResizeEventArgs e)
        {
            Invalidate();
            WIDTH = Math.Max(1, width);
            HEIGHT = Math.Max(1, height);
            GL.Viewport(0, 0, WIDTH, HEIGHT);
        }

        public void Render() 
        {
            if (!_isDirty)
            {
                // I know this is wrong, but this seems to make it smoother and ligher on the GPU. Might affect vsync though
                System.Threading.Thread.Sleep(5);
                return;
            }

            _host.MakeCurrent();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // geo paths — unchanged
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, pathsSSBO);
            int countsOffset = (4 * sizeof(float)) * MaxTracePaths * MaxTraceSamples;
            GL.ClearBufferSubData(BufferTarget.ShaderStorageBuffer,
                PixelInternalFormat.R32i,
                (IntPtr)countsOffset, MaxTracePaths * sizeof(int),
                PixelFormat.RedInteger, PixelType.Int, IntPtr.Zero);

            DispatchCompute();

            // background blit — unchanged
            GL.UseProgram(_shaders.screenProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, outputTex);
            _shaders.SetParam(_shaders.screenProgram, "screenTexture", 0);

            GL.BindVertexArray(quadVao);
            GL.Disable(EnableCap.DepthTest);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            GL.Enable(EnableCap.DepthTest);

            HandleGeoPaths();

            _host.SwapBuffers(); // <— replaces direct SwapBuffers() call
            _isDirty = camera.moving;
            IsCameraMoving = camera.moving;
        }

        public bool IsCameraMoving = false;

        public void Dispose() 
        {
            _shaders.Unload();
            if (outputTex != 0) GL.DeleteTexture(outputTex);
            if (quadVbo != 0) GL.DeleteBuffer(quadVbo);
            if (quadVao != 0) GL.DeleteVertexArray(quadVao);
            if (cameraUBO != 0) GL.DeleteBuffer(cameraUBO);
            if (diskUBO != 0) GL.DeleteBuffer(diskUBO);
            if (objectsUBO != 0) GL.DeleteBuffer(objectsUBO);
            if (pathsSSBO != 0) GL.DeleteBuffer(pathsSSBO);
        }

        //  Input hooks 
        public void InputMouseDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button)
            => camera.ProcessMouseButton(button, OpenTK.Windowing.GraphicsLibraryFramework.InputAction.Press);

        public void InputMouseUp(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton button)
            => camera.ProcessMouseButton(button, OpenTK.Windowing.GraphicsLibraryFramework.InputAction.Release);

        public void InputMouseMove(float x, float y)
        {
            camera.ProcessMouseMove(x, y);
            if (camera.dragging) Invalidate();
        }

        public void InputMouseWheel(float dx, float dy)
        {
            camera.ProcessScroll(dx, dy);
            Invalidate();
        }

        public void InputKeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys key)
        {
            if (key == OpenTK.Windowing.GraphicsLibraryFramework.Keys.Escape)
            {
                camera.dragging = camera.panning = false;
                Invalidate();
            }
        }

        private void DispatchCompute()
        {
            UploadCameraUBO();
            UploadDiskUBO();
            GL.UseProgram(_shaders.computeProgram);

            GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindTexture(TextureTarget.Texture2D, envTex);

            GL.BindImageTexture(0, outputTex, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);

            // game setup params for computeProgram
            int steps = camera.moving ? _gameSetup.IntegrationStepsMoving : _gameSetup.IntegrationStepsStill; // more when still?
            _shaders.SetParam(_shaders.computeProgram, "uSteps", steps);
            _shaders.SetParam(_shaders.computeProgram, "uDLambdaBase", _gameSetup.AffineStep);
            _shaders.SetParam(_shaders.computeProgram, "uEscapeR", _gameSetup.EscapeR);
            _shaders.SetParam(_shaders.computeProgram, "uScaledRS", _gameSetup.RS_scaled);

            _shaders.SetParam(_shaders.computeProgram, "uShowBricks", _gameSetup.ShowBricks ? 1 : 0);
            _shaders.SetParam(_shaders.computeProgram, "uHorizonHandling", (int)_gameSetup.HorizonHandling);

            PrepareGeoPaths();

            var groupsX = Math.Max(1, (uint)Math.Ceiling(COMPUTE_WIDTH / 16.0));
            var groupsY = Math.Max(1, (uint)Math.Ceiling(COMPUTE_HEIGHT / 16.0));

            GL.DispatchCompute(groupsX, groupsY, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit);
            //  GL.Finish for debug if needed. it waits for all GL work to complete synchronously
        }

        void HandleGeoPaths()
        {
            if (!_gameSetup.EnablePaths) return;

            // Map SSBO to CPU
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, pathsSSBO);
            IntPtr ptr = GL.MapBuffer(BufferTarget.ShaderStorageBuffer, BufferAccess.ReadOnly);
            try
            {
                // Copy to managed arrays
                int floatsCount = 4 * MaxTracePaths * MaxTraceSamples;
                float[] pathData = new float[floatsCount];
                System.Runtime.InteropServices.Marshal.Copy(ptr, pathData, 0, floatsCount);

                int countsOffsetBytes = (4 * sizeof(float)) * MaxTracePaths * MaxTraceSamples;
                int[] pathPointCounts = new int[MaxTracePaths];
                unsafe
                {
                    byte* p = (byte*)ptr.ToPointer() + countsOffsetBytes;
                    System.Runtime.InteropServices.Marshal.Copy((IntPtr)p, pathPointCounts, 0, MaxTracePaths);
                }

                List<int> vertCount = new();  // number of vertices per path

                for (int pth = 0; pth < pathTraceSeedPixels.Count; ++pth)
                {
                    int count = Math.Min(pathPointCounts[pth], MaxTraceSamples);
                    if (count < 2) continue;

                    vertCount.Add(count);

                    int baseFloat = pth * MaxTraceSamples * 4;
                    for (int i = 0; i < count; ++i)
                    {
                        float x = pathData[baseFloat + i * 4 + 0];
                        float y = pathData[baseFloat + i * 4 + 1];
                        float z = pathData[baseFloat + i * 4 + 2];

                    }
                }

                var allPointsRS = new List<Vector3>();

                var pathCounts = new List<int>();

                for (int p = 0; p < pathTraceSeedPixels.Count; ++p)
                {
                    int count = Math.Min(pathPointCounts[p], MaxTraceSamples);
                    if (count < 1) continue;

                    pathCounts.Add(count);

                    int baseIdx = p * MaxTraceSamples;
                    for (int i = 0; i < count; ++i)
                    {
                        float x = pathData[(baseIdx + i) * 4 + 0];
                        float y = pathData[(baseIdx + i) * 4 + 1];
                        float z = pathData[(baseIdx + i) * 4 + 2];

                        // basic sanity filter
                        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) continue;
                        if (MathF.Abs(x) > 1e9f || MathF.Abs(y) > 1e9f || MathF.Abs(z) > 1e9f) continue;

                        allPointsRS.Add(new Vector3(x, y, z));
                    }
                }

                if (pointsVAO == 0) { pointsVAO = GL.GenVertexArray(); pointsVBO = GL.GenBuffer(); }
                GL.BindVertexArray(pointsVAO);
                GL.BindBuffer(BufferTarget.ArrayBuffer, pointsVBO);
                GL.BufferData(BufferTarget.ArrayBuffer, allPointsRS.Count * 3 * sizeof(float),
                              allPointsRS.Count > 0 ? allPointsRS.ToArray() : null,
                              BufferUsageHint.DynamicDraw);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                GL.BindVertexArray(0);

                DrawTracePoints(allPointsRS, pathCounts);
            }
            finally
            {
                GL.UnmapBuffer(BufferTarget.ShaderStorageBuffer);
            }
        }

        List<Vector3> desiredDirs = [];
        void DrawTracePoints(List<Vector3> allPointsRS, List<int> pathCounts)
        {
            GL.UseProgram(_shaders.pointProgram);

            // Robust basis (same as your compute UBO)
            Vector3 fwd = camera.target - camera.Position();
            if (fwd.LengthSquared < 1e-12f)
            {
                // camera is on the target; nudge or pick a safe forward
                fwd = Vector3.UnitZ;                    // arbitrary but stable
            }
            fwd = Vector3.Normalize(fwd);
            // 1) Camera in RS units
            Vector3 eyeRS = camera.Position() / _gameSetup.LengthUnit;
            Vector3 tgtRS = camera.target;
            Vector3 upGuess = Vector3.UnitY;
            if (MathF.Abs(Vector3.Dot(fwd, upGuess)) > 0.999f) upGuess = Vector3.UnitZ;
            Vector3 right = Vector3.Normalize(Vector3.Cross(upGuess, fwd));
            Vector3 up = Vector3.Normalize(Vector3.Cross(fwd, right));

            // 3) View/Proj (OpenTK)
            Matrix4 view = Matrix4.LookAt(eyeRS, tgtRS, up);
            float fovRad = camera.fovRad;
            float aspect = (float)WIDTH / Math.Max(1, HEIGHT);
            Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(fovRad, aspect, 0.01f, 5000f);

            // 4) IMPORTANT: row-major path -> vp = view * proj
            Matrix4 vp = view * proj;

            // 5) Upload with transpose = true (row-major -> column-major for GLSL)
            _shaders.SetParam(_shaders.pointProgram, "uViewProj", vp);
            _shaders.SetParam(_shaders.pointProgram, "uPointSize", 3.0f);
            _shaders.SetParam(_shaders.pointProgram, "uColor", new Vector3(1f, 1f, 1f));

            GL.BindVertexArray(pointsVAO);

            // Single draw for all points:
            //GL.DrawArrays(PrimitiveType.Points, 0, allPointsRS.Count);

            // Or per-path with different colors 

            int start = 0;
            for (int p = 0; p < pathCounts.Count; ++p)
            {
                int n = pathCounts[p];
                if (n <= 0) continue;
                // set color per path if you like
                GL.DrawArrays(PrimitiveType.Points, start, n - 1);
                start += n;
            }

            // ---------- LINES ----------
            bool connectTheDots = true;
            if (connectTheDots)
            {
                GL.UseProgram(_shaders.lineProgram);
                _shaders.SetParam(_shaders.lineProgram, "uViewProj", vp);
                _shaders.SetParam(_shaders.lineProgram, "uColor", new Vector3(0.9f, 0.9f, 1.0f));

                GL.LineWidth(1.0f);
                start = 0;
                for (int p = 0; p < pathCounts.Count; ++p)
                {
                    int n = pathCounts[p];
                    if (n > 3)
                        GL.DrawArrays(PrimitiveType.LineStrip, start, n - 1);
                    start += Math.Max(n, 0);
                }
            }
            GL.BindVertexArray(0);
            GL.Enable(EnableCap.DepthTest);
        }

        private void PrepareGeoPaths()
        {
            if (!_gameSetup.EnablePaths) return;
            // Trace screen pixels or directions
            //if (desiredDirs.Count < 1)
            {
                desiredDirs.Clear();
                // Build n rays on a cone (angle alpha) around forward, with evenly spaced azimuth phi.
                Vector3 fwd = Vector3.Normalize(camera.target - camera.Position());
                Vector3 upGuess = Vector3.UnitY;
                if (MathF.Abs(Vector3.Dot(fwd, upGuess)) > 0.999f) upGuess = Vector3.UnitZ;
                Vector3 right = Vector3.Normalize(Vector3.Cross(upGuess, fwd));
                Vector3 up = Vector3.Normalize(Vector3.Cross(fwd, right));

                // Orthonormal basis around forward
                float alpha = 0.08f;       // cone half-angle in radians 

                //var seeds = new List<(Vector3 posRS, Vector3 dirRS)>();
                for (int k = 0; k < MaxTracePaths; ++k)
                {
                    float phi = 2f * MathF.PI * k / MaxTracePaths;
                    Vector3 ring = MathF.Cos(phi) * right + MathF.Sin(phi) * up;
                    Vector3 dir = Vector3.Normalize(MathF.Cos(alpha) * fwd + MathF.Sin(alpha) * ring);
                    desiredDirs.Add((dir));      // upload to seeds SSBO (paths.comp) or map to pixels if using per-pixel tracing
                }
            }

            pathTraceSeedPixels = [];
            foreach (var dir in desiredDirs)
            {
                if (camera.WorldDirToComputePixel(dir, COMPUTE_WIDTH, COMPUTE_HEIGHT, out var xi, out var yi, out var behind))
                {
                    pathTraceSeedPixels.Add(new(xi, yi));
                }
            }

            _shaders.SetParam(_shaders.computeProgram, "uNumPaths", pathTraceSeedPixels.Count);
            _shaders.SetParam(_shaders.computeProgram, "uPathStride", camera.moving ? _gameSetup.PathStride : _gameSetup.PathStride);

            void SetIV2(string n, int i, int x, int y)
            {
                int loc = GL.GetUniformLocation(_shaders.computeProgram, n + "[" + i + "]");
                if (loc >= 0) GL.Uniform2(loc, x, y);
            }
            for (int i = 0; i < pathTraceSeedPixels.Count; ++i)
            {
                var pix = pathTraceSeedPixels[i];
                SetIV2("uPathPix", i, pix.X, pix.Y);
            }
        }

        private void CreateQuadAndTexture()
        {
            // two-triangle fullscreen quad
            float[] verts =
            {
                // pos    // uv
                -1f,  1f,  0f, 1f,
                -1f, -1f,  0f, 0f,
                 1f, -1f,  1f, 0f,
                -1f,  1f,  0f, 1f,
                 1f, -1f,  1f, 0f,
                 1f,  1f,  1f, 1f
            };

            quadVao = GL.GenVertexArray();
            quadVbo = GL.GenBuffer();

            GL.BindVertexArray(quadVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // output texture for the compute shader
            outputTex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, outputTex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f,
                Math.Max(1, COMPUTE_WIDTH), Math.Max(1, COMPUTE_HEIGHT),
                0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindVertexArray(0);
        }

        private void CreateBGBitmap()
        {
            envTex = GL.GenTexture();
            GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindTexture(TextureTarget.Texture2D, envTex);

            var bmp = ImageResult.FromStream(File.OpenRead("galaxy.png"), ColorComponents.RedGreenBlueAlpha);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
              bmp.Width, bmp.Height, 0,
              PixelFormat.Rgba, PixelType.UnsignedByte, bmp.Data);

            // Mipmaps & filtering
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            // Optional anisotropy
            GL.GetFloat((GetPName)All.TextureMaxAnisotropyExt, out float maxAniso);
            if (maxAniso > 0)
                GL.TexParameter(TextureTarget.Texture2D, (TextureParameterName)All.TextureMaxAnisotropyExt, MathF.Min(8f, maxAniso));

            // Bind to compute
            _shaders.SetParam(_shaders.computeProgram, "uEnvIntensity", 1.2f);
        }

        private void UploadCameraUBO()
        {
            camera.BuildBasis(out var fwd, out var right, out var up);
            var camPos = camera.Position() / _gameSetup.LengthUnit;

            var ubo = new CameraUBOStd140
            {
                camPos = camPos,
                camRight = right,
                camUp = up,
                camForward = fwd,
                tanHalfFov = camera.tanHalfFov,
                aspect = (float)WIDTH / Math.Max(1, (float)HEIGHT),
                moving = (camera.dragging || camera.panning) ? 1 : 0
            };

            int size = Marshal.SizeOf<CameraUBOStd140>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(ubo, ptr, false);
                GL.BindBuffer(BufferTarget.UniformBuffer, cameraUBO);
                GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, size, ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        private void UploadDiskUBO()
        {   
            // inner/outer radii around the BH
            float r1 = ScaleToShader(_gameSetup.RS * 2.7f);
            float r2 = ScaleToShader(_gameSetup.RS * 4.2f);

            if (!_gameSetup.ShowDisk)
            {
                r1 = r2 = 0f; // hide disk
            }

            Span<float> disk = stackalloc float[2] {
                r1,
                r2
            };
            GL.BindBuffer(BufferTarget.UniformBuffer, diskUBO);
            unsafe
            {
                fixed (float* p = disk)
                {
                    GL.BufferSubData(BufferTarget.UniformBuffer, IntPtr.Zero, 4 * sizeof(float), (IntPtr)p);
                }
            }
        }

    }

}
