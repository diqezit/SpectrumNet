namespace SpectrumNet
{
    public class TextRenderer : IDisposable
    {
        private readonly Dictionary<char, CharacterInfo> _characters = new Dictionary<char, CharacterInfo>();
        private readonly int _fontSize = 24;
        private readonly int _textureId;
        private readonly int _shaderProgram;
        private readonly int _vertexBuffer;
        private readonly int _uvBuffer;
        private bool _isDisposed;

        // Store viewport dimensions
        private int _viewportWidth;
        private int _viewportHeight;

        private const string LogPrefix = "[TextRenderer] ";
        private const int TEXTURE_ATLAS_SIZE = 1024;

        // Character info structure to store glyph data
        private struct CharacterInfo
        {
            public float AdvanceX;
            public float AdvanceY;
            public float Width;
            public float Height;
            public float OffsetX;
            public float OffsetY;
            public float TexCoordX;
            public float TexCoordY;
        }

        public TextRenderer()
        {
            try
            {
                SmartLogger.Log(LogLevel.Debug, LogPrefix, "Initializing TextRenderer...", forceLog: true);

                // Generate texture for font atlas
                _textureId = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, _textureId);

                // Create an empty texture atlas
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                    TEXTURE_ATLAS_SIZE, TEXTURE_ATLAS_SIZE, 0,
                    OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

                // Set texture parameters
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

                // Create shader program for rendering text
                _shaderProgram = CreateTextShaderProgram();

                // Create buffers for vertex and UV coordinates
                _vertexBuffer = GL.GenBuffer();
                _uvBuffer = GL.GenBuffer();

                // Load a basic bitmap font or use a system font via another library
                LoadBitmapFont();

                // Initialize viewport dimensions
                int[] viewport = new int[4];
                GL.GetInteger(GetPName.Viewport, viewport);
                _viewportWidth = viewport[2];
                _viewportHeight = viewport[3];

                SmartLogger.Log(LogLevel.Information, LogPrefix, "TextRenderer initialized successfully", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Failed to initialize TextRenderer: {ex}", forceLog: true);
                Dispose();
                throw;
            }
        }

        private void LoadBitmapFont()
        {
            // This is a simplified replacement for the SharpFont implementation
            // Here you would load a bitmap font or use a different font rendering approach

            // For a basic implementation, we'll create a simple bitmap font with fixed-width characters
            // In a real application, you would load actual font data from a file

            int glyphWidth = 16;
            int glyphHeight = 16;
            int charsPerRow = 16;

            // Create a simple monospaced font layout
            for (char c = ' '; c <= '~'; c++)
            {
                int charIndex = c - ' ';
                int row = charIndex / charsPerRow;
                int col = charIndex % charsPerRow;

                float texX = (float)col * glyphWidth / TEXTURE_ATLAS_SIZE;
                float texY = (float)row * glyphHeight / TEXTURE_ATLAS_SIZE;

                _characters[c] = new CharacterInfo
                {
                    AdvanceX = glyphWidth,
                    AdvanceY = 0,
                    Width = glyphWidth,
                    Height = glyphHeight,
                    OffsetX = 0,
                    OffsetY = 0,
                    TexCoordX = texX,
                    TexCoordY = texY
                };
            }

            // In a real implementation, you would load actual font bitmap data here
            // and upload it to the texture atlas
        }

        private int CreateTextShaderProgram()
        {
            int program = GL.CreateProgram();

            // Vertex shader
            string vertexShaderSource = @"
                #version 120
                attribute vec2 position;
                attribute vec2 texCoord;
                varying vec2 v_texCoord;
                uniform mat4 projection;
                
                void main() {
                    gl_Position = projection * vec4(position, 0.0, 1.0);
                    v_texCoord = texCoord;
                }
            ";

            // Fragment shader
            string fragmentShaderSource = @"
                #version 120
                varying vec2 v_texCoord;
                uniform sampler2D texture;
                uniform vec4 textColor;
                
                void main() {
                    vec4 sampled = texture2D(texture, v_texCoord);
                    gl_FragColor = vec4(textColor.rgb, textColor.a * sampled.a);
                }
            ";

            int vertexShader = CompileShader(ShaderType.VertexShader, vertexShaderSource);
            int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentShaderSource);

            GL.AttachShader(program, vertexShader);
            GL.AttachShader(program, fragmentShader);
            GL.LinkProgram(program);

            // Check linking status
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetProgramInfoLog(program);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error linking shader program: {info}", forceLog: true);
                throw new Exception($"Failed to link shader program: {info}");
            }

            // Delete shaders as they're linked into the program now
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);

            return program;
        }

        private int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);

            // Check compilation status
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetShaderInfoLog(shader);
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error compiling {type} shader: {info}", forceLog: true);
                GL.DeleteShader(shader);
                throw new Exception($"Failed to compile {type} shader: {info}");
            }

            return shader;
        }

        public void RenderText(string text, float x, float y, Color4 color)
        {
            if (string.IsNullOrEmpty(text) || _isDisposed)
                return;

            try
            {
                // Update viewport dimensions
                int[] viewport = new int[4];
                GL.GetInteger(GetPName.Viewport, viewport);
                _viewportWidth = viewport[2];
                _viewportHeight = viewport[3];

                // Save current OpenGL state
                int previousShader = GL.GetInteger(GetPName.CurrentProgram);
                bool blendWasEnabled = GL.IsEnabled(EnableCap.Blend);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                GL.UseProgram(_shaderProgram);

                // Set uniforms
                int projectionLocation = GL.GetUniformLocation(_shaderProgram, "projection");
                int textureLocation = GL.GetUniformLocation(_shaderProgram, "texture");
                int colorLocation = GL.GetUniformLocation(_shaderProgram, "textColor");

                // Get projection matrix
                Matrix4 projection = Matrix4.CreateOrthographicOffCenter(0, _viewportWidth,
                                                                        _viewportHeight, 0, -1, 1);
                GL.UniformMatrix4(projectionLocation, false, ref projection);

                GL.Uniform1(textureLocation, 0);
                GL.Uniform4(colorLocation, color.R, color.G, color.B, color.A);

                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _textureId);

                float cursorX = x;
                float cursorY = y;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];

                    if (!_characters.TryGetValue(c, out CharacterInfo ch))
                    {
                        cursorX += _fontSize / 2; // Default spacing for unknown characters
                        continue;
                    }

                    float xpos = cursorX + ch.OffsetX;
                    float ypos = cursorY - (ch.Height - ch.OffsetY);
                    float w = ch.Width;
                    float h = ch.Height;

                    // Calculate vertices
                    float[] vertices = new float[] {
                        xpos,     ypos + h,
                        xpos,     ypos,
                        xpos + w, ypos,
                        xpos,     ypos + h,
                        xpos + w, ypos,
                        xpos + w, ypos + h
                    };

                    // Calculate texture coordinates
                    float[] texCoords = new float[] {
                        ch.TexCoordX,                    ch.TexCoordY + ch.Height / TEXTURE_ATLAS_SIZE,
                        ch.TexCoordX,                    ch.TexCoordY,
                        ch.TexCoordX + ch.Width / TEXTURE_ATLAS_SIZE, ch.TexCoordY,
                        ch.TexCoordX,                    ch.TexCoordY + ch.Height / TEXTURE_ATLAS_SIZE,
                        ch.TexCoordX + ch.Width / TEXTURE_ATLAS_SIZE, ch.TexCoordY,
                        ch.TexCoordX + ch.Width / TEXTURE_ATLAS_SIZE, ch.TexCoordY + ch.Height / TEXTURE_ATLAS_SIZE
                    };

                    // Upload vertex and texture coordinates
                    GL.BindBuffer(BufferTarget.ArrayBuffer, _vertexBuffer);
                    GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
                    GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 0, 0);
                    GL.EnableVertexAttribArray(0);

                    GL.BindBuffer(BufferTarget.ArrayBuffer, _uvBuffer);
                    GL.BufferData(BufferTarget.ArrayBuffer, texCoords.Length * sizeof(float), texCoords, BufferUsageHint.DynamicDraw);
                    GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 0, 0);
                    GL.EnableVertexAttribArray(1);

                    // Draw
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

                    // Advance cursor
                    cursorX += ch.AdvanceX;
                }

                // Restore state
                GL.DisableVertexAttribArray(0);
                GL.DisableVertexAttribArray(1);
                GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.UseProgram(previousShader);

                if (!blendWasEnabled)
                    GL.Disable(EnableCap.Blend);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error rendering text: {ex}", forceLog: true);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            SmartLogger.Log(LogLevel.Debug, LogPrefix, "Disposing TextRenderer...", forceLog: true);

            try
            {
                // Delete OpenGL resources
                if (_textureId != 0)
                    GL.DeleteTexture(_textureId);

                if (_shaderProgram != 0)
                    GL.DeleteProgram(_shaderProgram);

                if (_vertexBuffer != 0)
                    GL.DeleteBuffer(_vertexBuffer);

                if (_uvBuffer != 0)
                    GL.DeleteBuffer(_uvBuffer);

                _isDisposed = true;
                SmartLogger.Log(LogLevel.Information, LogPrefix, "TextRenderer disposed successfully", forceLog: true);
            }
            catch (Exception ex)
            {
                SmartLogger.Log(LogLevel.Error, LogPrefix, $"Error during TextRenderer disposal: {ex}", forceLog: true);
            }
        }
    }
}