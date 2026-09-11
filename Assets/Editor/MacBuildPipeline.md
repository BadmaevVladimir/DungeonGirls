# macOS Universal из Windows

Для Unity 6000.5.8f1. Нужны активная лицензия Unity и модуль **Mac Build Support (Mono)** через Unity Hub. Упаковка требует Windows 10/11 `tar.exe` (bsdtar/libarchive); Git необязателен.

## Запуск

- В Unity: **Dungeon Girls > Build > macOS Universal**.
- **Dungeon Girls > Build > Open macOS Build Folder** открывает результат.
- В PowerShell из корня проекта, предварительно закрыв проект в Unity:

```powershell
& ./Assets/Editor/BuildMac.ps1
# Нестандартная установка редактора:
& ./Assets/Editor/BuildMac.ps1 -UnityPath 'D:\Unity Editors\6000.5.8f1\Editor\Unity.exe'
```

ZIP: `Builds/macOS/DungeonGirls_Mac_<bundleVersion>_<optional-git-hash>.zip`.
Внутри `DungeonGirls_Mac/`: `DungeonGirls.app`, `First Launch.command`, `README.txt`.
Лог команды: `Logs/MacBuildPipeline.log`.

## Настройки

Используются включённые сцены текущего Build Profile (включая override Scene List), иначе общий `EditorBuildSettings.scenes`. Пустые, отсутствующие и дублированные сцены отклоняются. Сцены перечисляются в Console. Несохранённые сцены при запуске через меню предлагается сохранить.

`StandaloneOSX`, `NamedBuildTarget.Standalone`, `SetArchitecture(..., 2)` задают Universal; backend временно устанавливается в Mono. Development, Script Debugging, Autoconnect Profiler и Deep Profiling выключаются; BuildOptions.None. Настройки backend, архитектуры и отладки восстанавливаются в finally. CLI заранее выбирает macOS через `-buildTarget OSXUniversal`; Unity может оставить выбранную платформу macOS после сборки.

После BuildReport.Succeeded проверяются Info.plist, CFBundleExecutable и обе архитектуры в Mach-O приложения и UnityPlayer.dylib, если он присутствует. Нативные плагины должны поддерживать обе архитектуры; сторонние плагины требуют проверки на реальных Mac.

## Упаковка

`ZipFile.CreateFromDirectory` не используется: обычное перечисление файлов .NET не гарантирует сохранение символических ссылок и Unix-прав. bsdtar создаёт ZIP без `-h`/разыменования ссылок, сохраняя их тип и содержимое. Затем меняются только поля Unix host/mode в центральном каталоге ZIP/ZIP64: ссылки остаются ссылками, каталогам и обычным файлам назначается 0755. Это намеренно включает данные, чтобы охватить helpers и исполняемые файлы без расширения. Сжатые данные, пути, extra fields, dylib, framework и .bundle не переписываются. System.IO.Compression применяется только для чтения/проверки архива.

Сборка создаётся в уникальной `.staging-*`. Ошибка оставляет staging для диагностики и сохраняет предыдущую сборку. После успеха предыдущая папка переносится в `DungeonGirls_Mac_previous_*`; резервные папки можно удалить вручную. ZIP той же версии/commit заменяется после проверки. Нужно свободное место под приложение, ZIP и предыдущую сборку.

## Тестер и ограничения

Тестер распаковывает ZIP стандартным Архиватором macOS и запускает First Launch.command. Скрипт читает имя бинарника из Info.plist, исправляет права основного и вложенных Mach-O, удаляет quarantine только с этой игры и вызывает `open`. Пути заключены в кавычки, скрипт записан UTF-8 без BOM с LF.

Gatekeeper может блокировать и приложение, и `.command`: разрешить запуск в настройках безопасности или в Terminal ввести `/bin/bash `, перетащить скрипт в окно и нажать Return. Это также помогает, если архиватор потерял executable-бит скрипта. Бесшовный первый запуск неподписанного приложения гарантировать нельзя. sudo и отключение Gatekeeper не требуются. Проверка запуска на Intel Mac и Apple Silicon остаётся обязательной.

Developer ID signing и notarization не выполняются. В будущем нужны Mac/macOS CI, Apple Developer Program, сертификат Developer ID Application с private key, подпись вложенного кода, hardened runtime и подходящие Unity entitlements. Затем `notarytool submit`, ожидание результата, `stapler staple`, проверка `codesign`/`spctl` и финальная упаковка на Mac. Для IL2CPP macOS также нужен macOS toolchain.

## Источники

- [Unity 6.5: Build a macOS application](https://docs.unity3d.com/6000.5/Documentation/Manual/macos-building.html)
- [Unity: PlayerSettings.SetArchitecture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PlayerSettings.SetArchitecture.html)
- [bsdtar: ZIP и символические ссылки](https://github.com/libarchive/libarchive/blob/master/tar/bsdtar.1)

API BuildProfile.GetScenesForBuild, NamedBuildTarget и PlayerSettings сверены также с UnityEditor.xml установленного редактора 6000.5.8f1.

## Проверка реализации (11 сентября 2026)

- C# скомпилирован внешним компилятором .NET с UnityEditor.dll и UnityEngine.dll установленного Unity 6000.5.8f1: 0 ошибок, 0 предупреждений. Это проверка API/синтаксиса, не замена компиляции проекта в Unity.
- Пройдены проверки ZIP и ZIP64 (65 536 записей): сохранение типа и содержимого symlink, данных и Unix permissions, пути с пробелами. Проверен bsdtar round-trip символической ссылки.
- Проверены принятие Universal Mach-O и отклонение некорректного бинарника. Проверены синтаксис PowerShell и `bash -n` для полного First Launch.command.
- В общей Scene List включена `Assets/Scenes/SampleScene.unity`. Выбор сцен профиля описан выше; фактическое выполнение API и меню требует запуска редактора.
- Проверена упаковка файлов через установленный bsdtar и сверка ZIP с исходной папкой; пропущенный файл обнаруживается проверкой.
- Реальная Unity-сборка была запущена, но редактор завершился до компиляции с кодом 198: `No valid Unity Editor license found`. Активация Unity Hub необходима, чтобы проверить меню, собрать реальный .app/ZIP и затем проверить запуск на обоих Mac. Готовый игровой архив в ходе этой проверки не получен.
