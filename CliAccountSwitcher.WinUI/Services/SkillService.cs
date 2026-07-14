using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using System.IO.Compression;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class SkillService
{
    public static string GetSkillsDirectoryPath(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => Constants.ClaudeCodeSkillsDirectory, CliProviderKind.OpenCodeGo => Constants.OpenCodeGoSkillsDirectory, _ => Constants.CodexSkillsDirectory  };

    public static string GetBackupFileNamePrefix(CliProviderKind providerKind) => providerKind switch { CliProviderKind.ClaudeCode => "claude-skills", CliProviderKind.OpenCodeGo => "opencode-skills", _ => "codex-skills"  };

    public IReadOnlyList<SkillItem> ScanSkills(CliProviderKind providerKind)
    {
        var skillsDirectoryPath = Path.GetFullPath(GetSkillsDirectoryPath(providerKind));
        if (!Directory.Exists(skillsDirectoryPath)) return [];

        var skillItems = new List<SkillItem>();
        ScanSkillDirectoriesRecursive(providerKind, skillsDirectoryPath, skillsDirectoryPath, skillItems);
        return skillItems.OrderBy(skillItem => skillItem.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task ExportSkillsAsync(CliProviderKind providerKind, IReadOnlyList<SkillItem> skillItems, string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);

        var skillsDirectoryPath = Path.GetFullPath(GetSkillsDirectoryPath(providerKind));
        using var zipArchive = ZipFile.Open(backupFilePath, ZipArchiveMode.Create);

        foreach (var skillDirectoryPath in GetSafeSkillDirectoryPaths(skillsDirectoryPath, skillItems))
        {
            if (!Directory.Exists(skillDirectoryPath)) continue;

            AddSkillDirectoryZipEntries(zipArchive, skillsDirectoryPath, skillDirectoryPath, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task<int> ImportSkillsAsync(CliProviderKind providerKind, string backupFilePath, CancellationToken cancellationToken = default)
    {
        var skillsDirectoryPath = Path.GetFullPath(GetSkillsDirectoryPath(providerKind));
        Directory.CreateDirectory(skillsDirectoryPath);
        var importedSkillDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var zipArchive = ZipFile.OpenRead(backupFilePath);

        foreach (var zipArchiveEntry in zipArchive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetSafeZipEntrySegments(zipArchiveEntry.FullName, out var zipEntrySegments)) continue;

            var isDirectoryEntry = IsZipDirectoryEntry(zipArchiveEntry.FullName);
            if (!isDirectoryEntry && zipEntrySegments.Length < 2) continue;

            var targetFilePath = Path.GetFullPath(CombinePathSegments(skillsDirectoryPath, zipEntrySegments));
            if (!IsPathWithinDirectory(skillsDirectoryPath, targetFilePath)) continue;

            if (isDirectoryEntry)
            {
                Directory.CreateDirectory(targetFilePath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            ClearReadOnlyAttributeIfExists(targetFilePath);
            zipArchiveEntry.ExtractToFile(targetFilePath, overwrite: true);

            if (string.Equals(zipEntrySegments[^1], "SKILL.md", StringComparison.OrdinalIgnoreCase))
            {
                var skillDirectoryName = string.Join("/", zipEntrySegments, 0, zipEntrySegments.Length - 1);
                importedSkillDirectoryNames.Add(skillDirectoryName);
            }
        }

        return Task.FromResult(importedSkillDirectoryNames.Count);
    }

    public void DeleteSkills(CliProviderKind providerKind, IReadOnlyList<SkillItem> skillItems)
    {
        var skillsDirectoryPath = Path.GetFullPath(GetSkillsDirectoryPath(providerKind));
        if (!Directory.Exists(skillsDirectoryPath)) return;

        foreach (var skillDirectoryName in GetSafeSkillDirectoryNames(skillItems))
        {
            var skillDirectoryPath = ResolveSkillDirectoryPath(skillsDirectoryPath, skillDirectoryName);
            if (skillDirectoryPath is null) continue;
            if (Directory.Exists(skillDirectoryPath)) DeleteDirectory(skillDirectoryPath);
        }
    }

    private static void ScanSkillDirectoriesRecursive(CliProviderKind providerKind, string skillsRootPath, string currentDirectoryPath, List<SkillItem> skillItems)
    {
        foreach (var childDirectoryPath in Directory.EnumerateDirectories(currentDirectoryPath))
        {
            var candidateSkillFilePath = Path.Combine(childDirectoryPath, "SKILL.md");
            if (File.Exists(candidateSkillFilePath))
            {
                if (TryCreateSkillItem(providerKind, skillsRootPath, childDirectoryPath, out var skillItem))
                {
                    skillItems.Add(skillItem);
                }
            }
            else ScanSkillDirectoriesRecursive(providerKind, skillsRootPath, childDirectoryPath, skillItems);
        }
    }

    private static bool TryCreateSkillItem(CliProviderKind providerKind, string skillsRootPath, string skillDirectoryPath, out SkillItem skillItem)
    {
        skillItem = null;

        try
        {
            var fullSkillDirectoryPath = Path.GetFullPath(skillDirectoryPath);
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(skillsRootPath, fullSkillDirectoryPath));
            if (!IsSafeRelativePath(relativePath)) return false;

            var skillFilePath = Path.Combine(fullSkillDirectoryPath, "SKILL.md");
            var (name, description) = ReadSkillFrontMatter(skillFilePath, Path.GetFileName(fullSkillDirectoryPath));
            var filePaths = Directory.EnumerateFiles(fullSkillDirectoryPath, "*", SearchOption.AllDirectories).ToArray();

            skillItem = new SkillItem
            {
                ProviderKind = providerKind,
                Name = name,
                DirectoryName = relativePath,
                FullPath = fullSkillDirectoryPath,
                Description = description,
                FileCount = filePaths.Length,
                LastModified = GetLastModified(fullSkillDirectoryPath, filePaths)
            };
            return true;
        }
        catch { return false; }
    }

    private static (string name, string description) ReadSkillFrontMatter(string skillFilePath, string fallbackName)
    {
        try
        {
            if (!File.Exists(skillFilePath)) return (fallbackName, "");

            var skillFileContent = File.ReadAllText(skillFilePath).Replace("\r\n", "\n").Replace('\r', '\n');
            var skillFileLines = skillFileContent.Split('\n');
            var frontMatterStartLineIndex = Array.FindIndex(skillFileLines, skillFileLine => !string.IsNullOrWhiteSpace(skillFileLine));
            if (frontMatterStartLineIndex < 0 || !string.Equals(skillFileLines[frontMatterStartLineIndex].Trim(), "---", StringComparison.Ordinal)) return (fallbackName, "");

            var hasFrontMatterEnd = false;
            var name = fallbackName;
            var description = "";

            for (var lineIndex = frontMatterStartLineIndex + 1; lineIndex < skillFileLines.Length; lineIndex++)
            {
                var trimmedLine = skillFileLines[lineIndex].Trim();
                if (string.Equals(trimmedLine, "---", StringComparison.Ordinal))
                {
                    hasFrontMatterEnd = true;
                    break;
                }

                var separatorIndex = trimmedLine.IndexOf(':');
                if (separatorIndex <= 0) continue;

                var key = trimmedLine[..separatorIndex].Trim();
                var value = UnquoteFrontMatterValue(trimmedLine[(separatorIndex + 1)..].Trim());
                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value)) name = value;
                else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value)) description = value;
            }

            return hasFrontMatterEnd ? (name, description) : (fallbackName, "");
        }
        catch { return (fallbackName, ""); }
    }

    private static DateTime GetLastModified(string skillDirectoryPath, IReadOnlyList<string> filePaths)
    {
        var lastModified = Directory.GetLastWriteTime(skillDirectoryPath);
        foreach (var filePath in filePaths)
        {
            var fileLastModified = File.GetLastWriteTime(filePath);
            if (fileLastModified > lastModified) lastModified = fileLastModified;
        }
        return lastModified;
    }

    private static IEnumerable<string> GetSafeSkillDirectoryNames(IEnumerable<SkillItem> skillItems)
    {
        var skillDirectoryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skillItem in skillItems)
        {
            if (!IsSafeRelativePath(skillItem.DirectoryName)) continue;
            if (skillDirectoryNames.Add(skillItem.DirectoryName)) yield return skillItem.DirectoryName;
        }
    }

    private static IEnumerable<string> GetSafeSkillDirectoryPaths(string skillsDirectoryPath, IEnumerable<SkillItem> skillItems)
    {
        var skillDirectoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skillItem in skillItems)
        {
            var skillDirectoryPath = ResolveSkillDirectoryPath(skillsDirectoryPath, skillItem);
            if (skillDirectoryPath is null) continue;
            if (skillDirectoryPaths.Add(skillDirectoryPath)) yield return skillDirectoryPath;
        }
    }

    private static void AddSkillDirectoryZipEntries(ZipArchive zipArchive, string skillsDirectoryPath, string skillDirectoryPath, CancellationToken cancellationToken)
    {
        var skillDirectoryEntryPath = NormalizeZipEntryPath(Path.GetRelativePath(skillsDirectoryPath, skillDirectoryPath)).TrimEnd('/') + "/";
        zipArchive.CreateEntry(skillDirectoryEntryPath);

        foreach (var directoryPath in Directory.EnumerateDirectories(skillDirectoryPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directoryEntryPath = NormalizeZipEntryPath(Path.GetRelativePath(skillsDirectoryPath, directoryPath)).TrimEnd('/') + "/";
            zipArchive.CreateEntry(directoryEntryPath);
        }

        foreach (var filePath in Directory.EnumerateFiles(skillDirectoryPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeZipEntryPath(Path.GetRelativePath(skillsDirectoryPath, filePath));
            zipArchive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
        }
    }

    private static string UnquoteFrontMatterValue(string value)
    {
        if (value.Length < 2) return value;
        if (value[0] == '"' && value[^1] == '"') return value[1..^1].Trim();
        if (value[0] == '\'' && value[^1] == '\'') return value[1..^1].Trim();
        return value;
    }

    private static bool TryGetSafeZipEntrySegments(string zipEntryFullName, out string[] zipEntrySegments)
    {
        var normalizedZipEntryFullName = zipEntryFullName.Replace('\\', '/').Trim('/');
        zipEntrySegments = [];
        if (string.IsNullOrWhiteSpace(normalizedZipEntryFullName)) return false;

        var segments = normalizedZipEntryFullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return false;
        if (segments.Any(segment => !IsSafePathSegment(segment))) return false;

        zipEntrySegments = segments;
        return true;
    }

    private static bool IsZipDirectoryEntry(string zipEntryFullName) => zipEntryFullName.EndsWith("/", StringComparison.Ordinal) || zipEntryFullName.EndsWith("\\", StringComparison.Ordinal);

    private static string NormalizeZipEntryPath(string zipEntryPath) => zipEntryPath.Replace('\\', '/');

    private static string NormalizeRelativePath(string relativePath) => relativePath.Replace('\\', '/');

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        foreach (var segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") return false;
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        }

        return true;
    }

    private static bool IsSafePathSegment(string pathSegment) => !string.IsNullOrWhiteSpace(pathSegment) && pathSegment is not "." and not ".." && pathSegment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string ResolveSkillDirectoryPath(string skillsDirectoryPath, string skillDirectoryName)
    {
        var skillDirectoryPath = Path.GetFullPath(Path.Combine(skillsDirectoryPath, skillDirectoryName));
        return IsPathWithinDirectory(skillsDirectoryPath, skillDirectoryPath) ? skillDirectoryPath : null;
    }

    private static string ResolveSkillDirectoryPath(string skillsDirectoryPath, SkillItem skillItem)
    {
        if (!string.IsNullOrWhiteSpace(skillItem.FullPath))
        {
            var skillDirectoryPath = Path.GetFullPath(skillItem.FullPath);
            if (IsPathWithinDirectory(skillsDirectoryPath, skillDirectoryPath)) return skillDirectoryPath;
        }

        if (!IsSafeRelativePath(skillItem.DirectoryName)) return null;
        return ResolveSkillDirectoryPath(skillsDirectoryPath, skillItem.DirectoryName);
    }

    private static string CombinePathSegments(string rootPath, IReadOnlyList<string> pathSegments)
    {
        var combinedPath = rootPath;
        foreach (var pathSegment in pathSegments) combinedPath = Path.Combine(combinedPath, pathSegment);
        return combinedPath;
    }

    private static bool IsPathWithinDirectory(string directoryPath, string targetPath)
    {
        var fullDirectoryPath = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullTargetPath = Path.GetFullPath(targetPath);
        return fullTargetPath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteDirectory(string directoryPath)
    {
        ClearReadOnlyAttributes(directoryPath);
        Directory.Delete(directoryPath, recursive: true);
    }

    private static void ClearReadOnlyAttributes(string directoryPath)
    {
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories)) ClearReadOnlyAttributeIfExists(filePath);
        foreach (var childDirectoryPath in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories)) ClearReadOnlyAttributeIfExists(childDirectoryPath);
        ClearReadOnlyAttributeIfExists(directoryPath);
    }

    private static void ClearReadOnlyAttributeIfExists(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) == 0) return;
        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
