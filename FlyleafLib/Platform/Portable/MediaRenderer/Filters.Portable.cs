using System.Text.Json.Serialization;

using FlyleafLib.Controls.WPF;

namespace FlyleafLib.MediaFramework.MediaRenderer;

// F-13 portable (Linux) counterparts of the video filter config types declared in the (excluded) Direct3D11
// Renderer.VF.FL.cs / Renderer.VF.D3.cs. Kept so Config.Video (FLFilters / D3Filters) has the same shape and JSON on
// every platform. The software renderer applies the Flyleaf filters to the converted frames (Renderer.FLSetFilter ->
// SoftwareColorFilter). D3D11 video-processor filters never become Available off Windows.

/// <summary>D3D11 video processor filter ids (numeric values match D3D11_VIDEO_PROCESSOR_FILTER).</summary>
public enum VideoProcessorFilter
{
    Brightness          = 0,
    Contrast            = 1,
    Hue                 = 2,
    Saturation          = 3,
    NoiseReduction      = 4,
    EdgeEnhancement     = 5,
    AnamorphicScaling   = 6,
    StereoAdjustment    = 7
}

public class FLFilter : NotifyPropertyChanged
{   // TBR: Publics that currently required for Serialization

    [JsonIgnore]
    public bool         Available   => true; // TBR: FL always available
    public FLFilters    Filter      { get; set; }
    public int          Minimum     { get; set; }
    public float        MinimumPS   { get; set; }
    public int          Maximum     { get; set; }
    public float        MaximumPS   { get; set; }
    public float        Step        { get; set; }
    public int          Default
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                SetDefaultValue.OnCanExecuteChanged();
            }
        }
    }
    public int Value
    {
        get => _Value;
        set
        {
            int v = value;
            v = Math.Min(v, Maximum);
            v = Math.Max(v, Minimum);

            if (Set(ref _Value, v))
            {
                renderer?.FLSetFilter(this, true);
                SetDefaultValue.OnCanExecuteChanged();
            }
        }
    }
    protected int _Value;

    [JsonIgnore]
    public RelayCommand SetDefaultValue => field ??= new(_ =>
    {
        Value = Default;
    }, _ => Value != Default);

    Renderer renderer;

    public FLFilter() { }

    internal FLFilter(Renderer renderer, FLFilterSpec filterSpec)
    {
        Filter      = filterSpec.Filter;
        Minimum     = filterSpec.Minimum;
        MinimumPS   = filterSpec.MinimumPS;
        Maximum     = filterSpec.Maximum;
        MaximumPS   = filterSpec.MaximumPS;
        Step        = filterSpec.Step;
        Default     = Value = filterSpec.Default;
        this.renderer = renderer;
    }

    internal void Initialize(Renderer renderer)
        => this.renderer = renderer;

    internal void Dispose()
        => renderer = null;

    internal static List<FLFilterSpec> FLFilterSpecs =
        [new()
        {
            Filter  = FLFilters.Brightness,
            Minimum = -100,
            Maximum = 100,
            Default = 0,
            Step    = 1,

            MinimumPS = -0.5f,
            MaximumPS =  0.5f
        },
        new()
        {
            Filter  = FLFilters.Contrast,
            Minimum = -100,
            Maximum = 100,
            Default = 0,
            Step    = 1,

            MinimumPS = 0f,
            MaximumPS = 2f
        },
        new()
        {
            Filter  = FLFilters.Hue,
            Minimum = -180,
            Maximum = 180,
            Default = 0,
            Step    = 1,

            MinimumPS = -3.14f,
            MaximumPS =  3.14f
        },
        new()
        {
            Filter  = FLFilters.Saturation,
            Minimum = -100,
            Maximum = 100,
            Default = 0,
            Step    = 1,

            MinimumPS = 0,
            MaximumPS = 2
        }];

    internal class FLFilterSpec
    {
        public FLFilters Filter;
        public int      Default;
        public float    Step;
        public int      Minimum;
        public float    MinimumPS;
        public int      Maximum;
        public float    MaximumPS;
    }
}

public class D3Filter : NotifyPropertyChanged
{   // NOTE: Serialization requires Public sets and constructor

    /// <summary>Always false on the portable build (no D3D11 video processor).</summary>
    [JsonIgnore]
    public bool         Available   => false;
    public VideoProcessorFilter
                        Filter      { get; set; }
    public int          Minimum     { get; set; }
    public int          Maximum     { get; set; }
    public float        Step        { get; set; }
    public int          Default
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                SetDefaultValue.OnCanExecuteChanged();
            }
        }
    }
    public int Value
    {
        get => _Value;
        set
        {
            int v = value;
            v = Math.Min(v, Maximum);
            v = Math.Max(v, Minimum);

            if (Set(ref _Value, v))
                SetDefaultValue.OnCanExecuteChanged();
        }
    }
    protected int _Value;

    [JsonIgnore]
    public RelayCommand SetDefaultValue => field ??= new(_ =>
    {
        Value = Default;
    }, _ => Value != Default);

    public D3Filter() { }
}
