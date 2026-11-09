using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LLPlayer.Avalonia.Services;

namespace LLPlayer.Avalonia.ViewModels;

public sealed partial class AboutViewModel(AboutInformation information, Action<string> copy,
    Func<Uri, Task<bool>>? openLink = null) : ObservableObject
{
    public AboutInformation Information { get; } = information;
    public string ProjectUrl => AboutInformation.ProjectUrl;
    public string UpstreamUrl => AboutInformation.UpstreamUrl;
    [ObservableProperty] public partial string Message { get; private set; } = "";
    [ObservableProperty] public partial string NoticesText { get; private set; } = "";

    [RelayCommand]
    void CopyVersion()
    {
        copy(Information.CopyText);
        Message = "Version information copied.";
    }

    public async Task OpenLinkAsync(string? link)
    {
        if (openLink == null || link == null || !(link == ProjectUrl || link == UpstreamUrl
            || Information.Credits.Any(credit => credit.Url == link))) return;
        if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0) return;
        try { Message = await openLink(uri) ? "" : "A browser is not available for this link."; }
        catch (Exception) { Message = "The link could not be opened."; }
    }

    [RelayCommand]
    void LoadNotices()
    {
        try { NoticesText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md")); }
        catch (IOException) { NoticesText = "Bundled notices are not present in this development output. Published packages include THIRD-PARTY-NOTICES.md."; }
        catch (UnauthorizedAccessException) { NoticesText = "The bundled notices could not be read."; }
    }
}
