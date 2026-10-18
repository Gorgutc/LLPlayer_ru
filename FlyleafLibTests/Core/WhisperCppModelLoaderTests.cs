using FlyleafLib;
using LLPlayer.Core.Services;
using Whisper.net.Ggml;

namespace FlyleafLibTests.Core;

public class WhisperCppModelLoaderTests
{
    private static readonly QuantizationType[] Quantizations =
        [QuantizationType.NoQuantization, QuantizationType.Q5_0, QuantizationType.Q5_1, QuantizationType.Q8_0];

    [Fact]
    public void Catalog_preserves_enum_groups_quantizations_default_and_paths()
    {
        bool ensured = false;
        var paths = new List<string>();
        var rows = WhisperCppModelLoader.LoadAllModels(() => ensured = true, path =>
        {
            Assert.True(ensured);
            paths.Add(path);
            return null;
        });
        var expected = Enum.GetValues<GgmlType>().SelectMany(model => Quantizations.Select(quant => (model, quant))).ToArray();
        Assert.Equal(expected, rows.Select(row => (row.Model, row.Quantization)));
        Assert.Equal(Enum.GetValues<GgmlType>()[0], rows[0].Model);
        Assert.Equal(QuantizationType.NoQuantization, rows[0].Quantization);
        Assert.Equal(rows.Select(row => row.ModelFilePath), paths);
        Assert.All(rows, row => Assert.False(row.Downloaded));
        Assert.Equal("ggml-base-q5_1.bin", rows.Single(row => row.Model == GgmlType.Base && row.Quantization == QuantizationType.Q5_1).ModelFileName);
    }

    [Fact]
    public void Missing_zero_and_positive_lengths_control_downloaded_filter()
    {
        int probe = 0;
        long? Length(string _) => probe++ switch { 0 => null, 1 => 0, 2 => 123, _ => null };
        var rows = WhisperCppModelLoader.LoadAllModels(() => { }, Length);
        Assert.Equal(new long[] { 0, 0, 123 }, rows.Take(3).Select(row => row.Size));
        Assert.False(rows[0].Downloaded);
        Assert.False(rows[1].Downloaded);
        Assert.True(rows[2].Downloaded);
        probe = 0;
        var downloaded = WhisperCppModelLoader.LoadDownloadedModels(() => { }, Length);
        var only = Assert.Single(downloaded);
        Assert.Equal(rows[2].Model, only.Model);
        Assert.Equal(rows[2].Quantization, only.Quantization);
        Assert.Equal(123, only.Size);
    }

    [Fact]
    public void Every_call_ensures_directory_and_returns_fresh_rows_and_status()
    {
        int ensured = 0;
        var first = WhisperCppModelLoader.LoadAllModels(() => ensured++, _ => 42);
        first[0].Size = 999;
        var second = WhisperCppModelLoader.LoadAllModels(() => ensured++, _ => null);
        Assert.Equal(2, ensured);
        Assert.NotSame(first, second);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.NotSame(first[i], second[i]);
            Assert.Equal(0, second[i].Size);
        }
    }

    [Fact]
    public void Directory_failure_propagates_before_any_probe()
    {
        var failure = new IOException("directory denied");
        bool probed = false;
        var thrown = Assert.Throws<IOException>(() => WhisperCppModelLoader.LoadAllModels(
            () => throw failure, _ => { probed = true; return 0; }));
        Assert.Same(failure, thrown);
        Assert.False(probed);
    }

    [Fact]
    public void File_probe_failure_propagates()
    {
        var failure = new IOException("file unavailable");
        var thrown = Assert.Throws<IOException>(() => WhisperCppModelLoader.LoadDownloadedModels(
            () => { }, _ => throw failure));
        Assert.Same(failure, thrown);
    }

}
