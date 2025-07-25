using System.IO.Abstractions;
using System.Text.RegularExpressions;
using GitVersion.Helpers;
using Microsoft.Build.Framework;

using static GitVersion.Core.RegexPatterns.AssemblyVersion;

namespace GitVersion.MsBuild;

internal static class AssemblyInfoFileHelper
{
    public static readonly string TempPath = MakeAndGetTempPath();

    private static string MakeAndGetTempPath() => FileSystemHelper.Path.Combine(FileSystemHelper.Path.GetTempPath(), "GitVersionTask");

    public static string GetFileExtension(string language) => language switch
    {
        "C#" => "cs",
        "F#" => "fs",
        "VB" => "vb",
        _ => throw new ArgumentException($"Unknown language detected: '{language}'")
    };

    public static string GetProjectExtension(string language) => language switch
    {
        "C#" => "csproj",
        "F#" => "fsproj",
        "VB" => "vbproj",
        _ => throw new ArgumentException($"Unknown language detected: '{language}'")
    };

    public static void CheckForInvalidFiles(IFileSystem fileSystem, IEnumerable<ITaskItem> compileFiles, string projectFile)
    {
        var invalidCompileFile = GetInvalidFiles(fileSystem, compileFiles, projectFile).FirstOrDefault();
        if (invalidCompileFile != null)
        {
            throw new WarningException("File contains assembly version attributes which conflict with the attributes generated by GitVersion " + invalidCompileFile);
        }
    }

    private static bool FileContainsVersionAttribute(IFileSystem fileSystem, string compileFile, string projectFile)
    {
        var compileFileExtension = FileSystemHelper.Path.GetExtension(compileFile);

        var (attributeRegex, triviaRegex) = compileFileExtension switch
        {
            ".cs" => (CSharp.AttributeRegex(), CSharp.TriviaRegex()),
            ".fs" => (FSharp.AttributeRegex(), FSharp.TriviaRegex()),
            ".vb" => (VisualBasic.AttributeRegex(), VisualBasic.TriviaRegex()),
            _ => throw new WarningException("File with name containing AssemblyInfo could not be checked for assembly version attributes which conflict with the attributes generated by GitVersion " + compileFile)
        };

        return FileContainsVersionAttribute(fileSystem, compileFile, projectFile, attributeRegex, triviaRegex);
    }

    private static bool FileContainsVersionAttribute(IFileSystem fileSystem, string compileFile, string projectFile, Regex attributeRegex, Regex triviaRegex)
    {
        var combine = FileSystemHelper.Path.Combine(FileSystemHelper.Path.GetDirectoryName(projectFile), compileFile);
        var allText = fileSystem.File.ReadAllText(combine);
        allText += FileSystemHelper.Path.NewLine; // Always add a new line, this handles the case for when a file ends with the EOF marker and no new line.

        var noCommentsOrStrings = triviaRegex.Replace(allText, me => me.Value.StartsWith("//") || me.Value.StartsWith('\'') ? FileSystemHelper.Path.NewLine : string.Empty);
        return attributeRegex.IsMatch(noCommentsOrStrings);
    }

    private static IEnumerable<string> GetInvalidFiles(IFileSystem fileSystem, IEnumerable<ITaskItem> compileFiles, string projectFile)
        => compileFiles.Select(x => x.ItemSpec)
            .Where(compileFile => compileFile.Contains("AssemblyInfo"))
            .Where(filePath => FileContainsVersionAttribute(fileSystem, filePath, projectFile));

    public static FileWriteInfo GetFileWriteInfo(this string? intermediateOutputPath, string language, string projectFile, string outputFileName)
    {
        var fileExtension = GetFileExtension(language);
        string workingDirectory, fileName;

        if (intermediateOutputPath == null)
        {
            fileName = $"{outputFileName}_{FileSystemHelper.Path.GetFileNameWithoutExtension(projectFile)}_{FileSystemHelper.Path.GetRandomFileName()}.g.{fileExtension}";
            workingDirectory = TempPath;
        }
        else
        {
            fileName = $"{outputFileName}.g.{fileExtension}";
            workingDirectory = intermediateOutputPath;
        }

        return new FileWriteInfo(workingDirectory, fileName, fileExtension);
    }
}
