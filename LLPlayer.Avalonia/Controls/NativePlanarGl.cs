using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.OpenGL;
using FlyleafLib;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Small context-owned GL binding. Created, called and disposed only from Avalonia's GL callbacks.</summary>
internal sealed unsafe class NativePlanarGl : IPlanarGlApi
{
    const uint Texture2D = 0x0DE1, ArrayBuffer = 0x8892;
    readonly GetStringFn getString;
    readonly GetErrorFn getError;
    readonly CreateShaderFn createShader;
    readonly ShaderSourceFn shaderSource;
    readonly OneUIntFn compileShader, deleteShader, deleteProgram, useProgram, enableAttribute, disable;
    readonly GetObjectFn getShader, getProgram;
    readonly GetLogFn getShaderLog, getProgramLog;
    readonly CreateProgramFn createProgram;
    readonly TwoUIntFn attachShader, bindTexture, bindBuffer, bindFramebuffer;
    readonly BindAttributeFn bindAttribute;
    readonly OneUIntFn linkProgram, activeTexture;
    readonly GetUniformFn getUniform;
    readonly UniformIntFn uniformInt;
    readonly Uniform2Fn uniform2;
    readonly Uniform4Fn uniform4;
    readonly ObjectsFn genTextures, deleteTextures, genBuffers, deleteBuffers;
    readonly ObjectsFn? genVertexArrays, deleteVertexArrays;
    readonly OneUIntFn? bindVertexArray;
    readonly BufferDataFn bufferData;
    readonly AttributeFn attribute;
    readonly TexParameterFn texParameter;
    readonly TexImageFn texImage;
    readonly TexSubImageFn texSubImage;
    readonly PixelStoreFn pixelStore;
    readonly ViewportFn viewport;
    readonly ClearColorFn clearColor;
    readonly OneUIntFn clear;
    readonly DrawFn draw;
    readonly uint[] textures = new uint[3];
    readonly int[] textureWidths = new int[3], textureHeights = new int[3];
    uint program, vertexBuffer, vertexArray;
    int nv12Uniform, conversionUniform, chromaScaleUniform, yUniform, uUniform, vUniform;
    readonly bool modernTextures;
    int width, height;
    PlanarVideoPixelFormat format;
    VideoPresentationParameters presentation;

    public string DriverDescription { get; }

