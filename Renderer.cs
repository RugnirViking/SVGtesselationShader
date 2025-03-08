using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using SVGtesselationShader.Shapes;
using System.Drawing;
using LibTessDotNet;
using System.IO;
using System.Reflection;

namespace SVGtesselationShader
{
    public class Renderer : GameWindow
    {
        private int vertexBufferObject;
        private int vertexArrayObject;
        private int shaderProgram;
        private int fxaaShaderProgram;
        private int framebuffer;
        private int colorTexture;
        private int screenQuadVAO;
        private SVGHandler svgHandler;
        private Matrix4 projectionMatrix = Matrix4.Identity;
        private float svgAspectRatio;
        private (float x, float y, float width, float height) svgViewBox;
        private float rotationAngle = 0.0f;
        
        // FPS counter variables
        private int frameCount = 0;
        private float fpsTimer = 0.0f;
        private float currentFps = 0.0f;
        private const float FPS_UPDATE_INTERVAL = 0.5f;  // Update FPS every 0.5 seconds

        // Beat timing variables
        private float bpm = 120.0f;
        private float beatInterval;
        private float currentBeatTime = 0.0f;
        private int beatCount = 1;
        private bool rotatingClockwise = true;
        private float lastBeatAngle = 0.0f;
        private float targetAngle = 90.0f;  // 90 degrees per beat

        // Batched geometry for efficient rendering
        private BatchedGeometry batchedGeometry;
        private int fillVBO;
        private int fillVAO;
        private int strokeVBO;
        private int strokeVAO;
        private int fillVertexCount;
        private int strokeVertexCount;

        // Font rendering
        private TextRenderer? fpsTextRenderer;
        private TextRenderer? titleTextRenderer;
        private int textShaderProgram;
        private Matrix4 orthoProjection;
        private const string TITLE_TEXT = "LOVE YOU SIGNE";
        private float titleScale = 1.0f;

        private float CubicBezierEase(float t)
        {
            // Use a bezier curve that starts fast and slows down
            // P0 = (0,0), P1 = (0.2,0.8), P2 = (0.1,1.0), P3 = (1,1)
            float u = 1 - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            // Bezier formula
            float result = uuu * 0.0f +                    // P0 * (1-t)³
                          3 * uu * t * 0.8f +             // 3P1 * (1-t)²t
                          3 * u * tt * 1.0f +             // 3P2 * (1-t)t²
                          ttt * 1.0f;                     // P3 * t³
            
            return result;
        }

        private readonly float[] vertices = {
            // Position(x, y, z), Color(r, g, b, a)
            -0.5f, -0.5f, 0.0f,  1.0f, 0.0f, 0.0f, 1.0f, // Bottom left
             0.5f, -0.5f, 0.0f,  1.0f, 0.0f, 0.0f, 1.0f, // Bottom right
             0.0f,  0.5f, 0.0f,  1.0f, 0.0f, 0.0f, 1.0f  // Top
        };

        private readonly float[] screenQuadVertices = {
            // Position (x, y), TexCoords (u, v)
            -1.0f,  1.0f,  0.0f, 1.0f,  // Top-left
            -1.0f, -1.0f,  0.0f, 0.0f,  // Bottom-left
             1.0f, -1.0f,  1.0f, 0.0f,  // Bottom-right
             1.0f,  1.0f,  1.0f, 1.0f   // Top-right
        };

        private readonly uint[] screenQuadIndices = {
            0, 1, 2,  // First triangle
            0, 2, 3   // Second triangle
        };

        private const string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec4 aColor;

            uniform mat4 projection;
            uniform mat4 modelView;

            out vec4 color;

            void main()
            {
                gl_Position = projection * modelView * vec4(aPosition, 1.0);
                color = aColor;
            }";

        private const string fragmentShaderSource = @"
            #version 330 core
            in vec4 color;
            out vec4 fragColor;

            void main()
            {
                fragColor = color;
            }";

