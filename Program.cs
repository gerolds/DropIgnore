using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using CommandLine;
using MAB.DotIgnore;

namespace DropboxIgnore;

public class Program
{
    private const string IgnoreFileName = ".dropIgnore";

    [Verb("ignore", HelpText = "Ignore paths")]
    public class IgnoreOptions
    {
        [Option('f', "files", Required = true, HelpText = "The files to ignore")]
        public IEnumerable<string>? Files { get; set; }
    }

    public class Options
    {
        [Option('f', "files", Required = false, HelpText = "The files to ignore")]
        public IEnumerable<string>? Files { get; set; }
    }

    public static void Main(string[] args)
    {
        CommandLine.Parser
                   .Default
                   .ParseArguments<Options>(args)
                   .WithParsed<Options>(o =>
                   {
                       List<string>? files = o.Files?.ToList();
                       if (files?.Any() ?? false)
                       {
                           Console.WriteLine("Files:");
                           foreach (var file in o.Files)
                           {
                               Console.WriteLine($"{file}");
                           }
                       }
                       else
                       {
                           Console.WriteLine($"No files specified, looking for {IgnoreFileName}...");
                           UseIgnoreFile();
                       }
                   })
                   .WithNotParsed(errors =>
                   {
                       foreach (var error in errors)
                       {
                           Console.WriteLine(error);
                       }
                   });
    }


    private static bool UseIgnoreFile()
    {
        using Runspace runSpace = RunspaceFactory.CreateRunspace();
        runSpace.Open();
        string dir = Directory.GetCurrentDirectory();
        DirectoryInfo workingDirectory = new (dir);
        Console.WriteLine($"The current directory is {dir}");
        List<IgnoreList> ignoreList = new();
        Stopwatch sw = new Stopwatch();
        sw.Start();
        var b = ProcessDirectory(workingDirectory, runSpace, ignoreList);
        Console.WriteLine($"{sw.Elapsed} sek");
        return b;
    }

    private static bool ProcessDirectory(DirectoryInfo dir, Runspace runSpace, IList<IgnoreList> ignoreSet)
    {
        Console.WriteLine($"Processing {dir.FullName}");
        FileInfo localIgnoreFile = new FileInfo(Path.Combine(dir.FullName, IgnoreFileName));
        IgnoreList? localIgnore = null;
        if (localIgnoreFile.Exists)
        {
            Console.WriteLine($"{IgnoreFileName} found at {localIgnoreFile.FullName}");
            localIgnore = new IgnoreList(localIgnoreFile.FullName);
            Console.WriteLine($"Rules are:");
            foreach (var rule in localIgnore.Rules)
            {
                Console.WriteLine(rule.Pattern);
            }
            ignoreSet.Add(localIgnore);
        }
        
        foreach (var sub in dir.EnumerateDirectories())
        {
            ProcessDirectory(dir, runSpace, ignoreSet);
        }

        {
            IEnumerable<string> ignoredFiles =
                    dir
                        .EnumerateFiles()
                        .Where(file => ignoreSet.Any(ignoreList => ignoreList.IsIgnored(file)))
                        .Select(it => $@"'{it.FullName}'")
                        .ToList()
                ;
            
            Console.WriteLine($"Ignoring files");
            
            foreach (var file in ignoredFiles)
            {
                string ignoreScript = $"Set-Content -Path {file} -Stream com.dropbox.ignored -Value 1";
                Console.WriteLine(ignoreScript);
                var r = RunScript(ignoreScript, runSpace);
                if (!string.IsNullOrWhiteSpace(r))
                    Console.WriteLine(r);
            }

            IList<string> revokedFiles =
                    dir
                        .EnumerateFiles()
                        .Where(file => !ignoreSet.Any(ignoreList => ignoreList.IsIgnored(file)))
                        .Select(it => $@"'{it.FullName}'")
                        .ToList()
                ;

            Console.WriteLine($"Revoking files");
        
            foreach (var file in revokedFiles)
            {
                string s = $"Clear-Content -Path {file} -Stream com.dropbox.ignored";
                Console.WriteLine(s);
                var r = RunScript(s, runSpace);
                if (!string.IsNullOrWhiteSpace(r))
                    Console.WriteLine(r);
            }
        }
        
        
        
        
        
        
        
        
        
        
        if (localIgnore != null)
            ignoreSet.Remove(localIgnore);

        
        return true;
    }

    static string RunScript(string scriptText, Runspace runspace)
    {
        Pipeline pipeline = runspace.CreatePipeline();
        pipeline.Commands.AddScript(scriptText);
        pipeline.Commands.Add("Out-String");
        Collection<PSObject> results = pipeline.Invoke();
        
        StringBuilder stringBuilder = new StringBuilder();
        foreach (PSObject obj in results)
        {
            stringBuilder.AppendLine(obj.ToString());
        }
        return stringBuilder.ToString();
    }
}