    public NativePlanarGl(GlInterface gl)
    {
        T Load<T>(string name) where T : Delegate
        {
            nint address = gl.GetProcAddress(name);
            if (address == 0)
                throw new NotSupportedException($"Required GL procedure {name} is unavailable.");
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }
        T? Optional<T>(string name) where T : Delegate
        {
            nint address = gl.GetProcAddress(name);
            return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        getString = Load<GetStringFn>("glGetString");
        getError = Load<GetErrorFn>("glGetError");
        createShader = Load<CreateShaderFn>("glCreateShader");
        shaderSource = Load<ShaderSourceFn>("glShaderSource");
        compileShader = Load<OneUIntFn>("glCompileShader");
        deleteShader = Load<OneUIntFn>("glDeleteShader");
        getShader = Load<GetObjectFn>("glGetShaderiv");
        getProgram = Load<GetObjectFn>("glGetProgramiv");
        getShaderLog = Load<GetLogFn>("glGetShaderInfoLog");
        getProgramLog = Load<GetLogFn>("glGetProgramInfoLog");
        createProgram = Load<CreateProgramFn>("glCreateProgram");
        attachShader = Load<TwoUIntFn>("glAttachShader");
        bindAttribute = Load<BindAttributeFn>("glBindAttribLocation");
        linkProgram = Load<OneUIntFn>("glLinkProgram");
        deleteProgram = Load<OneUIntFn>("glDeleteProgram");
        useProgram = Load<OneUIntFn>("glUseProgram");
        getUniform = Load<GetUniformFn>("glGetUniformLocation");
        uniformInt = Load<UniformIntFn>("glUniform1i");
        uniform2 = Load<Uniform2Fn>("glUniform2f");
        uniform4 = Load<Uniform4Fn>("glUniform4f");
        genTextures = Load<ObjectsFn>("glGenTextures");
        deleteTextures = Load<ObjectsFn>("glDeleteTextures");
        bindTexture = Load<TwoUIntFn>("glBindTexture");
        activeTexture = Load<OneUIntFn>("glActiveTexture");
        genBuffers = Load<ObjectsFn>("glGenBuffers");
        deleteBuffers = Load<ObjectsFn>("glDeleteBuffers");
        bindBuffer = Load<TwoUIntFn>("glBindBuffer");
        bufferData = Load<BufferDataFn>("glBufferData");
        attribute = Load<AttributeFn>("glVertexAttribPointer");
        enableAttribute = Load<OneUIntFn>("glEnableVertexAttribArray");
        genVertexArrays = Optional<ObjectsFn>("glGenVertexArrays");
        deleteVertexArrays = Optional<ObjectsFn>("glDeleteVertexArrays");
        bindVertexArray = Optional<OneUIntFn>("glBindVertexArray");
        bindFramebuffer = Load<TwoUIntFn>("glBindFramebuffer");
        texParameter = Load<TexParameterFn>("glTexParameteri");
        texImage = Load<TexImageFn>("glTexImage2D");
        texSubImage = Load<TexSubImageFn>("glTexSubImage2D");
        pixelStore = Load<PixelStoreFn>("glPixelStorei");
        viewport = Load<ViewportFn>("glViewport");
        clearColor = Load<ClearColorFn>("glClearColor");
        clear = Load<OneUIntFn>("glClear");
        disable = Load<OneUIntFn>("glDisable");
        draw = Load<DrawFn>("glDrawArrays");

        string version = ReadString(0x1F02);
        DriverDescription = $"{ReadString(0x1F00)} | {ReadString(0x1F01)} | {version}";
        bool es = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        string numeric = es ? version[(version.IndexOf("OpenGL ES", StringComparison.OrdinalIgnoreCase) + 9)..].TrimStart(' ', '-', 'C', 'M') : version;
        int major = numeric.Length > 0 && char.IsAsciiDigit(numeric[0]) ? numeric[0] - '0' : 0;
        if (major < 2)
            throw new NotSupportedException($"OpenGL 2 / GLES 2 or newer is required ({version}).");
        modernTextures = major >= 3;
        try
        {
            // A shared context may contain an earlier error; attribute only our subsequent errors to this renderer.
            for (int i = 0; i < 32 && getError() != 0; i++) { }
            InitializeResources(es, modernTextures);
            CheckError("initialization");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    string ReadString(uint name) => Marshal.PtrToStringAnsi(getString(name)) ?? "Unknown";

    void InitializeResources(bool es, bool modern)
    {
        uint vertex = 0, fragment = 0;
        try
        {
            var sources = PlanarShaderSources.Create(es, modern);
            vertex = Compile(0x8B31, sources.Vertex);
            fragment = Compile(0x8B30, sources.Fragment);
            program = createProgram();
            attachShader(program, vertex);
            attachShader(program, fragment);
            bindAttribute(program, 0, "aPosition");
            bindAttribute(program, 1, "aUv");
            linkProgram(program);
            int linked;
            getProgram(program, 0x8B82, &linked);
            if (linked == 0)
                throw new InvalidOperationException("Shader link failed: " + ObjectLog(program, false));
        }
        finally
        {
            if (vertex != 0) deleteShader(vertex);
            if (fragment != 0) deleteShader(fragment);
        }
        nv12Uniform = getUniform(program, "uNv12");
        conversionUniform = getUniform(program, "uConversion");
        chromaScaleUniform = getUniform(program, "uChromaScale");
        yUniform = getUniform(program, "uY");
        uUniform = getUniform(program, "uU");
        vUniform = getUniform(program, "uV");
        fixed (uint* ids = textures) genTextures(3, ids);
        uint buffer;
        genBuffers(1, &buffer);
        vertexBuffer = buffer;
        if (genVertexArrays != null && bindVertexArray != null && deleteVertexArrays != null)
        {
            uint array;
            genVertexArrays(1, &array);
            vertexArray = array;
            bindVertexArray(array);
        }
        bindBuffer(ArrayBuffer, vertexBuffer);
        attribute(0, 2, 0x1406, 0, 16, 0);
        attribute(1, 2, 0x1406, 0, 16, 8);
        enableAttribute(0);
        enableAttribute(1);
    }

    uint Compile(uint kind, string source)
    {
        uint shader = createShader(kind);
        try
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(source + '\0');
            fixed (byte* text = utf8)
            {
                byte* pointer = text;
                shaderSource(shader, 1, &pointer, null);
            }
            compileShader(shader);
            int compiled;
            getShader(shader, 0x8B81, &compiled);
            if (compiled == 0)
                throw new InvalidOperationException("Shader compilation failed: " + ObjectLog(shader, true));
            return shader;
        }
        catch { deleteShader(shader); throw; }
    }

    string ObjectLog(uint id, bool shader)
    {
        byte* bytes = stackalloc byte[4096];
        int length = 0;
        (shader ? getShaderLog : getProgramLog)(id, 4096, &length, bytes);
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(bytes, Math.Clamp(length, 0, 4095)));
    }

    public void Upload(PlanarFrameMailbox.Frame frame)
    {
        width = frame.Width;
        height = frame.Height;
        format = frame.Format;
        presentation = frame.Presentation;
        pixelStore(0x0CF5, 1); // GL_UNPACK_ALIGNMENT; mailbox rows are tightly packed.
        UploadPlane(0, frame.Y, width, height, false);
        int cw = (width + 1) / 2, ch = (height + 1) / 2;
        UploadPlane(1, frame.Uv, cw, ch, format == PlanarVideoPixelFormat.Nv12);
        if (format == PlanarVideoPixelFormat.Yuv420P)
            UploadPlane(2, frame.V, cw, ch, false);
        Span<float> vertices = stackalloc float[16];
        PlanarTextureGeometry.WriteVertices(vertices, width, height, presentation);
        if (vertexArray != 0) bindVertexArray!(vertexArray);
        bindBuffer(ArrayBuffer, vertexBuffer);
        fixed (float* data = vertices) bufferData(ArrayBuffer, 16 * sizeof(float), data, 0x88E8);
        CheckError("plane upload");
    }

    void UploadPlane(int index, byte[] bytes, int w, int h, bool pair)
    {
        activeTexture((uint)(0x84C0 + index));
        bindTexture(Texture2D, textures[index]);
        texParameter(Texture2D, 0x2801, 0x2601);
        texParameter(Texture2D, 0x2800, 0x2601);
        texParameter(Texture2D, 0x2802, 0x812F);
        texParameter(Texture2D, 0x2803, 0x812F);
        uint external = modernTextures ? (pair ? 0x8227u : 0x1903u) : (pair ? 0x190Au : 0x1909u);
        int internalFormat = modernTextures ? (pair ? 0x822B : 0x8229) : (int)external;
        fixed (byte* data = bytes)
        {
            // Changing NV12 / YUV420 at the same size also changes the second texture's channel count.
            int identityWidth = pair ? -w : w;
            if (textureWidths[index] != identityWidth || textureHeights[index] != h)
            {
                texImage(Texture2D, 0, internalFormat, w, h, 0, external, 0x1401, data);
                textureWidths[index] = identityWidth;
                textureHeights[index] = h;
            }
            else
                texSubImage(Texture2D, 0, 0, 0, w, h, external, 0x1401, data);
        }
    }

    public void Draw(int framebuffer, int targetWidth, int targetHeight, Rect destination, Color background, bool show)
    {
        bindFramebuffer(0x8D40, (uint)framebuffer);
        disable(0x0C11); // scissor
        disable(0x0B71); // depth
        disable(0x0B44); // culling
        disable(0x0BE2); // blending
        viewport(0, 0, targetWidth, targetHeight);
        if (show)
            clearColor(background.R / 255f, background.G / 255f, background.B / 255f, 1);
        else
            clearColor(0, 0, 0, 0); // the CPU parent stays visible until a planar picture has been accepted.
        clear(0x00004000);
        if (!show || width <= 0 || destination.Width <= 0 || destination.Height <= 0)
            return;
        viewport((int)Math.Round(destination.X), (int)Math.Round(targetHeight - destination.Bottom),
            Math.Max(1, (int)Math.Round(destination.Width)), Math.Max(1, (int)Math.Round(destination.Height)));
        useProgram(program);
        if (vertexArray != 0) bindVertexArray!(vertexArray);
        bindBuffer(ArrayBuffer, vertexBuffer);
        attribute(0, 2, 0x1406, 0, 16, 0);
        attribute(1, 2, 0x1406, 0, 16, 8);
        enableAttribute(0);
        enableAttribute(1);
        for (int i = 0; i < 3; i++)
        {
            activeTexture((uint)(0x84C0 + i));
            bindTexture(Texture2D, textures[i]);
        }
        uniformInt(yUniform, 0);
        uniformInt(uUniform, 1);
        uniformInt(vUniform, format == PlanarVideoPixelFormat.Nv12 ? 1 : 2);
        uniformInt(nv12Uniform, format == PlanarVideoPixelFormat.Nv12 ? 1 : 0);
        (float kr, float kb) = presentation.ColorSpace switch
        {
            ColorSpace.Bt709 => (0.2126f, 0.0722f),
            ColorSpace.Bt2020 => (0.2627f, 0.0593f),
            _ => (0.299f, 0.114f)
        };
        bool full = presentation.ColorRange == ColorRange.Full;
        uniform4(conversionUniform, kr, kb, full ? 1 : 255f / 219, full ? 1 : 255f / 224);
        uniform2(chromaScaleUniform, width / (2f * ((width + 1) / 2)), height / (2f * ((height + 1) / 2)));
        draw(0x0005, 0, 4);
        CheckError("draw");
    }

    void CheckError(string operation)
    {
        uint error = getError();
        if (error != 0)
            throw new InvalidOperationException($"GL {operation} failed (0x{error:X}).");
    }

    public void Dispose()
    {
        if (program != 0) { deleteProgram(program); program = 0; }
        if (vertexBuffer != 0)
        {
            uint buffer = vertexBuffer;
            deleteBuffers(1, &buffer);
            vertexBuffer = 0;
        }
        if (vertexArray != 0 && deleteVertexArrays != null)
        {
            uint array = vertexArray;
            deleteVertexArrays(1, &array);
            vertexArray = 0;
        }
        fixed (uint* ids = textures) deleteTextures(3, ids);
        Array.Clear(textures);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate nint GetStringFn(uint name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint GetErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint CreateShaderFn(uint kind);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ShaderSourceFn(uint shader, int count, byte** source, int* lengths);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void OneUIntFn(uint value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TwoUIntFn(uint first, uint second);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GetObjectFn(uint id, uint name, int* value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GetLogFn(uint id, int capacity, int* length, byte* text);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint CreateProgramFn();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindAttributeFn(uint program, uint index, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetUniformFn(uint program, [MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void UniformIntFn(int location, int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform2Fn(int location, float x, float y);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform4Fn(int location, float x, float y, float z, float w);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ObjectsFn(int count, uint* values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BufferDataFn(uint target, nint size, void* data, uint usage);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void AttributeFn(uint index, int count, uint type, byte normalized, int stride, nint offset);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TexParameterFn(uint target, uint name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TexImageFn(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, void* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TexSubImageFn(uint target, int level, int x, int y, int width, int height, uint format, uint type, void* data);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void PixelStoreFn(uint name, int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ViewportFn(int x, int y, int width, int height);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ClearColorFn(float red, float green, float blue, float alpha);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DrawFn(uint mode, int first, int count);
}

internal static class PlanarShaderSources
{
    public static (string Vertex, string Fragment) Create(bool es, bool modern)
    {
        string header = modern ? (es ? "#version 300 es\n" : "#version 130\n") : (es ? "#version 100\n" : "#version 120\n");
        string precision = es ? "precision mediump float;\n" : "";
        string input = modern ? "in" : "attribute";
        string vertexOutput = modern ? "out" : "varying";
        string fragmentInput = modern ? "in" : "varying";
        string sample = modern ? "texture" : "texture2D";
        string output = modern ? "fragColor" : "gl_FragColor";
        string pair = modern ? "rg" : "ra";
        string vertex = header + precision + $"{input} vec2 aPosition; {input} vec2 aUv; {vertexOutput} vec2 vUv;\n" +
            "void main() { gl_Position=vec4(aPosition,0.0,1.0); vUv=aUv; }\n";
        string fragment = header + precision + $"{fragmentInput} vec2 vUv;\n" +
            (modern ? "out vec4 fragColor;\n" : "") +
            "uniform sampler2D uY,uU,uV; uniform int uNv12; uniform vec4 uConversion; uniform vec2 uChromaScale;\n" +
            "void main() {\n" +
            $"float y={sample}(uY,vUv).r; vec2 p=vUv*uChromaScale; vec2 uv;\n" +
            $"if(uNv12==1) uv={sample}(uU,p).{pair}; else uv=vec2({sample}(uU,p).r,{sample}(uV,p).r);\n" +
            "float kr=uConversion.x,kb=uConversion.y,kg=1.0-kr-kb;\n" +
            "y=(y-(uConversion.z>1.0 ? 16.0/255.0 : 0.0))*uConversion.z; uv=(uv-vec2(128.0/255.0))*uConversion.w;\n" +
            "vec3 rgb=vec3(y+2.0*(1.0-kr)*uv.y,y-2.0*kb*(1.0-kb)/kg*uv.x-2.0*kr*(1.0-kr)/kg*uv.y,y+2.0*(1.0-kb)*uv.x);\n" +
            $"{output}=vec4(clamp(rgb,0.0,1.0),1.0); }}\n";
        return (vertex, fragment);
    }
}