        private const string fxaaVertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec2 aPosition;
            layout(location = 1) in vec2 aTexCoords;
            out vec2 TexCoords;

            void main()
            {
                gl_Position = vec4(aPosition, 0.0, 1.0);
                TexCoords = aTexCoords;
            }";

        private const string fxaaFragmentShaderSource = @"
            #version 330 core
            in vec2 TexCoords;
            out vec4 FragColor;
            uniform sampler2D screenTexture;

            // FXAA settings
            const float FXAA_SPAN_MAX = 8.0;
            const float FXAA_REDUCE_MUL = 1.0/8.0;
            const float FXAA_REDUCE_MIN = 1.0/128.0;

            void main()
            {
                vec2 texelSize = 1.0 / textureSize(screenTexture, 0);
                
                // Sample neighboring texels
                vec3 rgbNW = texture(screenTexture, TexCoords + vec2(-1.0, -1.0) * texelSize).rgb;
                vec3 rgbNE = texture(screenTexture, TexCoords + vec2(1.0, -1.0) * texelSize).rgb;
                vec3 rgbSW = texture(screenTexture, TexCoords + vec2(-1.0, 1.0) * texelSize).rgb;
                vec3 rgbSE = texture(screenTexture, TexCoords + vec2(1.0, 1.0) * texelSize).rgb;
                vec3 rgbM  = texture(screenTexture, TexCoords).rgb;
                
                // Compute luma (perceived brightness) for each sample
                const vec3 luma = vec3(0.299, 0.587, 0.114);
                float lumaNW = dot(rgbNW, luma);
                float lumaNE = dot(rgbNE, luma);
                float lumaSW = dot(rgbSW, luma);
                float lumaSE = dot(rgbSE, luma);
                float lumaM  = dot(rgbM,  luma);
                
                // Compute min and max luma
                float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
                float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));
                
                // Compute edge direction
                vec2 dir;
                dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
                dir.y =  ((lumaNW + lumaSW) - (lumaNE + lumaSE));
                
                // Normalize direction and compute inverse length
                float dirReduce = max(
                    (lumaNW + lumaNE + lumaSW + lumaSE) * (0.25 * FXAA_REDUCE_MUL),
                    FXAA_REDUCE_MIN);
                
                float rcpDirMin = 1.0/(min(abs(dir.x), abs(dir.y)) + dirReduce);
                dir = min(vec2(FXAA_SPAN_MAX), max(vec2(-FXAA_SPAN_MAX),
                      dir * rcpDirMin)) * texelSize;
                
                // Sample along the edge direction
                vec3 rgbA = 0.5 * (
                    texture(screenTexture, TexCoords + dir * (1.0/3.0 - 0.5)).rgb +
                    texture(screenTexture, TexCoords + dir * (2.0/3.0 - 0.5)).rgb);
                    
                vec3 rgbB = rgbA * 0.5 + 0.25 * (
                    texture(screenTexture, TexCoords + dir * -0.5).rgb +
                    texture(screenTexture, TexCoords + dir * 0.5).rgb);
                    
                // Detect if we should keep B sample
                float lumaB = dot(rgbB, luma);
                
