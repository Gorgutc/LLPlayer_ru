using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Only these callbacks touch GL. The producer just copies into the bounded mailbox.</summary>
internal sealed class PlanarGlControl(VideoView owner, PlanarFrameMailbox mailbox) : OpenGlControlBase
{
    readonly PlanarGlFramePump pump = new(mailbox);

    public void RequestFrame() => RequestNextFrameRendering();

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            var api = new NativePlanarGl(gl);
            pump.Initialize(api);
            owner.OpenGlReady(api.DriverDescription);
        }
        catch (Exception error)
        {
            pump.Deinitialize();
            owner.OpenGlFailed($"OpenGL initialization failed: {error.Message}");
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!pump.IsInitialized)
            return;
        try
        {
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = Math.Max(1, (int)Math.Round(Bounds.Width * scaling));
            int height = Math.Max(1, (int)Math.Round(Bounds.Height * scaling));
            var background = owner.Background is ISolidColorBrush brush ? brush.Color : Colors.Black;
            pump.Render(fb, width, height, pictureSize =>
            {
                var destination = owner.GetDestinationRect(new Rect(Bounds.Size), pictureSize);
                return new Rect(destination.X * scaling, destination.Y * scaling, destination.Width * scaling, destination.Height * scaling);
            }, background, owner.ShowPlanarFrame);
        }
        catch (Exception error)
        {
            // Still in the current-context render callback: deletion is legal here.
            pump.Deinitialize();
            owner.OpenGlFailed($"OpenGL presentation failed: {error.Message}");
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        pump.Deinitialize();
        owner.OpenGlFailed("OpenGL context was released; software presentation is active.");
    }

    protected override void OnOpenGlLost()
    {
        // The old context is dead. Its object identities must never be deleted in a new context.
        pump.LoseContext();
        owner.OpenGlFailed("OpenGL context was lost; software presentation is active.");
    }
}
