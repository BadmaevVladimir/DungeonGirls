using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MacBuildPipeline
{
    const string Menu = "Dungeon Girls/Build/";
    const string PackageName = "DungeonGirls_Mac";
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static string Output => Path.Combine(Root, "Builds", "macOS");

    [MenuItem(Menu + "macOS Universal")]
    public static void Build()
    {
        try { BuildInternal(); }
        catch (Exception e)
        {
            string message = "macOS build FAILED: " + e.Message;
            Debug.LogError(message + "\n" + e);
            if (Application.isBatchMode) throw new BuildFailedException(message);
            EditorUtility.DisplayDialog("Dungeon Girls — macOS", message, "OK");
        }
    }

    [MenuItem(Menu + "macOS Universal", true)]
    static bool CanBuild() => !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode
                              && !BuildPipeline.isBuildingPlayer;

    [MenuItem(Menu + "Open macOS Build Folder")]
    public static void OpenBuildFolder()
    {
        Directory.CreateDirectory(Output);
        string package = Path.Combine(Output, PackageName);
        EditorUtility.RevealInFinder(Directory.Exists(package) ? package : Output);
    }

    static void BuildInternal()
    {
        if (!CanBuild()) throw new BuildFailedException("Stop Play Mode and wait for script compilation before building.");
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
            throw new BuildFailedException("macOS Build Support is missing. Unity Hub > Installs > " +
                Application.unityVersion + " > Add modules > Mac Build Support (Mono). Install it and restart Unity.");
        string tar = Application.platform == RuntimePlatform.WindowsEditor
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe") : "/usr/bin/tar";
        if (!Run(tar, "--version", Root, 10000).Contains("bsdtar"))
            throw new BuildFailedException("Packaging requires libarchive bsdtar (included with current Windows 10/11). Update Windows to install tar.exe.");

        var profile = BuildProfile.GetActiveBuildProfile();
        var scenes = (profile != null ? profile.GetScenesForBuild() : EditorBuildSettings.scenes)
            .Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) throw new BuildFailedException("No enabled scenes. Add scenes in File > Build Profiles > Scene List.");
        foreach (string scene in scenes)
            if (!scene.StartsWith("Assets/", StringComparison.Ordinal) || !scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                throw new BuildFailedException("Enabled scene is missing or invalid: " + scene);
        if (scenes.Distinct().Count() != scenes.Length) throw new BuildFailedException("Scene List contains duplicate scenes.");
        if (string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion)) throw new BuildFailedException("Set Player Settings > Version before building.");
        if (string.IsNullOrWhiteSpace(PlayerSettings.productName)) throw new BuildFailedException("Player Settings > Product Name is empty.");
        if (!Application.isBatchMode && !UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            throw new BuildFailedException("Build cancelled while saving scenes.");

        Directory.CreateDirectory(Output);
        // A unique staging directory prevents stale files entering a build and preserves the last successful package on failure.
        string staging = Path.Combine(Output, ".staging-" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(staging, PackageName);
        Directory.CreateDirectory(package);
        string app = Path.Combine(package, "DungeonGirls.app");
        string version = PlayerSettings.bundleVersion;
        string suffix = "";
        try { string hash = Run("git", "rev-parse --short HEAD", Root, 5000).Trim(); if (Regex.IsMatch(hash, "^[0-9a-f]+$")) suffix = "_" + hash; }
        catch { /* Git is optional. */ }
        string zipName = PackageName + "_" + Regex.Replace(version, "[^a-zA-Z0-9._-]", "_") + suffix + ".zip";
        var target = NamedBuildTarget.Standalone;
        // Universal is 2 in Unity 6000.5; StandaloneOSXUniversal is obsolete and removed.
        int oldArchitecture = PlayerSettings.GetArchitecture(target);
        var oldBackend = PlayerSettings.GetScriptingBackend(target);
        bool development = EditorUserBuildSettings.development;
        bool debugging = EditorUserBuildSettings.allowDebugging;
        bool profiler = EditorUserBuildSettings.connectProfiler;
        bool deep = EditorUserBuildSettings.buildWithDeepProfilingSupport;
        try
        {
            PlayerSettings.SetArchitecture(target, 2);
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = false;
            Debug.Log("macOS Universal / Mono / Release. Scenes: " + string.Join(", ", scenes));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = app, target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone, subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None
            });
            if (report == null || report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Unity player build failed. See preceding Console errors / Editor.log. Staging: " + staging);
            MacBuildArchive.ValidateApp(app);
            WriteText(Path.Combine(package, "First Launch.command"), LaunchScript);
            WriteText(Path.Combine(package, "README.txt"), TesterReadme);
            string stagedZip = Path.Combine(staging, zipName);
            Run(tar, "--format zip -cf " + Quote(stagedZip) + " -C " + Quote(staging) + " " + PackageName, Root, 1800000);
            MacBuildArchive.SetUnixPermissions(stagedZip);
            MacBuildArchive.ValidateZip(stagedZip);
            MacBuildArchive.ValidateSourceEntries(package, stagedZip);
            // Only replace generated output after the complete package has passed validation.
            string finalPackage = Path.Combine(Output, PackageName);
            if (Directory.Exists(finalPackage))
                Directory.Move(finalPackage, Path.Combine(Output, PackageName + "_previous_" + Guid.NewGuid().ToString("N")));
            Directory.Move(package, finalPackage);
            string zip = Path.Combine(Output, zipName);
            if (File.Exists(zip)) File.Replace(stagedZip, zip, null);
            else File.Move(stagedZip, zip);
            Directory.Delete(staging); // Empty, never recursive.
            Debug.Log($"macOS build SUCCEEDED\n.app: {Path.Combine(finalPackage, "DungeonGirls.app")}\nZIP: {zip}\nVersion: {version}\nArchive: {new FileInfo(zip).Length:N0} bytes\nUniversal: Intel x64 + Apple Silicon ARM64; Mono; Release");
        }
        finally
        {
            PlayerSettings.SetArchitecture(target, oldArchitecture);
            PlayerSettings.SetScriptingBackend(target, oldBackend);
            EditorUserBuildSettings.development = development;
            EditorUserBuildSettings.allowDebugging = debugging;
            EditorUserBuildSettings.connectProfiler = profiler;
            EditorUserBuildSettings.buildWithDeepProfilingSupport = deep;
        }
    }

    static void WriteText(string path, string content) => File.WriteAllText(path, content.Replace("\r\n", "\n"), new UTF8Encoding(false));
    static string Quote(string value) => "\"" + value + "\"";
    static string Run(string executable, string arguments, string directory, int timeout)
    {
        try
        {
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            }))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout)) { process.Kill(); throw new IOException("Timed out: " + executable); }
                string error = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new IOException(executable + " exited with " + process.ExitCode + ": " + error);
                return stdout.GetAwaiter().GetResult();
            }
        }
        catch (System.ComponentModel.Win32Exception e)
        { throw new IOException("Cannot start " + executable + ". Install/enable the required tool. " + e.Message, e); }
    }

    internal const string LaunchScript = @"#!/bin/bash
