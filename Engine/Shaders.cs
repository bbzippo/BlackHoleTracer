using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace BlackHole
{
    internal class Shaders
    {
        public int screenProgram = 0;
        public int computeProgram = 0;
        public int lineProgram = 0;
        public int pointProgram = 0;

        public void Unload()
        {
            if (screenProgram != 0) GL.DeleteProgram(screenProgram);
            if (computeProgram != 0) GL.DeleteProgram(computeProgram);
            if (pointProgram != 0) GL.DeleteProgram(pointProgram);
            if (lineProgram != 0) GL.DeleteProgram(lineProgram);
        }

        public void Load() {
            // Fullscreen textured quad shader
            screenProgram = ShaderHelper.CreateProgramFromStrings(
                @"#version 330 core
                  layout(location=0) in vec2 aPos;
                  layout(location=1) in vec2 aUV;
                  out vec2 vUV;
                  void main(){ gl_Position = vec4(aPos,0,1); vUV = aUV; }",
                @"#version 330 core
                  in vec2 vUV; out vec4 FragColor;
                  uniform sampler2D screenTexture;
                  void main(){ FragColor = texture(screenTexture, vUV); }"
            );

            lineProgram = ShaderHelper.CreateProgramFromStrings(
                @"#version 330 core
                layout(location=0) in vec3 aPosRS;
                uniform mat4 uViewProj; // in RS units
                void main(){ gl_Position = uViewProj * vec4(aPosRS, 1.0); }
                ",
                @"#version 330 core
                uniform vec3 uColor;
                out vec4 FragColor;
                void main(){ FragColor = vec4(uColor, 1.0); }
                "
            );

            pointProgram = ShaderHelper.CreateProgramFromStrings(
                @"#version 330 core
                layout(location=0) in vec3 aPosRS;   // positions in RS units
                uniform mat4 uViewProj;               // Projection * View (RS units)
                uniform float uPointSize;             // in pixels
                void main() {
                    gl_Position =  uViewProj * vec4(aPosRS, 1.0);
                    gl_PointSize = uPointSize;        // requires ProgramPointSize
                }
                ",
                @"#version 330 core
                uniform vec3 uColor;
                out vec4 FragColor;

                // Comment out the discard if you prefer square points
                void main() {
                    // make circular point sprite
                    vec2 p = gl_PointCoord * 2.0 - 1.0;
                    float r2 = dot(p,p);
                    if (r2 > 1.0) discard;
                    FragColor = vec4(uColor, 1.0);
                }
                "
                );

            // Compute shader 
            computeProgram = ShaderHelper.CreateComputeProgramFromFile("sw-geodesic2.js");
        }

        public void SetParam(int program, string name, int val)
        {
            int loc = GL.GetUniformLocation(program, name);
            if (loc >= 0) GL.Uniform1(loc, val);
        }
        public void SetParam(int program, string name, double val)
        {
            int loc = GL.GetUniformLocation(program, name);
            if (loc >= 0) GL.Uniform1(loc, val);
        }
        public void SetParam(int program, string name, float val)
        {
            int loc = GL.GetUniformLocation(program, name);
            if (loc >= 0) GL.Uniform1(loc, val);
        }
        public void SetParam(int program, string name, Vector3 val)
        {
            int loc = GL.GetUniformLocation(program, name);
            if (loc >= 0) GL.Uniform3(loc, ref val);
        }

        public void SetParam(int program, string name, Vector4 val)
        {
            int loc = GL.GetUniformLocation(program, name);
            if (loc >= 0) GL.Uniform4(loc, ref val);
        }

        public void SetParam(int program, string name, Matrix4 val)
        {
            int loc = GL.GetUniformLocation(program, name);
            if (loc >= 0) GL.UniformMatrix4(loc, transpose: true, ref val);
        }
    }
}
