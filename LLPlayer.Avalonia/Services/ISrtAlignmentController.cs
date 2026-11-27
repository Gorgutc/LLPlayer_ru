using LLPlayer.Core.Services;

namespace LLPlayer.Avalonia.Services;

public interface ISrtAlignmentController
{
    bool CanStartSrtAlignment(int slot);
    ISrtAlignmentSession CaptureSrtAlignment(int slot);
}
