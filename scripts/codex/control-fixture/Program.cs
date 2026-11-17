const string MissingCpu = "The test recognizer requires an explicit CPU device.";
int device = Array.IndexOf(args, "--device");
if (device < 0 || device + 1 >= args.Length || args[device + 1] != "cpu")
{
    Console.Error.WriteLine(MissingCpu);
    return 2;
}

using (FileStream audio = File.OpenRead(args[^1]))
{
    Span<byte> header = stackalloc byte[12];
    audio.ReadExactly(header);
    if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WAVE"u8))
    {
        Console.Error.WriteLine("The test recognizer did not receive a decoded WAV chunk.");
        return 2;
    }
}

Console.WriteLine("[00:00.100 --> 00:00.400] First fixture sentence.");
Console.WriteLine("[00:00.650 --> 00:00.950] Second fixture sentence.");
Console.WriteLine("Operation finished in: 0 seconds.");
return 0;
