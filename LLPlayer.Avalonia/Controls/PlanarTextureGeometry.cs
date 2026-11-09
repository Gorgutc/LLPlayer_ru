using Avalonia;
using FlyleafLib.MediaFramework.MediaRenderer;

namespace LLPlayer.Avalonia.Controls;

/// <summary>Quad coordinates for the existing crop / transpose / output-flip contract.</summary>
internal static class PlanarTextureGeometry
{
    public static PixelSize VisibleSize(int width, int height, in VideoPresentationParameters presentation)
    {
        int w = Math.Max(1, width - (int)Math.Min((long)presentation.Crop.Left + presentation.Crop.Right, width - 1L));
        int h = Math.Max(1, height - (int)Math.Min((long)presentation.Crop.Top + presentation.Crop.Bottom, height - 1L));
        return presentation.Transpose ? new(h, w) : new(w, h);
    }

    public static void WriteVertices(Span<float> vertices, int width, int height, in VideoPresentationParameters presentation)
    {
        if (vertices.Length < 16 || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(vertices));
        float left = Math.Min(presentation.Crop.Left, (uint)width - 1);
        float top = Math.Min(presentation.Crop.Top, (uint)height - 1);
        float right = Math.Min(presentation.Crop.Right, width - 1 - left);
        float bottom = Math.Min(presentation.Crop.Bottom, height - 1 - top);
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2;
            float y = i < 2 ? 1 : 0;
            vertices[i * 4] = x * 2 - 1;
            vertices[i * 4 + 1] = 1 - y * 2;
            if (presentation.FlipX) x = 1 - x;
            if (presentation.FlipY) y = 1 - y;
            if (presentation.Transpose) (x, y) = (y, x);
            vertices[i * 4 + 2] = (left + x * (width - left - right)) / width;
            vertices[i * 4 + 3] = (top + y * (height - top - bottom)) / height;
        }
    }
}