                // Output final color
                if(lumaB < lumaMin || lumaB > lumaMax)
                    FragColor = vec4(rgbA, 1.0);
                else
                    FragColor = vec4(rgbB, 1.0);
            }";

        private const string text_vertex_shader = @"
            #version 330 core
            layout (location = 0) in vec2 aPos;       // Screen (or world) coordinates
            layout (location = 1) in vec2 aTexCoord;  // UV in our glyph atlas

            out vec2 TexCoord;

            uniform mat4 projection;  // If drawing in screen space, you might use an orthographic projection

            void main()
            {
                gl_Position = projection * vec4(aPos, 0.0, 1.0);
                TexCoord = aTexCoord;
                    }
        ";

        private const string text_fragment_shader = @"
            #version 330 core
            in vec2 TexCoord;
            out vec4 FragColor;

            uniform sampler2D fontAtlas;
            uniform vec4 textColor;   // e.g. set this to (1.0, 1.0, 1.0, 1.0) for white text

            void main()
            {
                // Sample alpha from the font atlas texture:
                float alpha = texture(fontAtlas, TexCoord).r;  
                // The .r channel is often where monochrome glyph data is stored by StbTrueType

                FragColor = vec4(textColor.rgb, textColor.a * alpha);
            }
        ";

        public Renderer(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings, SVGHandler handler)
            : base(gameWindowSettings, nativeWindowSettings)
        {
            svgHandler = handler;
            beatInterval = 60.0f / bpm;  // Convert BPM to seconds per beat
            batchedGeometry = new BatchedGeometry();  // Initialize batchedGeometry
        }

        protected override void OnLoad()
        {
            base.OnLoad();

            GL.ClearColor(0.2f, 0.3f, 0.3f, 1.0f);

            // Create and compile main shaders
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexShaderSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompilation(vertexShader, "Vertex");

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentShaderSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompilation(fragmentShader, "Fragment");

            shaderProgram = GL.CreateProgram();
            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);
            CheckProgramLinking(shaderProgram, "Main");

            // Create and compile FXAA shaders
            int fxaaVertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(fxaaVertexShader, fxaaVertexShaderSource);
            GL.CompileShader(fxaaVertexShader);
            CheckShaderCompilation(fxaaVertexShader, "FXAA Vertex");

            int fxaaFragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fxaaFragmentShader, fxaaFragmentShaderSource);
            GL.CompileShader(fxaaFragmentShader);
            CheckShaderCompilation(fxaaFragmentShader, "FXAA Fragment");

            fxaaShaderProgram = GL.CreateProgram();
            GL.AttachShader(fxaaShaderProgram, fxaaVertexShader);
            GL.AttachShader(fxaaShaderProgram, fxaaFragmentShader);
            GL.LinkProgram(fxaaShaderProgram);
            CheckProgramLinking(fxaaShaderProgram, "FXAA");

            // Cleanup shaders
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(fxaaVertexShader);
            GL.DeleteShader(fxaaFragmentShader);

            // Setup VAO for main rendering
            vertexArrayObject = GL.GenVertexArray();
            vertexBufferObject = GL.GenBuffer();
            
            GL.BindVertexArray(vertexArrayObject);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            
            // Position attribute (x, y, z)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            
            // Color attribute (r, g, b, a)
            GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // Setup VAO for screen quad (FXAA pass)
            screenQuadVAO = GL.GenVertexArray();
            GL.BindVertexArray(screenQuadVAO);
            
            var screenQuadVBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, screenQuadVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, screenQuadVertices.Length * sizeof(float), screenQuadVertices, BufferUsageHint.StaticDraw);
            
            var screenQuadEBO = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, screenQuadEBO);
            GL.BufferData(BufferTarget.ElementArrayBuffer, screenQuadIndices.Length * sizeof(uint), screenQuadIndices, BufferUsageHint.StaticDraw);

            // Position attribute (x, y)
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            
            // Texture coordinate attribute (u, v)
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // Unbind VAO
            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);

            // Set up framebuffer
            SetupFramebuffer();

            // Store SVG viewbox for later use
            svgViewBox = svgHandler.GetViewBox();
            svgAspectRatio = svgViewBox.width / svgViewBox.height;

            // Set up projection matrix based on SVG viewbox
            UpdateProjection();
            
            // Initialize batched geometry for efficient rendering
            InitializeBatchedGeometry();

            // Set up text rendering
            InitializeTextRendering();
        }

        private void CheckShaderCompilation(int shader, string name)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(shader);
                Console.WriteLine($"{name} shader compilation error: {infoLog}");
            }
        }

        private void CheckProgramLinking(int program, string name)
        {
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(program);
                Console.WriteLine($"{name} program linking error: {infoLog}");
            }
        }

        private void SetupFramebuffer()
        {
            // Delete existing framebuffer if it exists
            if (framebuffer != 0)
                GL.DeleteFramebuffer(framebuffer);
            if (colorTexture != 0)
                GL.DeleteTexture(colorTexture);

            framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

            // Create color texture with RGBA format
            colorTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, colorTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, Size.X, Size.Y, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, colorTexture, 0);

            // Check framebuffer status
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                Console.WriteLine($"Framebuffer is not complete! Status: {status}");
            }

            // Unbind framebuffer and texture
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void UpdateProjection()
        {
            // Get the current window dimensions
            float windowWidth = Size.X;
            float windowHeight = Size.Y;
            float windowAspectRatio = windowWidth / windowHeight;

            // Calculate the projection dimensions to maintain aspect ratio
            float projectionWidth, projectionHeight;
            float offsetX = 0, offsetY = 0;

            if (windowAspectRatio > svgAspectRatio)
            {
                // Window is wider than SVG - add padding on sides
                projectionHeight = svgViewBox.height;
                projectionWidth = projectionHeight * windowAspectRatio;
                offsetX = (projectionWidth - svgViewBox.width) / 2;
            }
            else
            {
                // Window is taller than SVG - add padding on top/bottom
                projectionWidth = svgViewBox.width;
                projectionHeight = projectionWidth / windowAspectRatio;
                offsetY = (projectionHeight - svgViewBox.height) / 2;
            }

            // Create orthographic projection that maintains aspect ratio
            projectionMatrix = Matrix4.CreateOrthographicOffCenter(
                svgViewBox.x - offsetX, svgViewBox.x + svgViewBox.width + offsetX,
                svgViewBox.y + svgViewBox.height + offsetY, svgViewBox.y - offsetY, // Y is flipped in OpenGL
                -1, 1
            );

            GL.UseProgram(shaderProgram);
            int projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
            GL.UniformMatrix4(projectionLocation, false, ref projectionMatrix);
        }

        private void InitializeTextRendering()
        {
            // Compile and link the text shader
            int textVertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(textVertexShader, text_vertex_shader);
            GL.CompileShader(textVertexShader);
            CheckShaderCompilation(textVertexShader, "Text Vertex");

            int textFragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(textFragmentShader, text_fragment_shader);
            GL.CompileShader(textFragmentShader);
            CheckShaderCompilation(textFragmentShader, "Text Fragment");

            textShaderProgram = GL.CreateProgram();
            GL.AttachShader(textShaderProgram, textVertexShader);
            GL.AttachShader(textShaderProgram, textFragmentShader);
            GL.LinkProgram(textShaderProgram);
            CheckProgramLinking(textShaderProgram, "Text");

            GL.DeleteShader(textVertexShader);
            GL.DeleteShader(textFragmentShader);

            // Create orthographic projection for screen space text
            orthoProjection = Matrix4.CreateOrthographicOffCenter(0, Size.X, Size.Y, 0, -1, 1);

            // Initialize the text renderers for different font sizes
            try
            {
                string fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "OpenSans-Regular.ttf");
                fpsTextRenderer = new TextRenderer(textShaderProgram, 512, 512, fontPath, 24.0f); // Smaller font size for FPS
                titleTextRenderer = new TextRenderer(textShaderProgram, 1024, 1024, fontPath, 72.0f); // Larger font size for title
                Console.WriteLine("Text renderers initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing text renderers: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private void RenderFPSCounter()
        {
            if (fpsTextRenderer == null) return;

            // Enable blending for text rendering
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Render the FPS text in the top-left corner
            string fpsText = $"FPS: {currentFps:0.0}";
            fpsTextRenderer.RenderText(fpsText, 10, 30, new Vector4(1, 1, 0, 1), orthoProjection);

            // Disable blending when done with text
            GL.Disable(EnableCap.Blend);
        }

        private void RenderTitle()
        {
            if (titleTextRenderer == null) return;

            // Enable blending for text rendering
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            // Calculate text position - centered horizontally
            string titleText = TITLE_TEXT;
            float baseSize = 1.5f; // Base text scale
            float pulsingSize = baseSize * titleScale;

            // Calculate the exact width of the text
            float textWidth = 0.0f;
            foreach (char c in titleText)
            {
                if (titleTextRenderer.TryGetGlyphInfo(c, out var glyph))
                {
                    textWidth += glyph.XAdvance * pulsingSize;
                }
            }

            // Calculate x position to center the text
            float x = (Size.X - textWidth) / 2;
            float y = Size.Y * -0.03f; // Position at 15% from the top

            // Render the title with current pulse scale
            var titleColor = new Vector4(1.0f, 1.0f, 1.0f, 1.0f); // Gold color
            titleTextRenderer.RenderText(titleText, x, y, titleColor, orthoProjection, pulsingSize);

            // Disable blending when done with text
            GL.Disable(EnableCap.Blend);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            base.OnRenderFrame(e);

            // Update FPS counter
            frameCount++;
            fpsTimer += (float)e.Time;
            if (fpsTimer >= FPS_UPDATE_INTERVAL)
            {
                currentFps = frameCount / fpsTimer;
                frameCount = 0;
                fpsTimer = 0;
            }

            // Update beat timing
            currentBeatTime += (float)e.Time;
            if (currentBeatTime >= beatInterval)
            {
                // A beat has occurred
                beatCount++;
                currentBeatTime -= beatInterval;
                
                // Save current angle as the start angle for this beat
                lastBeatAngle = rotationAngle;
                
                // Set a new target angle (90 degrees per beat, in the current direction)
                targetAngle = rotatingClockwise ? 90.0f : -90.0f;
                
                // Reverse direction every 4 beats
                if (beatCount % 4 == 0)
                {
                    rotatingClockwise = !rotatingClockwise;
                }

                // Reset title scale to start pulsing from a smaller size
                titleScale = 0.8f;
            }

            // Calculate rotation based on bezier easing
            float t = currentBeatTime / beatInterval;
            float easedT = CubicBezierEase(t);
            rotationAngle = lastBeatAngle + easedT * targetAngle;

            // Calculate title scale with a different easing curve
            // Make it grow quickly and then settle back to normal size
            titleScale = 0.8f + 0.2f * (1.0f - (float)Math.Pow(1.0f - easedT, 3));

            // First pass: Render to framebuffer
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Render the SVG content using the batched geometry approach
            RenderSVGContent();

            // Second pass: Apply FXAA and render to screen
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            
            // Bind the color texture from the first pass
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, colorTexture);
            
            // Use the FXAA shader
            GL.UseProgram(fxaaShaderProgram);
            GL.Uniform1(GL.GetUniformLocation(fxaaShaderProgram, "screenTexture"), 0);
            GL.Uniform2(GL.GetUniformLocation(fxaaShaderProgram, "texelSize"), 1.0f / Size.X, 1.0f / Size.Y);
            
            // Render a full-screen quad to apply FXAA
            GL.BindVertexArray(screenQuadVAO);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);

            // Render title text above the grid
            RenderTitle();

            // Render FPS counter on top of everything
            RenderFPSCounter();

            SwapBuffers();
        }

        private Matrix4 CreateSVGProjection(float xOffset)
        {
            float windowWidth = Size.X;
            float windowHeight = Size.Y;
            float windowAspectRatio = windowWidth / windowHeight;

            float projectionWidth, projectionHeight;
            float offsetY = 0;

            // Calculate height to maintain aspect ratio
            projectionWidth = svgViewBox.width;
            projectionHeight = projectionWidth / windowAspectRatio;
            offsetY = (projectionHeight - svgViewBox.height) / 2;

            return Matrix4.CreateOrthographicOffCenter(
                xOffset, 
                xOffset + svgViewBox.width,
                svgViewBox.y + svgViewBox.height + offsetY, 
                svgViewBox.y - offsetY,
                -1, 1
            );
        }

        private void RenderSVGContent()
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            
            GL.UseProgram(shaderProgram);

            // Use the projection matrix that maintains aspect ratio
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "projection"), false, ref projectionMatrix);

            // Calculate base dimensions for grid layout
            float scale = 0.15f; // Smaller scale for more stamps
            float spacingX = svgViewBox.width * 0.16f;
            float spacingY = svgViewBox.height * 0.16f;
            int gridSize = 5; // 5x5 grid

            // Calculate total grid dimensions
            float totalWidth = spacingX * (gridSize - 1);
            float totalHeight = spacingY * (gridSize - 1);

            // Calculate starting position to center the grid
            float startX = -totalWidth / 2;
            float startY = -totalHeight / 2;

            // Calculate SVG center point
            Vector3 center = new Vector3(
                svgViewBox.x + svgViewBox.width / 2,
                svgViewBox.y + svgViewBox.height / 2,
                0
            );

            int modelViewLoc = GL.GetUniformLocation(shaderProgram, "modelView");

            // Render grid of SVGs
            for (int row = 0; row < gridSize; row++)
            {
                for (int col = 0; col < gridSize; col++)
                {
                    // Calculate position
                    float x = startX + col * spacingX;
                    float y = startY + row * spacingY;

                    // Calculate unique rotation speed based on position
                    // Map position to speed: top-left is slowest (0.2x), bottom-right is fastest (2.0x)
                    float rowFactor = row / (float)(gridSize - 1);    // 0.0 to 1.0
                    float colFactor = col / (float)(gridSize - 1);    // 0.0 to 1.0
                    float speedMultiplier = 0.2f + (rowFactor + colFactor) * 0.9f;  // 0.2 to 2.0
                    float localRotation = rotationAngle * speedMultiplier;

                    // Calculate pulse scale based on beat timing
                    float pulseAmount = 0.15f; // Maximum scale variation (15%)
                    float t = currentBeatTime / beatInterval;
                    float easedT = CubicBezierEase(t);
                    float beatPulse = 1.0f - (pulseAmount * (1.0f - easedT));
                    float localScale = scale * beatPulse;

                    // Create transformation matrix
                    Matrix4 transform = Matrix4.CreateTranslation(-center) *
                                     Matrix4.CreateScale(localScale) *
                                     Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(localRotation)) *
                                     Matrix4.CreateTranslation(center + new Vector3(x, y, 0));

                    GL.UniformMatrix4(modelViewLoc, false, ref transform);

                    // Draw the shapes using batched geometry
                    if (fillVertexCount > 0)
                    {
                        GL.BindVertexArray(fillVAO);
                        GL.DrawArrays(PrimitiveType.Triangles, 0, fillVertexCount);
                    }

                    if (strokeVertexCount > 0)
                    {
                        GL.BindVertexArray(strokeVAO);
                        GL.DrawArrays(PrimitiveType.Lines, 0, strokeVertexCount);
                    }
                }
            }

            // Optionally, render the SVG border for reference
            // RenderSVGBorder();

            GL.Disable(EnableCap.Blend);
        }

        private void RenderSVGBorder()
        {
            // Create a thin border around the SVG viewbox
            var borderColor = Color.DarkGray;
            float borderWidth = 0.5f;
            
            // Create vertices for the border lines
            float[] borderVertices = new float[] {
                // Top border
                svgViewBox.x, svgViewBox.y, 0.0f,                             // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                svgViewBox.x + svgViewBox.width, svgViewBox.y, 0.0f,         // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                
                // Right border
                svgViewBox.x + svgViewBox.width, svgViewBox.y, 0.0f,         // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                svgViewBox.x + svgViewBox.width, svgViewBox.y + svgViewBox.height, 0.0f, // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                
                // Bottom border
                svgViewBox.x + svgViewBox.width, svgViewBox.y + svgViewBox.height, 0.0f, // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                svgViewBox.x, svgViewBox.y + svgViewBox.height, 0.0f,        // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                
                // Left border
                svgViewBox.x, svgViewBox.y + svgViewBox.height, 0.0f,        // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f,  // Color
                svgViewBox.x, svgViewBox.y, 0.0f,                            // Position
                borderColor.R/255f, borderColor.G/255f, borderColor.B/255f, borderColor.A/255f   // Color
            };
            
            GL.LineWidth(borderWidth);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, borderVertices.Length * sizeof(float), borderVertices, BufferUsageHint.StreamDraw);
            GL.DrawArrays(PrimitiveType.Lines, 0, 8); // 4 lines = 8 vertices
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            
            // Resize the viewport and framebuffer
            GL.Viewport(0, 0, Size.X, Size.Y);
            SetupFramebuffer();
            
            // Update projection matrix when window is resized
            UpdateProjection();

            // Update the orthographic projection for text rendering
            orthoProjection = Matrix4.CreateOrthographicOffCenter(0, Size.X, Size.Y, 0, -1, 1);
        }
        
        protected override void OnUnload()
        {
            // Clean up resources
            GL.DeleteFramebuffer(framebuffer);
            GL.DeleteTexture(colorTexture);
            GL.DeleteProgram(shaderProgram);
            GL.DeleteProgram(fxaaShaderProgram);
            GL.DeleteVertexArray(vertexArrayObject);
            GL.DeleteBuffer(vertexBufferObject);
            GL.DeleteVertexArray(screenQuadVAO);
            
            // Clean up batched geometry resources
            GL.DeleteVertexArray(fillVAO);
            GL.DeleteBuffer(fillVBO);
            GL.DeleteVertexArray(strokeVAO);
            GL.DeleteBuffer(strokeVBO);
            
            // Clean up the text renderers
            fpsTextRenderer?.Dispose();
            titleTextRenderer?.Dispose();
            GL.DeleteProgram(textShaderProgram);
            
            base.OnUnload();
        }

        private void InitializeBatchedGeometry()
        {
            Console.WriteLine("Pre-processing SVG shapes for batched rendering...");
            
            // Create batched geometry processor
            batchedGeometry.ProcessShapes(svgHandler.GetShapes());
            
            Console.WriteLine($"Processed {batchedGeometry.FillVertexCount} vertices for fill and {batchedGeometry.StrokeVertexCount} vertices for stroke");
            
            // Create and set up VBO/VAO for fill geometry
            fillVAO = GL.GenVertexArray();
            fillVBO = GL.GenBuffer();
            
            GL.BindVertexArray(fillVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, fillVBO);
            
            if (batchedGeometry.FillVertices.Count > 0)
            {
                // Upload fill vertex data to GPU
                GL.BufferData(
                    BufferTarget.ArrayBuffer,
                    batchedGeometry.FillVertices.Count * sizeof(float),
                    batchedGeometry.FillVertices.ToArray(),
                    BufferUsageHint.StaticDraw
                );
                
                fillVertexCount = batchedGeometry.FillVertexCount;
            
                // Position attribute (x, y, z)
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                
                // Color attribute (r, g, b, a)
                GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);
            }
            
            // Create and set up VBO/VAO for stroke geometry
            strokeVAO = GL.GenVertexArray();
            strokeVBO = GL.GenBuffer();
            
            GL.BindVertexArray(strokeVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, strokeVBO);
            
            if (batchedGeometry.StrokeVertices.Count > 0)
            {
                // Upload stroke vertex data to GPU
                GL.BufferData(
                    BufferTarget.ArrayBuffer,
                    batchedGeometry.StrokeVertices.Count * sizeof(float),
                    batchedGeometry.StrokeVertices.ToArray(),
                    BufferUsageHint.StaticDraw
                );
                
                strokeVertexCount = batchedGeometry.StrokeVertexCount;
            
                // Position attribute (x, y, z)
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                
                // Color attribute (r, g, b, a)
                GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, 7 * sizeof(float), 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);
            }
            
            // Unbind VAO
            GL.BindVertexArray(0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }
    }
} 