set -euo pipefail
trap 'echo ""Launch failed. Please send the Terminal output to the developer.""; read -r -p ""Press Return to close..."" || true' ERR
DIR=""$(cd -- ""$(dirname -- ""$0"")"" && pwd -P)""
APP=""$DIR/DungeonGirls.app""
if [[ ! -d ""$APP"" ]]; then echo ""DungeonGirls.app must be beside this script.""; exit 1; fi
EXE=""$(/usr/libexec/PlistBuddy -c 'Print :CFBundleExecutable' ""$APP/Contents/Info.plist"")""
if [[ -z ""$EXE"" || ""$EXE"" == */* || ! -f ""$APP/Contents/MacOS/$EXE"" ]]; then
  echo ""Invalid or missing CFBundleExecutable. Please extract the complete ZIP again.""; exit 1
fi
/bin/chmod u+x ""$APP/Contents/MacOS/$EXE""
# Repair nested native libraries/helpers too if the unzip tool discarded modes. Never follow symlinks.
/usr/bin/find ""$APP"" -type d -exec /bin/chmod u+rx {} +
/usr/bin/find ""$APP"" -type f -exec /bin/chmod u+rX {} +
while IFS= read -r -d '' FILE; do
  if /usr/bin/file -b ""$FILE"" | /usr/bin/grep -q 'Mach-O'; then /bin/chmod u+x ""$FILE""; fi
done < <(/usr/bin/find ""$APP"" -type f -print0)
# Only this trusted test app is affected; Gatekeeper remains enabled system-wide.
if ! /usr/bin/xattr -dr com.apple.quarantine ""$APP""; then
  echo ""Could not remove quarantine. Move the extracted folder to a writable location and try again.""
  exit 1
fi
/usr/bin/open ""$APP""
";

    internal const string TesterReadme = @"Dungeon Girls — macOS test build

1. Распакуйте ZIP целиком стандартным Архиватором macOS в доступную для записи папку.
2. Запустите First Launch.command рядом с DungeonGirls.app.
3. Если macOS блокирует скрипт: Системные настройки > Конфиденциальность и безопасность > Всё равно открыть (Open Anyway).
4. После первого запуска открывайте DungeonGirls.app обычным двойным кликом.

Если скрипт не открывается или не имеет прав запуска: откройте Terminal, введите
/bin/bash и пробел, перетащите First Launch.command в окно Terminal и нажмите Return.
Это также работает, если архиватор Windows потерял executable permission.

Билд не подписан Developer ID и не notarized Apple. Gatekeeper может показать предупреждение.
Запускайте скрипт только если доверяете отправителю: он исправляет права файлов игры,
удаляет quarantine только с этой DungeonGirls.app и запускает её. sudo не требуется.
Не отключайте Gatekeeper целиком. Если запуск всё равно не удался, отправьте разработчику
вывод Terminal и ~/Library/Logs/<Company Name>/<Product Name>/Player.log (если появился).
Поддерживаются Intel x64 и Apple Silicon ARM64. Проверка реального запуска нужна на обоих Mac.
";
}
