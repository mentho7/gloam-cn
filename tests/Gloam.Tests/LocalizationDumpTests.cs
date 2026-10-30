using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Gloam.Localization;
using Xunit;

namespace Gloam.Tests
{
    /// <summary>
    /// Dumps every remaining hard-coded string with its file and line, for migration work.
    /// </summary>
    /// <remarks>
    /// The baseline fixture only stores counts, which is enough for the ratchet and useless when
    /// actually translating: you need the text and the line to replace it. Gated on an environment
    /// variable and asserting nothing, so it is inert in CI.
    /// </remarks>
    public sealed class LocalizationDumpTests
    {
        private static readonly JsonSerializerOptions DumpJson = new() { WriteIndented = true };

        [Fact]
        public void Dump_candidates()
        {
            string? output = Environment.GetEnvironmentVariable("GLOAM_DUMP_LOCALIZATION");
            if (string.IsNullOrEmpty(output))
                return;

            string root = FindRepoRoot();
            var rows = new List<object>();

            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                string extension = Path.GetExtension(file);
                if (extension is not (".xaml" or ".cs"))
                    continue;

                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                string text = File.ReadAllText(file);
                SourceAuditResult result = extension == ".xaml"
                    ? LocalizationSourceAudit.AuditXaml(text, relative)
                    : LocalizationSourceAudit.AuditCSharp(text, relative);

                rows.AddRange(result.Hardcoded.Select(item => new { file = item.File, line = item.Line, text = item.Text }));
            }

            File.WriteAllText(output, JsonSerializer.Serialize(rows, DumpJson));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
                dir = dir.Parent;

            return dir?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
        }
    }
}
