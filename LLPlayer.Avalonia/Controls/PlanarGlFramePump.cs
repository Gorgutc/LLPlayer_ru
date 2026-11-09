using Avalonia;
using Avalonia.Media;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Context-lifetime seam. Only the GL control's current-context callbacks invoke this owner.</summary>
internal interface IPlanarGlApi : IDisposable
{
    string DriverDescription { get; }
    void Upload(PlanarFrameMailbox.Frame frame);
    void Draw(int framebuffer, int width, int height, Rect destination, Color background, bool show);
}

internal sealed class PlanarGlFramePump(PlanarFrameMailbox mailbox)
{
    IPlanarGlApi? api;
    long uploadedEpoch = -1;
    PixelSize pictureSize;
    public bool IsInitialized => api != null;

    public void Initialize(IPlanarGlApi next)
    {
        api = next;
        uploadedEpoch = -1;
    }

    public void Render(int framebuffer, int width, int height, Func<PixelSize, Rect> destination, Color background, bool show)
    {
        if (api == null)
            return;
        var next = mailbox.Acquire();
        if (next != null)
        {
            try
            {
                if (mailbox.IsCurrent(next))
                {
                    api.Upload(next);
                    pictureSize = PlanarTextureGeometry.VisibleSize(next.Width, next.Height, next.Presentation);
                    uploadedEpoch = next.Epoch;
                }
            }
            finally { mailbox.Release(next); }
        }
        api.Draw(framebuffer, width, height, destination(pictureSize), background, show && uploadedEpoch == mailbox.Epoch);
    }

    public void Deinitialize()
    {
        api?.Dispose();
        api = null;
        uploadedEpoch = -1;
        mailbox.Clear();
    }

    public void LoseContext()
    {
        // Avalonia owns the dead context; never delete these names in a replacement context.
        api = null;
        uploadedEpoch = -1;
        mailbox.Clear();
    }
}
