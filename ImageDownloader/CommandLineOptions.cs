using CommandLine;

namespace ImageDownloader;

internal class CommandLineOptions
{
    [Value(0)]
    public string? Path { get; set; } = null;
    
    [Option(shortName: 'v', longName: "verbose", Required = false, HelpText = "Verbose")]
    public bool Verbose { get; set; }
}
