using System.Diagnostics;
using System.IO.Compression;

namespace OpenKaraoke.Core.Tools;

/// <summary>Extracts a single executable out of a downloaded tool archive.</summary>
public interface IToolArchiveExtractor
{
    /// <summary>
    /// Copies the file named <paramref name="memberFileName"/>, wherever it sits inside the
    /// archive, to <paramref name="destinationPath"/>.
    /// </summary>
    Task ExtractAsync(
        string archivePath,
        string memberFileName,
        string destinationPath,
        CancellationToken cancellationToken);
}

/// <summary>Chooses the extractor that matches this platform's archive format.</summary>
public static class ToolArchiveExtractor
{
    /// <summary>Windows builds ship as .zip, Linux builds as .tar.xz.</summary>
    public static IToolArchiveExtractor ForCurrentPlatform()
        => OperatingSystem.IsWindows() ? new ZipToolArchiveExtractor() : new TarToolArchiveExtractor();
}

/// <summary>Reads the .zip archives published for Windows.</summary>
public sealed class ZipToolArchiveExtractor : IToolArchiveExtractor
{
    public async Task ExtractAsync(
        string archivePath,
        string memberFileName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
            candidate => string.Equals(candidate.Name, memberFileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new ToolInstallException($"압축 파일에서 {memberFileName} 파일을 찾지 못했습니다.");
        }

        await using Stream source = entry.Open();
        await using FileStream target = File.Create(destinationPath);
        await source.CopyToAsync(target, cancellationToken);
    }
}

/// <summary>
/// Reads the .tar.xz archives published for Linux through the system <c>tar</c>, which every
/// desktop distribution ships, instead of bundling an xz decoder.
/// </summary>
public sealed class TarToolArchiveExtractor : IToolArchiveExtractor
{
    public async Task ExtractAsync(
        string archivePath,
        string memberFileName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string workDirectory = Path.Combine(
            Path.GetTempPath(),
            "open-karaoke-extract-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workDirectory);

        try
        {
            var startInfo = new ProcessStartInfo("tar")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            // --wildcards keeps the versioned root folder matchable, --strip-components drops it.
            startInfo.ArgumentList.Add("-xf");
            startInfo.ArgumentList.Add(archivePath);
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(workDirectory);
            startInfo.ArgumentList.Add("--wildcards");
            startInfo.ArgumentList.Add("--strip-components=1");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("*/bin/" + memberFileName);

            using Process process = Process.Start(startInfo)
                ?? throw new ToolInstallException("tar 명령을 실행하지 못했습니다.");

            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await output;
            string errorText = await error;

            if (process.ExitCode != 0)
            {
                throw new ToolInstallException(
                    errorText.Contains("xz", StringComparison.OrdinalIgnoreCase)
                        ? "압축 해제 도구(xz)를 찾지 못했습니다. 터미널에서 'sudo apt install xz-utils'를 실행한 뒤 다시 시도해 주세요."
                        : $"압축을 풀지 못했습니다 (tar 종료 코드 {process.ExitCode}): {errorText.Trim()}");
            }

            string extracted = Path.Combine(workDirectory, "bin", memberFileName);
            if (!File.Exists(extracted))
            {
                throw new ToolInstallException($"압축 파일에서 bin/{memberFileName} 파일을 찾지 못했습니다.");
            }

            File.Move(extracted, destinationPath, overwrite: true);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ToolInstallException(
                "tar 명령을 찾을 수 없습니다. 터미널에서 'sudo apt install tar xz-utils'를 실행한 뒤 다시 시도해 주세요.",
                ex);
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception)
        {
            // Temporary files only; a failure here must not hide the install result.
        }
    }
}
