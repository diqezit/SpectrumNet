namespace SpectrumNet
{
    public class SceneGeometry : IDisposable
    {
        #region Константы и настройки
        private record SceneColors
        {
            public static readonly Color4 Floor = new Color4(0.2f, 0.1f, 0.3f, 1.0f);
            public static readonly Color4 Ceiling = new Color4(0.1f, 0.1f, 0.2f, 1.0f);
            public static readonly Color4 Wall = new Color4(0.15f, 0.15f, 0.25f, 1.0f);
            public static readonly Color4 Grid = new Color4(0.4f, 0.4f, 0.9f, 0.5f);
            public static readonly Color4 WallGrid = new Color4(0.4f, 0.4f, 0.9f, 0.3f);
            public static readonly Color4 CeilingGrid = new Color4(0.4f, 0.4f, 0.9f, 0.2f);
            public static readonly Color4 Light = new Color4(1.0f, 0.95f, 0.8f, 1.0f);
        }

        private record SceneSettings
        {
            public const float GridHeight = 0.1f;
            public const float GridLineWidth = 1.0f;
            public const float InitialWidth = 1500f;
            public const float InitialHeight = 500f;
            public const float InitialDepth = 1500f;
            public const int GridSize = 20;
            public const float GridSpacing = 40f;
        }

        private record VertexFormats
        {
            public const int PositionSize = 3;
            public const int ColorSize = 3;
            public const int VertexSize = 6;
            public const int PositionOffset = 0;
            public const int ColorOffset = 3;
            public const int PositionLocation = 0;
            public const int ColorLocation = 2;
        }

        private record RenderingState
        {
            public const int TriangleCount = 12;
            public const int IndexCount = TriangleCount * 3;
        }
        #endregion

        #region Приватные поля
        private int _vao;
        private int _vbo;
        private int _ibo;
        private int _gridVao;
        private int _gridVbo;
        private int _gridIbo;
        private int _lightVao;
        private int _lightVbo;
        private int _lightIbo;
        private bool _isInitialized;
        private readonly float _width;
        private readonly float _height;
        private readonly float _depth;
        private Vector3 _lightPosition;
        private ShaderProgram? _sceneShader;
        private ShaderProgram? _lightShader;
        private ShaderProgram? _debugShader;
        #endregion

        public SceneGeometry()
        {
            _width = SceneSettings.InitialWidth;
            _height = SceneSettings.InitialHeight;
            _depth = SceneSettings.InitialDepth;
            _lightPosition = new Vector3(0, _height - 10, 0);
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            SmartLogger.Safe(() => {
                _vao = GL.GenVertexArray();
                _vbo = GL.GenBuffer();
                _ibo = GL.GenBuffer();

                _gridVao = GL.GenVertexArray();
                _gridVbo = GL.GenBuffer();
                _gridIbo = GL.GenBuffer();

                _lightVao = GL.GenVertexArray();
                _lightVbo = GL.GenBuffer();
                _lightIbo = GL.GenBuffer();

                InitializeShaders();
                InitializeGeometry();
                InitializeGrid();
                InitializeLight();

                _isInitialized = true;
                SmartLogger.Log(LogLevel.Debug, "SceneGeometry", "Scene geometry initialized");
            }, "SceneGeometry", "Failed to initialize scene geometry");
        }

        private void InitializeShaders()
        {
            _sceneShader = SmartLogger.Safe(() => new ShaderProgram(Shaders.vertexSceneShader, Shaders.fragmentSceneShader),
                defaultValue: null);

            _lightShader = SmartLogger.Safe(() => new ShaderProgram(Shaders.vertexShader, Shaders.glowFragmentShader),
                defaultValue: null);

            if (_sceneShader != null && _lightShader != null)
                SmartLogger.Log(LogLevel.Debug, "SceneGeometry", "Shaders initialized");
        }

        private void InitializeGeometry()
        {
            SmartLogger.Safe(() => {
                GL.BindVertexArray(_vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

                float[] vertices = GenerateVertices();
                int[] indices = GenerateIndices();

                GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ibo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);

                GL.EnableVertexAttribArray(VertexFormats.PositionLocation);
                GL.VertexAttribPointer(VertexFormats.PositionLocation,
                                      VertexFormats.PositionSize,
                                      VertexAttribPointerType.Float,
                                      false,
                                      VertexFormats.VertexSize * sizeof(float),
                                      VertexFormats.PositionOffset * sizeof(float));

                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false,
                                      VertexFormats.VertexSize * sizeof(float),
                                      VertexFormats.PositionOffset * sizeof(float));

                GL.EnableVertexAttribArray(VertexFormats.ColorLocation);
                GL.VertexAttribPointer(VertexFormats.ColorLocation,
                                      VertexFormats.ColorSize,
                                      VertexAttribPointerType.Float,
                                      false,
                                      VertexFormats.VertexSize * sizeof(float),
                                      VertexFormats.ColorOffset * sizeof(float));
                GL.BindVertexArray(0);

                SmartLogger.Log(LogLevel.Debug, "SceneGeometry", "Main geometry initialized");
            }, "SceneGeometry", "Failed to initialize main geometry");
        }

        private void InitializeGrid()
        {
            SmartLogger.Safe(() => {
                GL.BindVertexArray(_gridVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _gridVbo);

                float[] gridVertices = GenerateGridVertices();
                int[] gridIndices = GenerateGridIndices();

                GL.BufferData(BufferTarget.ArrayBuffer, gridVertices.Length * sizeof(float), gridVertices, BufferUsageHint.StaticDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _gridIbo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, gridIndices.Length * sizeof(int), gridIndices, BufferUsageHint.StaticDraw);

                GL.EnableVertexAttribArray(VertexFormats.PositionLocation);
                GL.VertexAttribPointer(VertexFormats.PositionLocation,
                                      VertexFormats.PositionSize,
                                      VertexAttribPointerType.Float,
                                      false,
                                      VertexFormats.VertexSize * sizeof(float),
                                      VertexFormats.PositionOffset * sizeof(float));

                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false,
                                      VertexFormats.VertexSize * sizeof(float),
                                      VertexFormats.PositionOffset * sizeof(float));

                GL.EnableVertexAttribArray(VertexFormats.ColorLocation);
                GL.VertexAttribPointer(VertexFormats.ColorLocation,
                                      VertexFormats.ColorSize,
                                      VertexAttribPointerType.Float,
                                      false,
                                      VertexFormats.VertexSize * sizeof(float),
                                      VertexFormats.ColorOffset * sizeof(float));

                GL.BindVertexArray(0);

                SmartLogger.Log(LogLevel.Debug, "SceneGeometry", $"Grid initialized with {gridVertices.Length / VertexFormats.VertexSize} vertices and {gridIndices.Length} indices");
            }, "SceneGeometry", "Failed to initialize grid");
        }

        private void InitializeLight()
        {
            SmartLogger.Safe(() => {
                GL.BindVertexArray(_lightVao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, _lightVbo);

                float size = 20.0f;
                float[] lightVertices = {
                    -size, -size/4,  size, size, -size/4,  size, size,  size/4,  size, -size,  size/4,  size,
                    -size, -size/4, -size, size, -size/4, -size, size,  size/4, -size, -size,  size/4, -size,
                    -size,  size/4, -size, size,  size/4, -size, size,  size/4,  size, -size,  size/4,  size,
                    -size, -size/4, -size, size, -size/4, -size, size, -size/4,  size, -size, -size/4,  size,
                    size, -size/4, -size, size, -size/4,  size, size,  size/4,  size, size,  size/4, -size,
                    -size, -size/4, -size, -size, -size/4,  size, -size,  size/4,  size, -size,  size/4, -size
                };

                int[] indices = {
                    0, 1, 2, 2, 3, 0,
                    4, 5, 6, 6, 7, 4,
                    8, 9, 10, 10, 11, 8,
                    12, 13, 14, 14, 15, 12,
                    16, 17, 18, 18, 19, 16,
                    20, 21, 22, 22, 23, 20
                };

                GL.BufferData(BufferTarget.ArrayBuffer, lightVertices.Length * sizeof(float), lightVertices, BufferUsageHint.StaticDraw);

                GL.BindBuffer(BufferTarget.ElementArrayBuffer, _lightIbo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);

                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

                GL.BindVertexArray(0);

                SmartLogger.Log(LogLevel.Debug, "SceneGeometry", "Light source initialized");
            }, "SceneGeometry", "Failed to initialize light source");
        }

        private float[] GenerateVertices()
        {
            float w = _width / 2;
            float h = _height;
            float d = _depth / 2;
            Vector3 lightPos = _lightPosition;

            Func<Vector3, float> calculateBrightness = (pos) => {
                float distance = Vector3.Distance(pos, lightPos);
                float attenuation = 1.0f / (1.0f + 0.0001f * distance * distance);
                return 0.7f + 0.3f * Math.Min(1.0f, attenuation * 5.0f);
            };

            Func<Color4, Vector3, Color4> applyBrightness = (color, pos) => {
                float brightness = calculateBrightness(pos);
                return new Color4(
                    color.R * brightness,
                    color.G * brightness,
                    color.B * brightness,
                    color.A
                );
            };

            Vector3[] positions = {
                new Vector3(-w, 0, -d), new Vector3(w, 0, -d), new Vector3(w, 0, d), new Vector3(-w, 0, d),
                new Vector3(-w, h, -d), new Vector3(w, h, -d), new Vector3(w, h, d), new Vector3(-w, h, d),
                new Vector3(-w, 0, -d), new Vector3(w, 0, -d), new Vector3(w, h, -d), new Vector3(-w, h, -d),
                new Vector3(-w, 0, d), new Vector3(w, 0, d), new Vector3(w, h, d), new Vector3(-w, h, d),
                new Vector3(w, 0, -d), new Vector3(w, 0, d), new Vector3(w, h, d), new Vector3(w, h, -d),
                new Vector3(-w, 0, -d), new Vector3(-w, 0, d), new Vector3(-w, h, d), new Vector3(-w, h, -d)
            };

            Color4[] colors = {
                applyBrightness(SceneColors.Floor, positions[0]), applyBrightness(SceneColors.Floor, positions[1]),
                applyBrightness(SceneColors.Floor, positions[2]), applyBrightness(SceneColors.Floor, positions[3]),
                applyBrightness(SceneColors.Ceiling, positions[4]), applyBrightness(SceneColors.Ceiling, positions[5]),
                applyBrightness(SceneColors.Ceiling, positions[6]), applyBrightness(SceneColors.Ceiling, positions[7]),
                applyBrightness(SceneColors.Wall, positions[8]), applyBrightness(SceneColors.Wall, positions[9]),
                applyBrightness(SceneColors.Wall, positions[10]), applyBrightness(SceneColors.Wall, positions[11]),
                applyBrightness(SceneColors.Wall, positions[12]), applyBrightness(SceneColors.Wall, positions[13]),
                applyBrightness(SceneColors.Wall, positions[14]), applyBrightness(SceneColors.Wall, positions[15]),
                applyBrightness(SceneColors.Wall, positions[16]), applyBrightness(SceneColors.Wall, positions[17]),
                applyBrightness(SceneColors.Wall, positions[18]), applyBrightness(SceneColors.Wall, positions[19]),
                applyBrightness(SceneColors.Wall, positions[20]), applyBrightness(SceneColors.Wall, positions[21]),
                applyBrightness(SceneColors.Wall, positions[22]), applyBrightness(SceneColors.Wall, positions[23])
            };

            float[] vertices = new float[24 * 6];

            for (int i = 0; i < 24; i++)
            {
                int offset = i * 6;
                vertices[offset + 0] = positions[i].X;
                vertices[offset + 1] = positions[i].Y;
                vertices[offset + 2] = positions[i].Z;
                vertices[offset + 3] = colors[i].R;
                vertices[offset + 4] = colors[i].G;
                vertices[offset + 5] = colors[i].B;
            }

            return vertices;
        }

        private int[] GenerateIndices()
        {
            return new int[] {
        0, 1, 2, 0, 2, 3,
        4, 5, 6, 4, 6, 7,
        8, 9, 10, 8, 10, 11,
        12, 13, 14, 12, 14, 15,
        16, 17, 18, 16, 18, 19,
        20, 21, 22, 20, 22, 23
    };
        }

        private class GridGenerator
        {
            private readonly float _width;
            private readonly float _height;
            private readonly float _depth;
            private readonly List<float> _vertices = new List<float>();

            public GridGenerator(float width, float height, float depth)
            {
                _width = width;
                _height = height;
                _depth = depth;
            }

            private void AddGridLine(float x1, float y1, float z1, float x2, float y2, float z2, float r, float g, float b)
            {
                _vertices.Add(x1); _vertices.Add(y1); _vertices.Add(z1);
                _vertices.Add(r); _vertices.Add(g); _vertices.Add(b);

                _vertices.Add(x2); _vertices.Add(y2); _vertices.Add(z2);
                _vertices.Add(r); _vertices.Add(g); _vertices.Add(b);
            }

            public void AddFloorGrid()
            {
                float w = _width / 2;
                float d = _depth / 2;
                float r = SceneColors.Grid.R;
                float g = SceneColors.Grid.G;
                float b = SceneColors.Grid.B;

                int halfSize = SceneSettings.GridSize / 2;
                float spacing = SceneSettings.GridSpacing;
                float height = SceneSettings.GridHeight;

                for (int i = -halfSize; i <= halfSize; i++)
                {
                    float x = i * spacing;
                    AddGridLine(x, height, -d, x, height, d, r, g, b);
                }

                for (int i = -halfSize; i <= halfSize; i++)
                {
                    float z = i * spacing;
                    AddGridLine(-w, height, z, w, height, z, r, g, b);
                }
            }

            public void AddCeilingGrid()
            {
                float w = _width / 2;
                float h = _height;
                float d = _depth / 2;
                float r = SceneColors.CeilingGrid.R;
                float g = SceneColors.CeilingGrid.G;
                float b = SceneColors.CeilingGrid.B;

                int ceilingSteps = 5;
                float ceilingSpacing = (_width) / ceilingSteps;

                for (int i = 0; i <= ceilingSteps; i++)
                {
                    float x = -w + i * ceilingSpacing;
                    AddGridLine(x, h, -d, x, h, d, r, g, b);
                }

                for (int i = 0; i <= ceilingSteps; i++)
                {
                    float z = -d + i * ((_depth) / ceilingSteps);
                    AddGridLine(-w, h, z, w, h, z, r, g, b);
                }
            }

            public void AddWallGrid()
            {
                float w = _width / 2;
                float h = _height;
                float d = _depth / 2;
                float r = SceneColors.WallGrid.R;
                float g = SceneColors.WallGrid.G;
                float b = SceneColors.WallGrid.B;

                int wallSteps = 4;
                float wallVerticalSpacing = h / wallSteps;
                float wallHorizontalSpacing = _width / wallSteps;

                AddWallGridForFace(-w, w, 0, h, -d, -d, r, g, b, wallSteps);  // Задняя стена
                AddWallGridForFace(-w, w, 0, h, d, d, r, g, b, wallSteps);    // Передняя стена
                AddWallGridForFace(-w, -w, 0, h, -d, d, r, g, b, wallSteps);  // Левая стена
                AddWallGridForFace(w, w, 0, h, -d, d, r, g, b, wallSteps);    // Правая стена
            }

            private void AddWallGridForFace(float x1, float x2, float y1, float y2, float z1, float z2, float r, float g, float b, int steps)
            {
                float wallVerticalSpacing = (y2 - y1) / steps;

                // Горизонтальные линии
                for (int i = 0; i <= steps; i++)
                {
                    float y = y1 + i * wallVerticalSpacing;
                    AddGridLine(x1, y, z1, x2, y, z2, r, g, b);
                }

                // Вертикальные линии
                if (z1 == z2)
                {  // Передняя или задняя стена
                    float wallHorizontalSpacing = (x2 - x1) / steps;
                    for (int i = 0; i <= steps; i++)
                    {
                        float x = x1 + i * wallHorizontalSpacing;
                        AddGridLine(x, y1, z1, x, y2, z1, r, g, b);
                    }
                }
                else
                {  // Боковая стена
                    float wallDepthSpacing = (z2 - z1) / steps;
                    for (int i = 0; i <= steps; i++)
                    {
                        float z = z1 + i * wallDepthSpacing;
                        AddGridLine(x1, y1, z, x1, y2, z, r, g, b);
                    }
                }
            }

            public float[] GetVertices()
            {
                return _vertices.ToArray();
            }

            public int[] GetIndices()
            {
                int lineCount = _vertices.Count / (VertexFormats.VertexSize * 2);
                int[] indices = new int[lineCount * 2];

                for (int i = 0; i < lineCount; i++)
                {
                    indices[i * 2] = i * 2;
                    indices[i * 2 + 1] = i * 2 + 1;
                }

                return indices;
            }
        }

        private float[] GenerateGridVertices()
        {
            GridGenerator generator = new GridGenerator(_width, _height, _depth);
            generator.AddFloorGrid();
            generator.AddCeilingGrid();
            generator.AddWallGrid();
            return generator.GetVertices();
        }

        private int[] GenerateGridIndices()
        {
            int floorLines = 2 * (SceneSettings.GridSize + 1);
            int ceilingSteps = 5;
            int wallSteps = 4;
            int ceilingLines = 2 * (ceilingSteps + 1);
            int wallLines = 4 * (wallSteps + 1) * 2;

            int totalLines = floorLines + ceilingLines + wallLines;
            int[] indices = new int[totalLines * 2];

            for (int i = 0; i < totalLines; i++)
            {
                indices[i * 2] = i * 2;
                indices[i * 2 + 1] = i * 2 + 1;
            }

            return indices;
        }

        public void UpdateSceneSize(Vector3 cameraPosition, float fov, float aspectRatio)
        {
        }

        public void Render(ShaderProgram externalShader, Matrix4 projection, Matrix4 modelView, Vector3 externalLightPos, Vector3 viewPos)
        {
            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Warning, "SceneGeometry", "Cannot render: not initialized");
                return;
            }

            if (_sceneShader == null || _lightShader == null)
            {
                SmartLogger.Log(LogLevel.Warning, "SceneGeometry", "Cannot render: shaders not initialized");
                return;
            }

            bool depthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
            bool blendEnabled = GL.IsEnabled(EnableCap.Blend);

            SmartLogger.Safe(() => {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Less);

                RenderMainGeometry(projection, modelView);
                RenderGrid();
                RenderLightSource(projection, modelView);

            }, "SceneGeometry", "Error during rendering");

            RestoreGLState(depthTestEnabled, blendEnabled);
        }

        private void RenderMainGeometry(Matrix4 projection, Matrix4 modelView)
        {
            _sceneShader!.Use();
            _sceneShader.SetUniform("projection", projection);
            _sceneShader.SetUniform("modelview", modelView);

            GL.BindVertexArray(_vao);
            GL.DrawElements(PrimitiveType.Triangles, RenderingState.IndexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        private void RenderGrid()
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.LineWidth(SceneSettings.GridLineWidth);

            GL.BindVertexArray(_gridVao);

            int floorLines = 2 * (SceneSettings.GridSize + 1);
            int ceilingSteps = 5;
            int wallSteps = 4;
            int ceilingLines = 2 * (ceilingSteps + 1);
            int wallLines = 4 * (wallSteps + 1) * 2;
            int totalIndices = (floorLines + ceilingLines + wallLines) * 2;

            GL.DrawElements(PrimitiveType.Lines, totalIndices, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        private void RenderLightSource(Matrix4 projection, Matrix4 modelView)
        {
            _lightShader!.Use();
            _lightShader.SetUniform("projection", projection);
            _lightShader.SetUniform("modelview", Matrix4.CreateTranslation(_lightPosition) * modelView);
            _lightShader.SetUniform("time", (float)DateTime.Now.TimeOfDay.TotalSeconds);
            _lightShader.SetUniform("intensity", 0.7f);
            _lightShader.SetUniform("pulseRate", 1.5f);
            _lightShader.SetUniform("useTexture", 0.0f);
            _lightShader.Color = SceneColors.Light;

            GL.BindVertexArray(_lightVao);
            GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        private void RestoreGLState(bool depthTestEnabled, bool blendEnabled)
        {
            if (!depthTestEnabled)
                GL.Disable(EnableCap.DepthTest);

            if (!blendEnabled)
                GL.Disable(EnableCap.Blend);

            GL.LineWidth(1.0f);
        }

        public void RenderWith3DLighting(ShaderProgram externalShader, Matrix4 projection, Matrix4 modelView, Vector3 externalLightPos, Vector3 viewPos)
        {
            if (!_isInitialized)
            {
                SmartLogger.Log(LogLevel.Warning, "SceneGeometry", "Cannot render: not initialized");
                return;
            }

            if (_sceneShader == null || _lightShader == null)
            {
                SmartLogger.Log(LogLevel.Warning, "SceneGeometry", "Cannot render: shaders not initialized");
                return;
            }

            bool depthTestEnabled = GL.IsEnabled(EnableCap.DepthTest);
            bool blendEnabled = GL.IsEnabled(EnableCap.Blend);

            SmartLogger.Safe(() => {
                GL.Enable(EnableCap.DepthTest);
                GL.DepthFunc(DepthFunction.Less);

                RenderMainGeometryWith3DLighting(projection, modelView, viewPos);
                RenderGrid();
                RenderLightSource(projection, modelView);

            }, "SceneGeometry", "Error during 3D lighting rendering");

            RestoreGLState(depthTestEnabled, blendEnabled);
        }

        private void RenderMainGeometryWith3DLighting(Matrix4 projection, Matrix4 modelView, Vector3 viewPos)
        {
            _sceneShader!.Use();
            _sceneShader.SetUniform("projection", projection);
            _sceneShader.SetUniform("modelview", modelView);
            _sceneShader.SetUniform("model", Matrix4.Identity);
            _sceneShader.SetUniform("viewPos", viewPos);

            _sceneShader.SetUniform("material.ambient", new Vector3(0.5f, 0.5f, 0.5f));
            _sceneShader.SetUniform("material.diffuse", new Vector3(1.0f, 1.0f, 1.0f));
            _sceneShader.SetUniform("material.specular", new Vector3(0.7f, 0.7f, 0.7f));
            _sceneShader.SetUniform("material.shininess", 16.0f);
            _sceneShader.SetUniform("material.opacity", 1.0f);

            _sceneShader.SetUniform("numLights", 1.0f);
            _sceneShader.SetUniform("lights[0].position", _lightPosition);
            _sceneShader.SetUniform("lights[0].color", new Vector3(SceneColors.Light.R, SceneColors.Light.G, SceneColors.Light.B));
            _sceneShader.SetUniform("lights[0].intensity", 2.0f);
            _sceneShader.SetUniform("lights[0].attenuation", 0.0005f);

            _sceneShader.SetUniform("useTexture", 0.0f);
            _sceneShader.SetUniform("receiveShadows", 0.0f);
            _sceneShader.Color = new Color4(1.0f, 1.0f, 1.0f, 1.0f);

            GL.BindVertexArray(_vao);
            GL.DrawElements(PrimitiveType.Triangles, RenderingState.IndexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);
        }

        public void SetLightPosition(Vector3 position)
        {
            _lightPosition = position;
        }

        public void Dispose()
        {
            if (!_isInitialized) return;

            if (_sceneShader != null)
                SmartLogger.SafeDispose(_sceneShader, "SceneShader");

            if (_lightShader != null)
                SmartLogger.SafeDispose(_lightShader, "LightShader");

            if (_debugShader != null)
                SmartLogger.SafeDispose(_debugShader, "DebugShader");

            SmartLogger.Safe(() => {
                DeleteBuffers();
                _isInitialized = false;
                SmartLogger.Log(LogLevel.Debug, "SceneGeometry", "Scene geometry resources disposed");
            }, "SceneGeometry", "Error during resource disposal");
        }

        private void DeleteBuffers()
        {
            if (_vao != 0)
            {
                GL.DeleteVertexArray(_vao);
                _vao = 0;
            }
            if (_vbo != 0)
            {
                GL.DeleteBuffer(_vbo);
                _vbo = 0;
            }
            if (_ibo != 0)
            {
                GL.DeleteBuffer(_ibo);
                _ibo = 0;
            }

            if (_gridVao != 0)
            {
                GL.DeleteVertexArray(_gridVao);
                _gridVao = 0;
            }
            if (_gridVbo != 0)
            {
                GL.DeleteBuffer(_gridVbo);
                _gridVbo = 0;
            }
            if (_gridIbo != 0)
            {
                GL.DeleteBuffer(_gridIbo);
                _gridIbo = 0;
            }

            if (_lightVao != 0)
            {
                GL.DeleteVertexArray(_lightVao);
                _lightVao = 0;
            }
            if (_lightVbo != 0)
            {
                GL.DeleteBuffer(_lightVbo);
                _lightVbo = 0;
            }
            if (_lightIbo != 0)
            {
                GL.DeleteBuffer(_lightIbo);
                _lightIbo = 0;
            }
        }
    }
}