using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using StbTrueTypeSharp;

namespace SVGtesselationShader
{
    public class TextRenderer : IDisposable
    {
        // Store glyph info for each character
        public struct GlyphInfo
        {
            public float X0, Y0, X1, Y1;  // glyph bounds in the atlas
            public float XAdvance;        // how far to move pen after this char
            public float XOffset;         // offset from pen position to left of glyph
            public float YOffset;         // offset from pen baseline to top of glyph
        }

        private Dictionary<int, GlyphInfo> _glyphs;  // glyph info by character code
        private int _textureID;                      // OpenGL font atlas texture
        private int _vao, _vbo;                      // For rendering text
        private float _atlasWidth, _atlasHeight;

        private int _textShaderProgram;
        private int _uProjection, _uTextColor, _uFontAtlas;

        public TextRenderer(int textShaderProgram, int atlasWidth, int atlasHeight, string fontPath, float fontPixelHeight)
        {
            _glyphs = new Dictionary<int, GlyphInfo>();
            _atlasWidth = atlasWidth;
            _atlasHeight = atlasHeight;
            _textShaderProgram = textShaderProgram;

            // Query uniform locations in the text shader
            _uProjection = GL.GetUniformLocation(textShaderProgram, "projection");
            _uTextColor = GL.GetUniformLocation(textShaderProgram, "textColor");
            _uFontAtlas = GL.GetUniformLocation(textShaderProgram, "fontAtlas");

            // 1) Generate empty atlas image
            byte[] pixels = new byte[atlasWidth * atlasHeight];

            // 2) Load TTF file into memory and bake characters into atlas
            var ttfData = File.ReadAllBytes(fontPath);
            int firstChar = 32;
            int numChars = 95; // ASCII 32..126

            // Create the baked character array
            var bakedChars = new StbTrueType.stbtt_bakedchar[numChars];

            // Bake the font bitmap
            StbTrueType.stbtt_BakeFontBitmap(
                ttfData, 0, fontPixelHeight, pixels, atlasWidth, atlasHeight, firstChar, numChars, bakedChars
            );

            // 4) Build a dictionary mapping char codes -> glyph info
            for (int c = firstChar; c < firstChar + numChars; c++)
            {
                int i = c - firstChar;
                var bc = bakedChars[i];

                GlyphInfo gi = new GlyphInfo();
                gi.X0 = bc.x0;
                gi.Y0 = bc.y0;
                gi.X1 = bc.x1;
                gi.Y1 = bc.y1;
                gi.XAdvance = bc.xadvance;
                gi.XOffset = bc.xoff;
                gi.YOffset = bc.yoff;

                _glyphs[c] = gi;
            }

            // 5) Create OpenGL texture from the `pixels` array
            _textureID = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, _textureID);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8,
                atlasWidth, atlasHeight, 0,
                PixelFormat.Red, PixelType.UnsignedByte, pixels
            );

            // Set texture params
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            // 6) Create VAO/VBO for rendering text quads
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

            // Each vertex = (vec2 pos, vec2 uv) => 4 floats total
            int stride = 4 * sizeof(float);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);
            GL.EnableVertexAttribArray(0);

            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // Unbind for safety
            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// Renders the given text at (x,y) in *pixel coordinates* with a specific scale.
        /// Provide an orthographic projection that matches your screen's pixel size if you want 1:1 positioning.
        /// </summary>
        public void RenderText(string text, float x, float y, Vector4 color, Matrix4 projection, float scale = 1.0f)
        {
            if (string.IsNullOrEmpty(text)) return;

            GL.UseProgram(_textShaderProgram);

            // Upload the projection matrix
            GL.UniformMatrix4(_uProjection, false, ref projection);

            // Upload text color
            GL.Uniform4(_uTextColor, color);

            // Bind the font atlas to unit 0
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _textureID);
            GL.Uniform1(_uFontAtlas, 0);

            // Build a dynamic vertex array for all glyphs in this string
            // We'll do 6 vertices (2 triangles) per character
            List<float> verts = new List<float>(text.Length * 6 * 4);

            float startX = x;
            float penX = x;
            float penY = y;

            // Bake quads for each character
            for (int i = 0; i < text.Length; i++)
            {
                int c = text[i];
                if (!_glyphs.TryGetValue(c, out var g))
                {
                    // Not in our baked range
                    continue;
                }

                float x0 = penX + g.XOffset * scale;                  // left in screen coords
                float y0 = penY - g.YOffset * scale;                  // top (STB uses y offset downwards)
                float x1 = x0 + (g.X1 - g.X0) * scale;
                float y1 = y0 + (g.Y1 - g.Y0) * scale;                // Using a top-down coordinate system

                float s0 = g.X0 / _atlasWidth;
                float t0 = g.Y0 / _atlasHeight;
                float s1 = g.X1 / _atlasWidth;
                float t1 = g.Y1 / _atlasHeight;

                // 2 triangles
                // (x0, y0)   top-left
                // (x1, y0)   top-right
                // (x0, y1)   bottom-left
                // (x1, y1)   bottom-right

                // Tri #1
                verts.Add(x0); verts.Add(y0); verts.Add(s0); verts.Add(t0);
                verts.Add(x1); verts.Add(y0); verts.Add(s1); verts.Add(t0);
                verts.Add(x0); verts.Add(y1); verts.Add(s0); verts.Add(t1);

                // Tri #2
                verts.Add(x1); verts.Add(y0); verts.Add(s1); verts.Add(t0);
                verts.Add(x1); verts.Add(y1); verts.Add(s1); verts.Add(t1);
                verts.Add(x0); verts.Add(y1); verts.Add(s0); verts.Add(t1);

                penX += g.XAdvance * scale;  // Advance pen with scaled advance
            }

            // Upload this dynamic array to the GPU
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StreamDraw);

            // Issue the draw
            GL.DrawArrays(PrimitiveType.Triangles, 0, verts.Count / 4);

            // Cleanup
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        public bool TryGetGlyphInfo(char c, out GlyphInfo glyphInfo)
        {
            return _glyphs.TryGetValue(c, out glyphInfo);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteVertexArray(_vao);
            GL.DeleteTexture(_textureID);
        }
    }
} 