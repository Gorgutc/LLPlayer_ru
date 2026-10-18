using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;

[assembly: AvaloniaTestApplication(typeof(LLPlayer.Avalonia.Tests.TestAppBuilder))]

namespace LLPlayer.Avalonia.Tests;

/// <summary>
/// Headless Avalonia app for the UI tests: the real <see cref="App"/> (FluentTheme + "LLPlayer shadcn" styles) rendered
/// with Skia so frames can be captured and pixels checked. No windows are created by App itself (no options).
/// </summary>
/// <summary>
/// Tests that touch FlyleafLib's process-wide seams (BindingOperations handlers, Utils.UIDispatcher, the engine) run in
/// this non-parallel collection so they cannot observe each other's temporary global state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FlyleafGlobalsCollection
{
    public const string Name = "FlyleafGlobals";
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, FrameBufferFormat = PixelFormat.Bgra8888 });
}
