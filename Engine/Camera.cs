using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BlackHole
{
    // std140 camera block: 4×(vec3+pad) + 4 scalars = 80 bytes
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    struct CameraUBOStd140
    {
        [FieldOffset(0)] public Vector3 camPos;
        [FieldOffset(12)] public float _pad0;

        [FieldOffset(16)] public Vector3 camRight;
        [FieldOffset(28)] public float _pad1;

        [FieldOffset(32)] public Vector3 camUp;
        [FieldOffset(44)] public float _pad2;

        [FieldOffset(48)] public Vector3 camForward;
        [FieldOffset(60)] public float _pad3;

        [FieldOffset(64)] public float tanHalfFov;
        [FieldOffset(68)] public float aspect;
        [FieldOffset(72)] public int moving; // 0/1
        [FieldOffset(76)] public int _pad4;
    }

    class CameraState
    {
        public Vector3 target = Vector3.Zero;
        public double radius = 1.0e8;
        public double minRadius = 1e10, maxRadius = 1e12;
        public double azimuth = 0.0, elevation = Math.PI / 2.0;
        public float orbitSpeed = 0.005f;
        public double zoomSpeed = 5e9;
        public bool dragging = false, panning = false;
        public bool moving = false;
        public double lastX = 0, lastY = 0;
        public float fovRad;
        public float tanHalfFov;

        public CameraState()
        {
            fovRad = MathHelper.DegreesToRadians(30);
            tanHalfFov = (float)Math.Tan(fovRad);
        }

        public Vector3 Position()
        {
            float e = MathHelper.Clamp((float)elevation, 0.01f, (float)Math.PI - 0.01f);
            return new Vector3(
                (float)(radius * Math.Sin(e) * Math.Cos(azimuth)), //? flip axis here?
                (float)(radius * Math.Cos(e)),
                (float)(radius * Math.Sin(e) * Math.Sin(azimuth))
            );
        }
        public void Update() { target = Vector3.Zero; moving = dragging || panning; }
        public void ProcessMouseMove(double x, double y)
        {
            float dx = (float)(x - lastX), dy = (float)(y - lastY);
            if (dragging && !panning)
            {
                azimuth += dx * orbitSpeed;
                elevation -= dy * orbitSpeed;
                elevation = MathHelper.Clamp((float)elevation, 0.01f, (float)Math.PI - 0.01f);
            }
            lastX = x; lastY = y; Update();
        }
        public void ProcessMouseButton(MouseButton btn, InputAction action)
        {
            if (btn == MouseButton.Left || btn == MouseButton.Middle)
            {
                if (action == InputAction.Press) 
                { 
                    dragging = true; 
                    panning = false; 
                }
                else if (action == InputAction.Release) 
                { 
                    dragging = false; 
                    panning = false; 
                }
            }
        }
        public void ProcessScroll(double xoff, double yoff)
        {
            radius -= yoff * zoomSpeed;
            radius = MathHelper.Clamp((float)radius, (float)minRadius, (float)maxRadius);
            Update();
        }


        public void BuildBasis(out Vector3 fwd, out Vector3 right, out Vector3 up)
        {
            fwd = this.target - this.Position();
            if (fwd.LengthSquared < 1e-12f) fwd = Vector3.UnitZ;
            fwd = Vector3.Normalize(fwd);

            var upGuess = Vector3.UnitY;
            if (MathF.Abs(Vector3.Dot(fwd, upGuess)) > 0.999f) upGuess = Vector3.UnitZ;

            right = Vector3.Normalize(Vector3.Cross(upGuess, fwd)); // upGuess × fwd
            up = Vector3.Normalize(Vector3.Cross(fwd, right));   // fwd × right
        }

        // Shader mapping variant selector:
        // If your shader does: dir = normalize(u*Right + v*Up + Forward)  => set vSign = +1
        // If your shader does: dir = normalize(u*Right - v*Up + Forward)  => set vSign = -1
        const float DefaultVSign = +1f; // change to -1f if your compute shader uses -v

        public Vector3 PixelToWorldDir(
            int x, int y, int W, int H,
            float vSign = DefaultVSign)
        {
            BuildBasis(out var fwd, out var right, out var up);
            float aspect = (float)W / Math.Max(1, H);

            float u = (2f * ((x + 0.5f) / W) - 1f) * aspect * tanHalfFov;
            float v = (1f - 2f * ((y + 0.5f) / H)) * tanHalfFov;

            // Note vSign here
            return Vector3.Normalize(u * right + vSign * v * up + fwd);
        }

        public bool WorldDirToComputePixel(
            Vector3 dirWorld, int W, int H,
            
            out int xi, out int yi, out bool behind, float vSign = DefaultVSign)
        {
            BuildBasis(out var fwd, out var right, out var up);
            float aspect = (float)W / Math.Max(1, H);

            Vector3 D = Vector3.Normalize(dirWorld);
            float c = Vector3.Dot(D, fwd);
            behind = (c <= 1e-6f);
            if (behind) { xi = yi = -1; return false; }

            float a = Vector3.Dot(D, right);
            float b = Vector3.Dot(D, up);

            // Invert the shader mapping:
            //   u_param = a/c,  v_param = (b/c) with vSign applied
            float u_param = a / c;
            float v_param = (b / c) / vSign;

            float xFloat = ((u_param / (aspect * tanHalfFov) + 1f) * 0.5f) * W - 0.5f;
            float yFloat = ((1f - v_param / tanHalfFov) * 0.5f) * H - 0.5f;

            xi = (int)MathF.Round(xFloat);
            yi = (int)MathF.Round(yFloat);

            return xi >= 0 && yi >= 0 && xi < W && yi < H;
        }

    }
}
