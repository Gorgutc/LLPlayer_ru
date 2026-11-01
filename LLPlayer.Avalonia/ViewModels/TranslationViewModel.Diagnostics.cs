using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer.Translation;
using FlyleafLib.MediaPlayer.Translation.Services;
using LLPlayer.Avalonia.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LLPlayer.Avalonia.ViewModels;

public partial class TranslationViewModel
{
    readonly ITranslationDiagnostics diagnostics;
    CancellationTokenSource? diagnosticCancellation;
    Task? activeDiagnostic;
    int diagnosticGeneration;
    bool closing;
    public bool IsSaving { get; private set; }
    public bool DiagnosticRunning => diagnosticCancellation != null;
    public ObservableCollection<string> AvailableModels { get; } = [];
    [ObservableProperty] string? selectedAvailableModel;
    [ObservableProperty] string diagnosticStatus = "";
    partial void OnSelectedAvailableModelChanged(string? value) { if (!loading && !string.IsNullOrEmpty(value)) Model = value; }
    partial void OnTargetLanguageChanged(TargetLanguage value) => OnPropertyChanged(nameof(TargetAdvisory));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        // Any draft/selection mutation invalidates a captured request, including programmatic changes while disabled.
        if (!loading && e.PropertyName is not (nameof(Busy) or nameof(Message) or nameof(DiagnosticStatus) or nameof(DiagnosticRunning) or nameof(SelectedAvailableModel))
            && diagnosticCancellation != null)
        {
            diagnosticGeneration++;
            diagnosticCancellation.Cancel();
        }
        base.OnPropertyChanged(e);
    }

    [RelayCommand] Task CheckProviderAsync() => StartDiagnostic(TranslationDiagnosticKind.Check);
    [RelayCommand] Task GetModelsAsync() => StartDiagnostic(TranslationDiagnosticKind.GetModels);
    [RelayCommand] Task HelloApiAsync() => StartDiagnostic(TranslationDiagnosticKind.Hello);
    [RelayCommand] void CancelDiagnostic() { diagnosticGeneration++; diagnosticCancellation?.Cancel(); }
    Task StartDiagnostic(TranslationDiagnosticKind kind)
    {
        if (Busy || closing) return Task.CompletedTask;
        try
        {
            CaptureChat(); CaptureFields();
            Draft.WordProvider = Provider; Draft.SentenceProvider = SentenceProvider; Draft.TargetLanguage = TargetLanguage;
            Draft.Validate();
            if (!Enum.TryParse<TranslateServiceType>(EditingProvider, out var type) || !Enum.IsDefined(type))
            { DiagnosticStatus = "Choose a provider before running a diagnostic."; return Task.CompletedTask; }
            var request = new TranslationDiagnosticRequest(type, Draft.Clone(), kind);
            var cancellation = new CancellationTokenSource();
            diagnosticCancellation = cancellation;
            int generation = ++diagnosticGeneration;
            Busy = true; OnPropertyChanged(nameof(DiagnosticRunning));
            DiagnosticStatus = kind == TranslationDiagnosticKind.Hello ? "Sending Hello model request…" : "Checking provider…";
            activeDiagnostic = RunDiagnosticAsync(request, cancellation, generation);
            return activeDiagnostic;
        }
        catch (ArgumentException e) { Message = e.Message; return Task.CompletedTask; }
    }

    async Task RunDiagnosticAsync(TranslationDiagnosticRequest request, CancellationTokenSource cancellation, int generation)
    {
        try
        {
            var result = await diagnostics.RunAsync(request, cancellation.Token);
            if (closing || cancellation.IsCancellationRequested || generation != diagnosticGeneration) return;
            if (result.Outcome == TranslationDiagnosticOutcome.Unsupported)
            { DiagnosticStatus = "This diagnostic is not supported by the selected provider."; return; }
            if (request.Kind == TranslationDiagnosticKind.GetModels)
            {
                AvailableModels.Clear();
                foreach (string name in (result.Models ?? []).Where(IsSafeModelName).Distinct(StringComparer.Ordinal)) AvailableModels.Add(name);
                DiagnosticStatus = AvailableModels.Count == 0 ? "No models were returned. The entered model was retained." : "Models refreshed. The entered model was retained.";
            }
            else DiagnosticStatus = request.Kind == TranslationDiagnosticKind.Hello ? "Hello model request succeeded." : "Provider check succeeded.";
        }
        catch (OperationCanceledException) { if (!closing && generation == diagnosticGeneration) DiagnosticStatus = "Diagnostic canceled."; }
        catch (Exception) { if (!closing && generation == diagnosticGeneration) DiagnosticStatus = "Diagnostic failed. Check the provider configuration and connection."; }
        finally
        {
            if (ReferenceEquals(diagnosticCancellation, cancellation))
            {
                diagnosticCancellation = null;
                Busy = false; OnPropertyChanged(nameof(DiagnosticRunning));
                if (!closing && generation != diagnosticGeneration) DiagnosticStatus = "Diagnostic canceled; results discarded.";
            }
            cancellation.Dispose();
        }
    }
    bool IsSafeModelName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && !name.Any(char.IsControl)
        && (string.IsNullOrEmpty(ApiKey) || !name.Contains(ApiKey, StringComparison.Ordinal))
        && !name.Contains("Bearer ", StringComparison.OrdinalIgnoreCase);

    public async Task DrainDiagnosticsAsync()
    {
        closing = true; diagnosticGeneration++; diagnosticCancellation?.Cancel();
        if (activeDiagnostic != null) await activeDiagnostic;
    }
}
