using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

using CommandLine;

namespace ImageDownloader;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var (urlFilesPath, verbose) = HandleArgs(args);
        if (urlFilesPath == null)
        {
            Console.WriteLine("No PATH provided!");
            return;
        }

        var appDatDir = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "image-downloader");

        var urlFiles = HandleUrlFiles(urlFilesPath);
        MaybeCreateApplicationData(appDatDir, verbose);

        var client = new HttpClient();
        var tasks = new List<Task>();
        foreach (var (urlFile, _) in urlFiles)
        {
            var enumerator = CreateUrlFileReader(urlFile);
            while (enumerator.MoveNext())
            {
                // batch this
                tasks.Add(GetImageAsync(client, enumerator.Current)
                    .ContinueWith(task =>
                        {
                            if (task.IsFaulted)
                                return;
                            
                            var result = task.Result;
                            if (result.Item2 == null)
                                return;
                            Directory.CreateDirectory(Path.Join(appDatDir, new DirectoryInfo(urlFile).Parent.ToString()));
                            Save(result.Item1, result.Item2,
                                Path.Join(appDatDir, Path.GetFileNameWithoutExtension(urlFilesPath)));
                        }
                    )
                );
            }
        }
        await Task.Factory.StartNew(() => Task.WaitAll(tasks.ToArray()), TaskCreationOptions.LongRunning);
    }

    private static readonly ConcurrentDictionary<string, byte> ResponseResultBlacklist =
        new(new List<KeyValuePair<string, byte>>
        {
            new("https://assets.tumblr.com/images/media_violation/community_guidelines_v1_500.png", 0),
            new("https://i.imgur.com/removed.png", 0),
        });

    private static (string? path, bool verbose) HandleArgs(IEnumerable<string> args)
    {
        string? path = null;
        var verbose = false;

        Parser.Default
            .ParseArguments<CommandLineOptions>(args)
            .WithParsed(options =>
            {
                path = options.Path;
                verbose = options.Verbose;
            });
        return (path, verbose);
    }

    private static void MaybeCreateApplicationData(string path, bool verbose = false)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        if (verbose)
        {
            Console.WriteLine(path + " created");
        }
    }

    private static ConcurrentDictionary<string, byte> HandleUrlFiles(string urlFilesDirectory)
    {
        var urlFiles = Directory.EnumerateFiles(urlFilesDirectory, "*.txt", SearchOption.AllDirectories).ToHashSet();
        var urlFilesFiltered = new ConcurrentDictionary<string, byte>();

        var skipRest = false;
        foreach (var file in urlFiles)
        {
            if (!skipRest)
            {
                Console.Write("Remove file " + Path.GetFileName(file) + "?: [y,n,s]");
                var userInput = Console.ReadLine();
                if (userInput == null)
                {
                    throw new Exception("Received null input!");
                }
                if (userInput.StartsWith("y", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                if (userInput.StartsWith("s", StringComparison.InvariantCultureIgnoreCase))
                {
                    skipRest = true;
                }
            }
            urlFilesFiltered.TryAdd(file, 0);
        }
        return urlFilesFiltered;
    }

    private static IEnumerator<string> CreateUrlFileReader(string file)
    {
        using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read);
        using var streamReader = new StreamReader(fileStream);

        while (streamReader.ReadLine() is { } url)
        {
            yield return url;
        }
    }

    private static async Task<(string, MemoryStream?)> GetImageAsync(HttpClient client, string url)
    {
        var result = await client.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                url)
        );

        if (result.StatusCode != HttpStatusCode.OK)
            return (string.Empty, null);
        if (result.RequestMessage?.RequestUri?.ToString() != null || ResponseResultBlacklist.ContainsKey(result.RequestMessage?.RequestUri?.ToString()))
            return (string.Empty, null);

        return (url, new MemoryStream(await result.Content.ReadAsByteArrayAsync()));
    }

    private static void Save(string filename, Stream imageBuffer, string directory, bool verbose = false)
    {
        var image = Image.FromStream(imageBuffer);
        imageBuffer.Close();

        var folderPath = Path.Join(directory, Path.GetFileName(new DirectoryInfo(filename).Parent?.ToString()));

        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        var filePath = Path.Join(folderPath,
            image.GetHashCode() + Path.GetExtension(filename));

        image.Save(filePath);
        image.Dispose();

        if (verbose)
            Console.WriteLine($"Saved an image to: {folderPath}");
    }
}