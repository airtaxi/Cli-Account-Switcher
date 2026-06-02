using CliAccountSwitcher.Api.Providers.Abstractions;
using CliAccountSwitcher.WinUI.Models;
using System.IO.Compression;

namespace CliAccountSwitcher.WinUI.Services;

public sealed class SkillService
{
    public static string GetSkillsDirectoryPath(CliProviderKind providerKind) => providerKind switch
    {
        CliProviderKind.ClaudeCode => Constants.ClaudeCodeSkillsDirectory,
        _ => Constants.CodexSkillsDirectory
    };

    public static string GetBackupFileNamePrefix(CliProviderKind providerKind) => providerKind switch
    {
        CliProviderKind.ClaudeCode => "claude-skills",
        _ => "codex-skills"
    };

    public IReadOnlyList<SkillItem> ScanSkills(CliProviderKind providerKind)
    {
        var skillsDirectoryPath = GetSkillsDirectoryPath(providerKind);
        if (!Directory.Exists(skillsDirectoryPath)) return [];

        var skillItems = new List<SkillItem>();

        foreach (var skillDirectoryPath in Directory.EnumerateDirectories(skillsDirectoryPath))
        {
            var skillDirectoryName = Path.GetFileName(skillDirectoryPath);
            if (string.IsNullOrWhiteSpace(skillDirectoryName)) continue;

            var skillFilePath = Path.Combine(skillDirectoryPath, "SKILL.md");
            var (name, description) = ReadSkillFrontMatter(skillFilePath, skillDirectoryName);

            var fileCount = Directory.EnumerateFiles(skillDirectoryPath, "*", SearchOption.AllDirectories).Count();
            var lastModified = Directory.GetLastWriteTime(skillDirectoryPath);

            skillItems.Add(new SkillItem
            {
                Name = name,
                DirectoryName = skillDirectoryName,
                FullPath = skillDirectoryPath,
                Description = description,
                FileCount = fileCount,
                LastModified = lastModified
            });
        }

        return skillItems.OrderBy(skillItem => skillItem.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task ExportSkillsAsync(CliProviderKind providerKind, IReadOnlyList<SkillItem> skillItems, string backupFilePath, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(backupFilePath) ?? Constants.BackupsDirectory);
        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);

        using var zipArchive = ZipFile.Open(backupFilePath, ZipArchiveMode.Create);

        foreach (var skillItem in skillItems)
        {
            var skillDirectoryPath = skillItem.FullPath;
            if (!Directory.Exists(skillDirectoryPath)) continue;

            var skillsDirectoryPath = GetSkillsDirectoryPath(providerKind);
            var relativeParent = skillsDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (var filePath in Directory.EnumerateFiles(skillDirectoryPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(relativeParent, filePath);
                zipArchive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
            }
        }
    }

    public async Task<int> ImportSkillsAsync(CliProviderKind providerKind, string backupFilePath, CancellationToken cancellationToken = default)
    {
        var skillsDirectoryPath = GetSkillsDirectoryPath(providerKind);
        Directory.CreateDirectory(skillsDirectoryPath);
        var importedCount = 0;

        using var zipArchive = ZipFile.OpenRead(backupFilePath);

        foreach (var zipArchiveEntry in zipArchive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullName = zipArchiveEntry.FullName;
            if (string.IsNullOrWhiteSpace(fullName)) continue;

            // Determine the skill directory name from the first segment of the path
            var firstSeparatorIndex = fullName.IndexOf('/');
            var skillDirectoryName = firstSeparatorIndex < 0 ? fullName : fullName[..firstSeparatorIndex];
            if (string.IsNullOrWhiteSpace(skillDirectoryName)) continue;

            var targetDirectoryPath = Path.Combine(skillsDirectoryPath, skillDirectoryName);
            var targetFilePath = Path.GetFullPath(Path.Combine(targetDirectoryPath, firstSeparatorIndex < 0 ? "" : fullName[(firstSeparatorIndex + 1)..]));

            // Ensure the target file path is within the skills directory (prevent path traversal)
            if (!targetFilePath.StartsWith(Path.GetFullPath(skillsDirectoryPath), StringComparison.OrdinalIgnoreCase)) continue;

            if (zipArchiveEntry.Length == 0 && string.IsNullOrEmpty(Path.GetFileName(targetFilePath)))
            {
                // Directory entry
                Directory.CreateDirectory(targetFilePath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
            zipArchiveEntry.ExtractToFile(targetFilePath, overwrite: true);
        }

        // Count imported skill directories
        if (Directory.Exists(skillsDirectoryPath))
        {
            var existingDirectoryNames = new HashSet<string>(Directory.EnumerateDirectories(skillsDirectoryPath).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            foreach (var zipArchiveEntry in zipArchive.Entries)
            {
                var fullName = zipArchiveEntry.FullName;
                var firstSeparatorIndex = fullName.IndexOf('/');
                var skillDirectoryName = firstSeparatorIndex < 0 ? fullName : fullName[..firstSeparatorIndex];
                if (!string.IsNullOrWhiteSpace(skillDirectoryName) && existingDirectoryNames.Contains(skillDirectoryName))
                {
                    importedCount++;
                    existingDirectoryNames.Remove(skillDirectoryName);
                }
            }
        }

        return importedCount;
    }

    public void DeleteSkills(IReadOnlyList<SkillItem> skillItems)
    {
        foreach (var skillItem in skillItems)
        {
            var skillDirectoryPath = skillItem.FullPath;
            if (Directory.Exists(skillDirectoryPath)) Directory.Delete(skillDirectoryPath, recursive: true);
        }
    }

    private static (string name, string description) ReadSkillFrontMatter(string skillFilePath, string fallbackName)
    {
        try
        {
            if (!File.Exists(skillFilePath)) return (fallbackName, "");

            var skillFileContent = File.ReadAllText(skillFilePath);

            // Parse YAML-like frontmatter between --- markers
            var content = skillFileContent.TrimStart();
            if (!content.StartsWith("---", StringComparison.Ordinal)) return (fallbackName, "");

            var endOfFrontMatterIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
            if (endOfFrontMatterIndex < 0) return (fallbackName, "");

            var frontMatterText = content[3..endOfFrontMatterIndex].Trim();
            if (string.IsNullOrWhiteSpace(frontMatterText)) return (fallbackName, "");

            var name = fallbackName;
            var description = "";

            foreach (var line in frontMatterText.Split('\n'))
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    var nameValue = trimmedLine["name:".Length..].Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(nameValue)) name = nameValue;
                }
                else if (trimmedLine.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                {
                    var descriptionValue = trimmedLine["description:".Length..].Trim().Trim('"', '\'');
                    if (!string.IsNullOrWhiteSpace(descriptionValue)) description = descriptionValue;
                }
            }

            return (name, description);
        }
        catch
        {
            return (fallbackName, "");
        }
    }
}
