using System.Globalization;

namespace FlyleafLib;

#nullable enable

// F-13 portable (Linux) TFM counterparts of the WPF/WinForms-bound members of Utils.cs (excluded there with #if WINDOWS).
public static partial class Utils
{
    /// <summary>
    /// UI dispatcher used by <see cref="UI"/>, <see cref="UIIfRequired"/>, <see cref="UIInvoke"/> and
    /// <see cref="UIInvokeIfRequired"/> on the portable build. Null (default) runs UI actions inline.
    /// </summary>
    public static IUIDispatcher? UIDispatcher { get; set; }

    /// <summary>Desktop-shell services (clipboard, file picker, sounds) provided by the host application.</summary>
    public static IHostServices? HostServices { get; set; }

    /// <summary>
    /// Begin Invokes the UI thread to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UI(Action action)
    {
        var dispatcher = UIDispatcher;
        if (dispatcher == null)
            action();
        else
            dispatcher.Post(action);
    }

    /// <summary>
    /// Begin Invokes the UI thread if required to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UIIfRequired(Action action)
    {
        var dispatcher = UIDispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Post(action);
    }

    /// <summary>
    /// Invokes the UI thread to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UIInvoke(Action action)
    {
        var dispatcher = UIDispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>
    /// Invokes the UI thread if required to execute the specified action
    /// </summary>
    /// <param name="action"></param>
    public static void UIInvokeIfRequired(Action action)
    {
        if (IsTesting) return;

        UIInvoke(action);
    }

    /// <summary>
    /// Starts a dedicated thread for <paramref name="action"/>. COM apartments do not exist off Windows, so unlike the
    /// Windows build the apartment state is left untouched (setting STA throws PlatformNotSupportedException on Unix).
    /// </summary>
    public static Thread STA(Action action)
    {
        Thread thread = new(() => action());
        thread.Start();

        return thread;
    }

    public static void STAInvoke(Action action)
    {
        Thread thread = STA(action);
        thread.Join();
    }

    /// <summary>
    /// Languages of the current user. The Windows build also adds every installed keyboard input language
    /// (WinForms <c>InputLanguage</c>); there is no portable equivalent, so the portable build uses English plus the
    /// original UI/thread cultures.
    /// </summary>
    public static List<Language> GetSystemLanguages()
    {
        List<Language> Languages = [ Language.English ];

        CultureInfo culture = OriginalCulture ?? CultureInfo.CurrentCulture;
        if (culture.ThreeLetterISOLanguageName != "eng" && !string.IsNullOrEmpty(culture.Name))
            Languages.Add(Language.Get(culture));

        CultureInfo uiCulture = OriginalUICulture ?? CultureInfo.CurrentUICulture;
        if (uiCulture.ThreeLetterISOLanguageName != culture.ThreeLetterISOLanguageName && uiCulture.ThreeLetterISOLanguageName != "eng" && !string.IsNullOrEmpty(uiCulture.Name))
            Languages.Add(Language.Get(uiCulture));

        return Languages;
    }

    // TODO: L: move to app, using event
    public static void PlayCompletionSound()
    {
        string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/completion.mp3");

        if (!File.Exists(soundPath))
        {
            return;
        }

        UI(() =>
        {
            try
            {
                HostServices?.PlayCompletionSound(soundPath);
            }
            catch
            {
                // ignored
            }
        });
    }
}
