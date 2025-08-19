using System;
using System.IO;
using System.Text;
using OpenTK.Graphics.OpenGL4;

namespace BlackHole
{
    public static class ShaderHelper
    {
        public static int Compile(ShaderType type, string src)
        {
            int sh = GL.CreateShader(type);
            GL.ShaderSource(sh, src);
            GL.CompileShader(sh);
            GL.GetShader(sh, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
            {
                string log = GL.GetShaderInfoLog(sh);
                GL.DeleteShader(sh);
                throw new Exception($"{type} compile error:\n{log}");
            }
            return sh;
        }

        public static int CreateProgramFromStrings(string vs, string fs)
        {
            int v = Compile(ShaderType.VertexShader, vs);
            int f = Compile(ShaderType.FragmentShader, fs);

            int p = GL.CreateProgram();
            GL.AttachShader(p, v);
            GL.AttachShader(p, f);
            GL.LinkProgram(p);
            GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
            {
                string log = GL.GetProgramInfoLog(p);
                GL.DeleteShader(v);
                GL.DeleteShader(f);
                GL.DeleteProgram(p);
                throw new Exception($"Program link error:\n{log}");
            }
            GL.DetachShader(p, v);
            GL.DetachShader(p, f);
            GL.DeleteShader(v);
            GL.DeleteShader(f);
            return p;
        }

        public static int CreateComputeProgramFromFile(string path)
        {
            string src = File.ReadAllText(path, Encoding.UTF8);
            int cs = Compile(ShaderType.ComputeShader, src);
            int p = GL.CreateProgram();
            GL.AttachShader(p, cs);
            GL.LinkProgram(p);
            GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0)
            {
                string log = GL.GetProgramInfoLog(p);
                GL.DeleteShader(cs);
                GL.DeleteProgram(p);
                throw new Exception($"Compute program link error:\n{log}");
            }
            GL.DetachShader(p, cs);
            GL.DeleteShader(cs);
            return p;
        }
    }
}
