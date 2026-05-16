using Silk.NET.OpenGL;
using SkiaSharp;
using System.Numerics;

namespace DeckMark.Viewer;

internal sealed class SlideTransitionRenderer : IDisposable
{
    private const float FieldOfViewY = MathF.PI / 4f;
    private const float NearPlane = 64f;
    private const float FarPlane = 8192f;

    private static readonly Vector2[] LocalCorners =
    [
        new(-SlideRenderer.SlideWidth / 2f, SlideRenderer.SlideHeight / 2f),
        new(SlideRenderer.SlideWidth / 2f, SlideRenderer.SlideHeight / 2f),
        new(SlideRenderer.SlideWidth / 2f, -SlideRenderer.SlideHeight / 2f),
        new(-SlideRenderer.SlideWidth / 2f, -SlideRenderer.SlideHeight / 2f),
    ];

    private static readonly Vector2[] TextureCorners =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private readonly GL _gl;
    private readonly Action<SKCanvas, int> _renderSlide;
    private readonly Dictionary<int, uint> _slideTextures = [];
    private readonly uint _program;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly int _opacityLocation;
    private readonly int _shadeLocation;
    private bool _disposed;

    public SlideTransitionRenderer(GL gl, Action<SKCanvas, int> renderSlide)
    {
        _gl = gl;
        _renderSlide = renderSlide;

        _program = CreateProgram();
        _opacityLocation = _gl.GetUniformLocation(_program, "uOpacity");
        _shadeLocation = _gl.GetUniformLocation(_program, "uShade");

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, _ebo);

        unsafe
        {
            _gl.BufferData(GLEnum.ArrayBuffer, (nuint)(sizeof(float) * 5 * 4), null, GLEnum.DynamicDraw);

            uint[] indices = [0, 1, 2, 2, 3, 0];
            fixed (uint* indexPtr = indices)
            {
                _gl.BufferData(GLEnum.ElementArrayBuffer, (nuint)(sizeof(uint) * indices.Length), indexPtr, GLEnum.StaticDraw);
            }
        }

        const uint stride = sizeof(float) * 5;
        _gl.EnableVertexAttribArray(0);
        unsafe
        {
            _gl.VertexAttribPointer(0, 3, GLEnum.Float, false, stride, (void*)0);
            _gl.EnableVertexAttribArray(1);
            _gl.VertexAttribPointer(1, 2, GLEnum.Float, false, stride, (void*)(sizeof(float) * 3));
        }

