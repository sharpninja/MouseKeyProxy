// Simple Nuke-like for the plan. Full Nuke would use Nuke.Common etc.
using System;
using System.IO;

class Build
{
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "PublishToolLocally")
        {
            PublishToolLocally();
        }
    }

    static void PublishToolLocally()
    {
        Console.WriteLine("Publishing tool locally...");
        // simulate dotnet pack to local feed
        var localFeed = "local-feed";
        Directory.CreateDirectory(localFeed);
        // in real, dotnet pack src/MouseKeyProxy.Repl -o local-feed
        File.WriteAllText(Path.Combine(localFeed, "MouseKeyProxy.Repl.0.5.0.nupkg"), "simulated nupkg");
        Console.WriteLine("Local nupkg created in " + localFeed);
        // test local install
        Console.WriteLine("dotnet tool install --global --add-source " + localFeed + " MouseKeyProxy.Repl");
        Console.WriteLine("mkp --help would run here");
    }
}