        _gl.BindVertexArray(0);
        _gl.BindBuffer(GLEnum.ArrayBuffer, 0);
        _gl.BindBuffer(GLEnum.ElementArrayBuffer, 0);
    }

    public void InvalidateTextures()
    {
        foreach (var texture in _slideTextures.Values)
            _gl.DeleteTexture(texture);

        _slideTextures.Clear();
    }

    public void RenderSlide(int viewportWidth, int viewportHeight, int slideIndex, float zoom, SKPoint pan, bool fillMode)
    {
        var context = CreateProjectionContext(viewportWidth, viewportHeight, zoom, pan, fillMode);
        PrepareFrame(viewportWidth, viewportHeight);

        DrawVisual(context, new SlideVisual(slideIndex, 1f, Vector3.Zero, Vector3.Zero, Vector3.One));
        CompleteFrame();
    }

    public void RenderTransition(int viewportWidth, int viewportHeight, SlideTransitionKind transitionKind, int fromSlideIndex, int toSlideIndex, float progress, float zoom, SKPoint pan, bool fillMode)
    {
        var context = CreateProjectionContext(viewportWidth, viewportHeight, zoom, pan, fillMode);
        PrepareFrame(viewportWidth, viewportHeight);

        var visuals = BuildTransitionVisuals(transitionKind, fromSlideIndex, toSlideIndex, progress);
        foreach (var visual in visuals.OrderBy(v => v.DepthSort))
            DrawVisual(context, visual);

        CompleteFrame();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        InvalidateTextures();
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
        _disposed = true;
    }

    private void PrepareFrame(int viewportWidth, int viewportHeight)
    {
        _gl.Viewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
        _gl.Disable(GLEnum.CullFace);
        _gl.Disable(GLEnum.DepthTest);
        _gl.Enable(GLEnum.Blend);
        _gl.BlendFunc(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha);
        _gl.ClearColor(0.07f, 0.07f, 0.12f, 1f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        _gl.UseProgram(_program);
        _gl.ActiveTexture(GLEnum.Texture0);
        _gl.BindVertexArray(_vao);
    }

    private void CompleteFrame()
    {
        _gl.BindVertexArray(0);
        _gl.BindTexture(GLEnum.Texture2D, 0);
        _gl.UseProgram(0);
    }

    private void DrawVisual(ProjectionContext context, SlideVisual visual)
    {
        if (visual.Opacity <= 0.001f)
            return;

        uint texture = GetOrCreateTexture(visual.SlideIndex);
        if (texture == 0)
            return;

        Span<float> vertices = stackalloc float[20];
        float shade = ComputeShade(visual.Rotation);

        for (int i = 0; i < 4; i++)
        {
            var projected = ProjectVertex(LocalCorners[i], visual, context);
            int offset = i * 5;
            vertices[offset] = projected.X;
            vertices[offset + 1] = projected.Y;
            vertices[offset + 2] = projected.Z;
            vertices[offset + 3] = TextureCorners[i].X;
            vertices[offset + 4] = TextureCorners[i].Y;
        }

        _gl.BindTexture(GLEnum.Texture2D, texture);
        _gl.Uniform1(_opacityLocation, visual.Opacity);
        _gl.Uniform1(_shadeLocation, shade);

        unsafe
        {
            fixed (float* vertexPtr = vertices)
            {
                _gl.BindBuffer(GLEnum.ArrayBuffer, _vbo);
                _gl.BufferSubData(GLEnum.ArrayBuffer, 0, (nuint)(sizeof(float) * vertices.Length), vertexPtr);
            }
        }

        unsafe
        {
            _gl.DrawElements(GLEnum.Triangles, 6, GLEnum.UnsignedInt, null);
        }
    }

    private uint GetOrCreateTexture(int slideIndex)
    {
        if (_slideTextures.TryGetValue(slideIndex, out uint existingTexture))
            return existingTexture;

        var info = new SKImageInfo((int)SlideRenderer.SlideWidth, (int)SlideRenderer.SlideHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
            return 0;

        surface.Canvas.Clear(SKColors.Transparent);
        _renderSlide(surface.Canvas, slideIndex);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        if (!bitmap.ReadyToDraw)
            return 0;

        uint texture = _gl.GenTexture();
        _gl.BindTexture(GLEnum.Texture2D, texture);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

        unsafe
        {
            fixed (byte* pixelPtr = bitmap.GetPixelSpan())
            {
                _gl.TexImage2D(GLEnum.Texture2D, 0, (int)GLEnum.Rgba8, (uint)bitmap.Width, (uint)bitmap.Height, 0, GLEnum.Rgba, GLEnum.UnsignedByte, pixelPtr);
            }
        }

        _slideTextures[slideIndex] = texture;
        return texture;
    }

    private static ProjectionContext CreateProjectionContext(int viewportWidth, int viewportHeight, float zoom, SKPoint pan, bool fillMode)
    {
        float aspect = viewportHeight == 0 ? 1f : viewportWidth / (float)viewportHeight;
        float tanHalfY = MathF.Tan(FieldOfViewY / 2f);
        float tanHalfX = tanHalfY * aspect;
        float distanceForHeight = (SlideRenderer.SlideHeight / 2f) / tanHalfY;
        float distanceForWidth = (SlideRenderer.SlideWidth / 2f) / tanHalfX;
        float cameraDistance = fillMode
            ? MathF.Min(distanceForHeight, distanceForWidth)
            : MathF.Max(distanceForHeight, distanceForWidth);

        return new ProjectionContext(aspect, tanHalfX, tanHalfY, cameraDistance, MathF.Max(zoom, 0.01f), new Vector2(pan.X, -pan.Y));
    }

    private static Vector3 ProjectVertex(Vector2 localPoint, SlideVisual visual, ProjectionContext context)
    {
        var point = new Vector3(localPoint.X * visual.Scale.X, localPoint.Y * visual.Scale.Y, 0f);
        point = RotateX(point, visual.Rotation.X);
        point = RotateY(point, visual.Rotation.Y);
        point = RotateZ(point, visual.Rotation.Z);
        point += visual.Translation;
        point = new Vector3(
            (point.X * context.Zoom) + context.Pan.X,
            (point.Y * context.Zoom) + context.Pan.Y,
            point.Z * context.Zoom);

        float cameraZ = MathF.Max(NearPlane, context.CameraDistance - point.Z);
        float clipX = point.X / (cameraZ * context.TanHalfX);
        float clipY = point.Y / (cameraZ * context.TanHalfY);
        float depth = Math.Clamp((cameraZ - NearPlane) / (FarPlane - NearPlane), 0f, 1f);
        float clipZ = (depth * 2f) - 1f;

        return new Vector3(clipX, clipY, clipZ);
    }

    private static float ComputeShade(Vector3 rotation)
    {
        var normal = RotateX(Vector3.UnitZ, rotation.X);
        normal = RotateY(normal, rotation.Y);
        normal = RotateZ(normal, rotation.Z);
        return Math.Clamp(0.45f + (MathF.Max(0f, normal.Z) * 0.55f), 0.25f, 1f);
    }

    private static IReadOnlyList<SlideVisual> BuildTransitionVisuals(SlideTransitionKind transitionKind, int fromSlideIndex, int toSlideIndex, float progress)
    {
        float t = EaseInOut(Math.Clamp(progress, 0f, 1f));
        float inverse = 1f - t;

        return transitionKind switch
        {
            SlideTransitionKind.Fade =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -120f * t), Vector3.Zero, Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 180f * inverse), Vector3.Zero, Vector3.One * Lerp(0.96f, 1f, t)),
            ],
            SlideTransitionKind.Dissolve =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-120f * t, 0f, -180f * t), new Vector3(0f, DegreesToRadians(-8f * t), 0f), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(120f * inverse, 0f, 220f * inverse), new Vector3(0f, DegreesToRadians(12f * inverse), 0f), Vector3.One),
            ],
            SlideTransitionKind.Newsflash =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -260f * t), new Vector3(0f, 0f, DegreesToRadians(22f * t)), Vector3.One * Lerp(1f, 0.9f, t)),
                new SlideVisual(toSlideIndex, 0.45f + (0.55f * t), new Vector3(0f, 0f, 460f * inverse), new Vector3(0f, 0f, DegreesToRadians(-18f * inverse)), Vector3.One * Lerp(0.72f, 1f, t)),
            ],
            SlideTransitionKind.Cover =>
            [
                new SlideVisual(fromSlideIndex, 1f, new Vector3(0f, 0f, -90f * t), Vector3.Zero, Vector3.One),
                new SlideVisual(toSlideIndex, 1f, new Vector3(860f * inverse, 0f, 220f * inverse), new Vector3(0f, DegreesToRadians(-34f * inverse), 0f), Vector3.One),
            ],
            SlideTransitionKind.Pull =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-940f * t, 0f, 260f * t), new Vector3(0f, DegreesToRadians(28f * t), 0f), Vector3.One),
                new SlideVisual(toSlideIndex, 1f, new Vector3(0f, 0f, -80f * inverse), Vector3.Zero, Vector3.One),
            ],
            SlideTransitionKind.Push =>
            [
                new SlideVisual(fromSlideIndex, 1f, new Vector3(-900f * t, 0f, 140f * t), new Vector3(0f, DegreesToRadians(18f * t), 0f), Vector3.One),
                new SlideVisual(toSlideIndex, 1f, new Vector3(900f * inverse, 0f, 180f * inverse), new Vector3(0f, DegreesToRadians(-18f * inverse), 0f), Vector3.One),
            ],
            SlideTransitionKind.Wipe =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-80f * t, 0f, -160f * t), new Vector3(0f, DegreesToRadians(10f * t), 0f), Vector3.One),
                new SlideVisual(toSlideIndex, 1f, new Vector3(620f * inverse, 0f, 180f * inverse), new Vector3(0f, DegreesToRadians(-62f * inverse), 0f), Vector3.One),
            ],
            SlideTransitionKind.Blinds =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, -60f * t, -220f * t), new Vector3(DegreesToRadians(28f * t), 0f, 0f), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 70f * inverse, 240f * inverse), new Vector3(DegreesToRadians(-42f * inverse), 0f, 0f), Vector3.One),
            ],
            SlideTransitionKind.Checker =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-140f * t, -80f * t, -180f * t), new Vector3(DegreesToRadians(16f * t), DegreesToRadians(-14f * t), DegreesToRadians(10f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(140f * inverse, 80f * inverse, 250f * inverse), new Vector3(DegreesToRadians(-22f * inverse), DegreesToRadians(18f * inverse), DegreesToRadians(-10f * inverse)), Vector3.One),
            ],
            SlideTransitionKind.Comb =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-260f * t, 0f, -150f * t), new Vector3(0f, DegreesToRadians(8f * t), DegreesToRadians(14f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(260f * inverse, 0f, 220f * inverse), new Vector3(0f, DegreesToRadians(-14f * inverse), DegreesToRadians(-16f * inverse)), Vector3.One),
            ],
            SlideTransitionKind.RandomBar =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-180f * t, 40f * MathF.Sin(t * MathF.PI * 3f), -200f * t), new Vector3(DegreesToRadians(8f * t), DegreesToRadians(14f * t), DegreesToRadians(-8f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(200f * inverse, -45f * MathF.Sin(inverse * MathF.PI * 2f), 260f * inverse), new Vector3(DegreesToRadians(-10f * inverse), DegreesToRadians(-16f * inverse), DegreesToRadians(6f * inverse)), Vector3.One),
            ],
            SlideTransitionKind.Split =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -220f * t), Vector3.Zero, new Vector3(Lerp(1f, 0.78f, t), 1f, 1f)),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 320f * inverse), Vector3.Zero, new Vector3(Lerp(1.22f, 1f, t), 1f, 1f)),
            ],
            SlideTransitionKind.Strips =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, -180f * t, -150f * t), new Vector3(DegreesToRadians(-14f * t), 0f, DegreesToRadians(6f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 220f * inverse, 210f * inverse), new Vector3(DegreesToRadians(24f * inverse), 0f, DegreesToRadians(-10f * inverse)), Vector3.One),
            ],
            SlideTransitionKind.Circle =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -180f * t), new Vector3(DegreesToRadians(10f * t), DegreesToRadians(28f * t), DegreesToRadians(40f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 260f * inverse), new Vector3(DegreesToRadians(-14f * inverse), DegreesToRadians(-34f * inverse), DegreesToRadians(-46f * inverse)), Vector3.One * Lerp(0.9f, 1f, t)),
            ],
            SlideTransitionKind.Cut => t < 0.5f
                ? [new SlideVisual(fromSlideIndex, 1f, Vector3.Zero, Vector3.Zero, Vector3.One)]
                : [new SlideVisual(toSlideIndex, 1f, Vector3.Zero, Vector3.Zero, Vector3.One)],
            SlideTransitionKind.Diamond =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -140f * t), new Vector3(0f, DegreesToRadians(12f * t), DegreesToRadians(45f * t)), Vector3.One * Lerp(1f, 0.82f, t)),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 260f * inverse), new Vector3(0f, DegreesToRadians(-18f * inverse), DegreesToRadians(45f - (45f * t))), Vector3.One * Lerp(1.18f, 1f, t)),
            ],
            SlideTransitionKind.Plus =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -180f * t), new Vector3(DegreesToRadians(18f * t), DegreesToRadians(-18f * t), 0f), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 260f * inverse), new Vector3(DegreesToRadians(-28f * inverse), DegreesToRadians(28f * inverse), 0f), Vector3.One),
            ],
            SlideTransitionKind.Wedge =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(-120f * t, 0f, -180f * t), new Vector3(0f, DegreesToRadians(18f * t), DegreesToRadians(30f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(340f * inverse, 0f, 220f * inverse), new Vector3(0f, DegreesToRadians(-52f * inverse), DegreesToRadians(-16f * inverse)), Vector3.One),
            ],
            SlideTransitionKind.Wheel =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -220f * t), new Vector3(0f, 0f, DegreesToRadians(180f * t)), Vector3.One),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 300f * inverse), new Vector3(0f, 0f, DegreesToRadians(-270f * inverse)), Vector3.One * Lerp(0.82f, 1f, t)),
            ],
            SlideTransitionKind.Zoom =>
            [
                new SlideVisual(fromSlideIndex, inverse, new Vector3(0f, 0f, -420f * t), Vector3.Zero, Vector3.One * Lerp(1f, 0.72f, t)),
                new SlideVisual(toSlideIndex, t, new Vector3(0f, 0f, 540f * inverse), Vector3.Zero, Vector3.One * Lerp(0.58f, 1f, t)),
            ],
            _ => [new SlideVisual(toSlideIndex, 1f, Vector3.Zero, Vector3.Zero, Vector3.One)],
        };
    }

    private uint CreateProgram()
    {
        const string vertexShaderSource = """
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoord;
out vec2 vTexCoord;
void main()
{
    gl_Position = vec4(aPosition, 1.0);
    vTexCoord = aTexCoord;
}
""";

        const string fragmentShaderSource = """
#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;
uniform sampler2D uTexture;
uniform float uOpacity;
uniform float uShade;
void main()
{
    vec4 color = texture(uTexture, vTexCoord);
    FragColor = vec4(color.rgb * uShade, color.a * uOpacity);
}
""";

        uint vertexShader = CompileShader(GLEnum.VertexShader, vertexShaderSource);
        uint fragmentShader = CompileShader(GLEnum.FragmentShader, fragmentShaderSource);
        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, GLEnum.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            _gl.DeleteShader(vertexShader);
            _gl.DeleteShader(fragmentShader);
            _gl.DeleteProgram(program);
            throw new InvalidOperationException($"Failed to link slide transition program: {log}");
        }

        _gl.DetachShader(program, vertexShader);
        _gl.DetachShader(program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _gl.UseProgram(program);
        int textureLocation = _gl.GetUniformLocation(program, "uTexture");
        _gl.Uniform1(textureLocation, 0);
        _gl.UseProgram(0);

        return program;
    }

    private uint CompileShader(GLEnum shaderType, string source)
    {
        uint shader = _gl.CreateShader(shaderType);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, GLEnum.CompileStatus, out int compiled);
        if (compiled == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"Failed to compile {shaderType} shader: {log}");
        }

        return shader;
    }

    private static Vector3 RotateX(Vector3 value, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector3(value.X, (value.Y * cos) - (value.Z * sin), (value.Y * sin) + (value.Z * cos));
    }

    private static Vector3 RotateY(Vector3 value, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector3((value.X * cos) + (value.Z * sin), value.Y, (-value.X * sin) + (value.Z * cos));
    }

    private static Vector3 RotateZ(Vector3 value, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector3((value.X * cos) - (value.Y * sin), (value.X * sin) + (value.Y * cos), value.Z);
    }

    private static float EaseInOut(float value)
    {
        return value * value * (3f - (2f * value));
    }

    private static float Lerp(float from, float to, float amount)
    {
        return from + ((to - from) * amount);
    }

    private static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }

    private readonly record struct ProjectionContext(float AspectRatio, float TanHalfX, float TanHalfY, float CameraDistance, float Zoom, Vector2 Pan);

    private readonly record struct SlideVisual(int SlideIndex, float Opacity, Vector3 Translation, Vector3 Rotation, Vector3 Scale)
    {
        public float DepthSort => Translation.Z;
    }
}
