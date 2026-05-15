using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class AutorecordInstaller
{
    private static readonly byte[] PayloadMarker = Encoding.ASCII.GetBytes("AUTORECORD_PAYLOAD_V1");
    private static readonly ModelInfo[] ReleaseModels =
    {
        new ModelInfo("gigaam-v3-ru-quality", "GigaAM v3", "gigaam-v3", "v3")
    };
    private const string InstallRootArgument = "--install-root";

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            EnsureNotRunning();

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var defaultInstallRoot = Path.Combine(localAppData, "Programs", "Autorecord");
            var appDataRoot = Path.Combine(localAppData, "Autorecord");
            var modelsRoot = Path.Combine(appDataRoot, "Models");
            var documentsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Autorecord");
            var requestedInstallRoot = GetRequestedInstallRoot(args);

            return ShowWizard(
                requestedInstallRoot ?? defaultInstallRoot,
                delegate(string installRoot, Action<int, string> report)
                {
                    InstallApplication(installRoot, appDataRoot, modelsRoot, documentsRoot, report);
                });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Autorecord installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ShowWizard(string defaultInstallRoot, InstallAction installAction)
    {
        using (var wizard = new InstallerWizard(defaultInstallRoot, installAction))
        {
            return wizard.ShowDialog() == DialogResult.OK ? 0 : 1;
        }
    }

    private static void InstallApplication(
        string installRoot,
        string appDataRoot,
        string modelsRoot,
        string documentsRoot,
        Action<int, string> report)
    {
        var fullInstallRoot = NormalizeInstallRoot(installRoot);
        AssertInstallRootIsSafe(fullInstallRoot);
        var programsRoot = Path.GetDirectoryName(fullInstallRoot);
        if (string.IsNullOrWhiteSpace(programsRoot))
        {
            throw new InvalidOperationException("Некорректная папка установки.");
        }

        report(5, "Подготовка папок...");
        Directory.CreateDirectory(programsRoot);
        Directory.CreateDirectory(appDataRoot);
        Directory.CreateDirectory(modelsRoot);
        Directory.CreateDirectory(documentsRoot);

        if (Directory.Exists(fullInstallRoot))
        {
            SafeDeleteDirectory(programsRoot, fullInstallRoot);
        }

        Directory.CreateDirectory(fullInstallRoot);

        foreach (var model in ReleaseModels)
        {
            SafeDeleteDirectory(modelsRoot, Path.Combine(modelsRoot, model.Id));
        }

        using (var payload = OpenPayloadStream())
        using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
        {
            report(15, "Установка приложения...");
            ExtractPrefix(archive, "app/", fullInstallRoot);

            var progress = 65;
            foreach (var model in ReleaseModels)
            {
                report(progress, "Установка модели " + model.DisplayName + "...");
                ExtractPrefix(archive, "models/" + model.Id + "/", Path.Combine(modelsRoot, model.Id));
                progress += 20;
            }
        }

        report(90, "Запись manifest и ярлыков...");
        WriteManifest(modelsRoot);
        CreateShortcuts(fullInstallRoot);
        report(100, "Установка завершена.");
    }

    private static string NormalizeInstallRoot(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            throw new InvalidOperationException("Укажите папку установки.");
        }

        var fullInstallRoot = Path.GetFullPath(installRoot.Trim());
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        if (IsSamePath(fullInstallRoot, programFiles))
        {
            return Path.Combine(programFiles, "Autorecord");
        }

        if (!string.IsNullOrWhiteSpace(programFilesX86) && IsSamePath(fullInstallRoot, programFilesX86))
        {
            return Path.Combine(programFilesX86, "Autorecord");
        }

        return fullInstallRoot;
    }

    private static void AssertInstallRootIsSafe(string fullInstallRoot)
    {
        var root = Path.GetPathRoot(fullInstallRoot);
        if (string.IsNullOrWhiteSpace(root) || IsSamePath(fullInstallRoot, root))
        {
            throw new InvalidOperationException("Выберите отдельную папку для установки Autorecord.");
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (IsSamePath(fullInstallRoot, windows) || IsSamePath(fullInstallRoot, system))
        {
            throw new InvalidOperationException("Нельзя устанавливать Autorecord в системную папку Windows.");
        }
    }

    private static bool RequiresElevation(string installRoot)
    {
        var fullInstallRoot = NormalizeInstallRoot(installRoot);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        return IsInsideOrEqual(fullInstallRoot, programFiles)
            || (!string.IsNullOrWhiteSpace(programFilesX86) && IsInsideOrEqual(fullInstallRoot, programFilesX86))
            || IsInsideOrEqual(fullInstallRoot, windows);
    }

    private static bool IsRunningAsAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
        {
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string GetRequestedInstallRoot(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], InstallRootArgument, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private const string LicenseAgreementText =
@"ЛИЦЕНЗИОННОЕ СОГЛАШЕНИЕ AUTORECORD

Перед установкой Autorecord внимательно ознакомьтесь с условиями использования.

1. Назначение программы

Autorecord предназначен для локальной записи, транскрибации и разделения по спикерам встреч, микрофона и системного звука на устройстве пользователя. Аудио и расшифровки обрабатываются локально, если пользователь отдельно не передает их внешним сервисам.

2. Законное использование

Пользователь обязуется применять программу только законным способом: не записывать чужие разговоры без надлежащего основания, учитывать право участников на частную жизнь и тайну связи, а также соблюдать требования законодательства о персональных данных.

Если запись, расшифровка или передача материалов содержит персональные данные, голос, сведения о частной жизни, коммерческую, служебную или иную охраняемую законом информацию, пользователь самостоятельно получает необходимые согласия, уведомляет участников и ограничивает доступ к таким материалам.

Разработчик не контролирует содержание записей и не отвечает за незаконное использование программы пользователем.

3. Модели и лицензии

GigaAM v3 распространяется по лицензии MIT License.
Источник: https://github.com/salute-developers/GigaAM

Pyannote Community-1 распространяется по лицензии CC BY 4.0. Модель не входит в установщик и скачивается пользователем с Hugging Face после принятия условий доступа.
Источник: https://huggingface.co/pyannote/speaker-diarization-community-1

4. Нормативные ориентиры РФ

Конституция РФ, статьи 23 и 24; УК РФ, статьи 137 и 138; ГК РФ, статья 152.2; Федеральный закон N 152-ФЗ «О персональных данных»; Федеральный закон N 126-ФЗ «О связи», статья 63.
";

    private static void EnsureNotRunning()
    {
        var running = Process.GetProcesses()
            .Where(process =>
            {
                try
                {
                    return string.Equals(process.ProcessName, "Autorecord.App", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, "worker", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

        if (running.Length == 0)
        {
            return;
        }

        var processList = string.Join(", ", running.Select(process => process.ProcessName + " (" + process.Id + ")"));
        throw new InvalidOperationException("Закройте Autorecord перед установкой. Запущены: " + processList);
    }

    private static Stream OpenPayloadStream()
    {
        var selfPath = Assembly.GetExecutingAssembly().Location;
        var file = File.OpenRead(selfPath);

        try
        {
            if (file.Length < PayloadMarker.Length + sizeof(long))
            {
                throw new InvalidOperationException("Installer payload is missing.");
            }

            file.Position = file.Length - PayloadMarker.Length;
            var marker = new byte[PayloadMarker.Length];
            ReadExactly(file, marker, 0, marker.Length);
            if (!PayloadMarker.SequenceEqual(marker))
            {
                throw new InvalidOperationException("Installer payload marker is missing.");
            }

            file.Position = file.Length - PayloadMarker.Length - sizeof(long);
            var lengthBytes = new byte[sizeof(long)];
            ReadExactly(file, lengthBytes, 0, lengthBytes.Length);
            var payloadLength = BitConverter.ToInt64(lengthBytes, 0);
            if (payloadLength <= 0)
            {
                throw new InvalidOperationException("Installer payload length is invalid.");
            }

            var payloadOffset = file.Length - PayloadMarker.Length - sizeof(long) - payloadLength;
            if (payloadOffset <= 0)
            {
                throw new InvalidOperationException("Installer payload offset is invalid.");
            }

            return new BoundedReadStream(file, payloadOffset, payloadLength);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static void ExtractPrefix(ZipArchive archive, string prefix, string destinationRoot)
    {
        var fullDestinationRoot = Path.GetFullPath(destinationRoot);
        Directory.CreateDirectory(fullDestinationRoot);

        var found = false;
        foreach (var entry in archive.Entries)
        {
            var normalizedName = entry.FullName.Replace('\\', '/');
            if (!normalizedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            found = true;
            var relativeName = normalizedName.Substring(prefix.Length);
            if (string.IsNullOrWhiteSpace(relativeName))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(fullDestinationRoot, relativeName.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideOrEqual(targetPath, fullDestinationRoot))
            {
                throw new InvalidOperationException("Payload contains an unsafe path: " + entry.FullName);
            }

            if (normalizedName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            using (var input = entry.Open())
            using (var output = File.Create(targetPath))
            {
                input.CopyTo(output);
            }
        }

        if (!found)
        {
            throw new InvalidOperationException("Payload section is missing: " + prefix);
        }
    }

    private static void WriteManifest(string modelsRoot)
    {
        var manifestModels = new List<object>();
        foreach (var model in ReleaseModels)
        {
            var modelPath = Path.Combine(modelsRoot, model.Id);
            if (!Directory.Exists(modelPath))
            {
                throw new InvalidOperationException("Installed model folder is missing: " + model.Id);
            }

            manifestModels.Add(new Dictionary<string, object>
            {
                { "id", model.Id },
                { "displayName", model.DisplayName },
                { "engine", model.Engine },
                { "version", model.Version },
                { "localPath", modelPath },
                { "installedAt", DateTimeOffset.UtcNow.ToString("o") },
                { "totalSizeBytes", GetDirectorySize(modelPath) },
                { "files", GetRelativeFiles(modelPath).ToArray() },
                { "status", 2 }
            });
        }

        var manifest = new Dictionary<string, object> { { "models", manifestModels } };
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(manifest);
        File.WriteAllText(Path.Combine(modelsRoot, "manifest.json"), PrettyJson(json), new UTF8Encoding(false));
    }

    private static IEnumerable<string> GetRelativeFiles(string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.GetFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetFullPath(file).Substring(fullRoot.Length).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
    }

    private static long GetDirectorySize(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
    }

    private static void CreateShortcuts(string installRoot)
    {
        var exePath = Path.Combine(installRoot, "Autorecord.App.exe");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Autorecord");

        Directory.CreateDirectory(startMenu);
        CreateShortcut(Path.Combine(desktop, "Autorecord.lnk"), exePath, installRoot);
        CreateShortcut(Path.Combine(startMenu, "Autorecord.lnk"), exePath, installRoot);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }

        var shell = Activator.CreateInstance(shellType);
        if (shell == null)
        {
            return;
        }

        var shortcut = shellType.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            null,
            shell,
            new object[] { shortcutPath });

        if (shortcut == null)
        {
            return;
        }

        var shortcutType = shortcut.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
        shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
        shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void SafeDeleteDirectory(string root, string directory)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullDirectory = Path.GetFullPath(directory);
        if (!IsInsideOrEqual(fullDirectory, fullRoot))
        {
            throw new InvalidOperationException("Refusing to delete outside expected root: " + fullDirectory);
        }

        if (Directory.Exists(fullDirectory))
        {
            Directory.Delete(fullDirectory, true);
        }
    }

    private static bool IsInsideOrEqual(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSamePath(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
            count -= read;
        }
    }

    private static string PrettyJson(string minifiedJson)
    {
        var indent = 0;
        var quoted = false;
        var builder = new StringBuilder(minifiedJson.Length * 2);

        for (var i = 0; i < minifiedJson.Length; i++)
        {
            var ch = minifiedJson[i];
            if (ch == '"' && !IsEscaped(minifiedJson, i))
            {
                quoted = !quoted;
            }

            if (quoted)
            {
                builder.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '{':
                case '[':
                    builder.Append(ch);
                    builder.AppendLine();
                    builder.Append(new string(' ', ++indent * 2));
                    break;
                case '}':
                case ']':
                    builder.AppendLine();
                    builder.Append(new string(' ', --indent * 2));
                    builder.Append(ch);
                    break;
                case ',':
                    builder.Append(ch);
                    builder.AppendLine();
                    builder.Append(new string(' ', indent * 2));
                    break;
                case ':':
                    builder.Append(": ");
                    break;
                default:
                    if (!char.IsWhiteSpace(ch))
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsEscaped(string value, int quoteIndex)
    {
        var slashCount = 0;
        for (var i = quoteIndex - 1; i >= 0 && value[i] == '\\'; i--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private sealed class InstallerWizard : Form
    {
        private static readonly Size WizardPageSize = new Size(620, 376);

        private readonly InstallAction _installAction;
        private readonly Panel[] _pages;
        private readonly Button _backButton = new Button();
        private readonly Button _nextButton = new Button();
        private readonly Button _cancelButton = new Button();
        private readonly CheckBox _agreeBox = new CheckBox();
        private readonly TextBox _installPathBox = new TextBox();
        private readonly ProgressBar _progressBar = new ProgressBar();
        private readonly Label _progressStatus = new Label();
        private readonly CheckBox _openAppBox = new CheckBox();
        private int _pageIndex;
        private string _installedExePath = "";

        public InstallerWizard(string defaultInstallRoot, InstallAction installAction)
        {
            _installAction = installAction;
            Text = "Установка Autorecord";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 620;
            Height = 470;
            MinimumSize = new Size(560, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _installPathBox.Text = defaultInstallRoot;
            _pages = new[]
            {
                BuildLicensePage(),
                BuildInstallPathPage(),
                BuildInstallProgressPage(),
                BuildFinishPage()
            };

            foreach (var page in _pages)
            {
                page.Left = 0;
                page.Top = 0;
                page.Width = ClientSize.Width;
                page.Height = ClientSize.Height - 54;
                page.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                Controls.Add(page);
            }

            var separator = new Label();
            separator.BorderStyle = BorderStyle.Fixed3D;
            separator.Left = 0;
            separator.Top = ClientSize.Height - 54;
            separator.Width = ClientSize.Width;
            separator.Height = 2;
            separator.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(separator);

            _backButton.Text = "< Назад";
            _backButton.Left = ClientSize.Width - 300;
            _backButton.Top = ClientSize.Height - 40;
            _backButton.Width = 90;
            _backButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _backButton.Click += delegate { ShowPage(_pageIndex - 1); };
            Controls.Add(_backButton);

            _nextButton.Text = "Далее >";
            _nextButton.Left = ClientSize.Width - 200;
            _nextButton.Top = ClientSize.Height - 40;
            _nextButton.Width = 90;
            _nextButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _nextButton.Click += NextButton_Click;
            Controls.Add(_nextButton);

            _cancelButton.Text = "Отмена";
            _cancelButton.Left = ClientSize.Width - 100;
            _cancelButton.Top = ClientSize.Height - 40;
            _cancelButton.Width = 90;
            _cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(_cancelButton);

            ShowPage(0);
        }

        private Panel BuildLicensePage()
        {
            var page = CreatePage("Лицензионное соглашение", "Перед установкой Autorecord ознакомьтесь с условиями использования.");

            var text = new RichTextBox();
            text.Left = 24;
            text.Top = 82;
            text.Width = 552;
            text.Height = 245;
            text.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            text.ReadOnly = true;
            text.BorderStyle = BorderStyle.FixedSingle;
            text.Font = new Font("Times New Roman", 10);
            text.Text = LicenseAgreementText;
            page.Controls.Add(text);

            _agreeBox.Left = 24;
            _agreeBox.Top = 340;
            _agreeBox.Width = 420;
            _agreeBox.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _agreeBox.Text = "Я согласен с условиями лицензионного соглашения";
            _agreeBox.CheckedChanged += delegate { UpdateButtons(); };
            page.Controls.Add(_agreeBox);

            return page;
        }

        private Panel BuildInstallPathPage()
        {
            var page = CreatePage("Папка установки", "Выберите папку, в которую будет установлен Autorecord.");

            var label = new Label();
            label.Left = 24;
            label.Top = 96;
            label.Width = 540;
            label.Text = "Autorecord будет установлен в следующую папку:";
            page.Controls.Add(label);

            _installPathBox.Left = 24;
            _installPathBox.Top = 128;
            _installPathBox.Width = 430;
            _installPathBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _installPathBox.Leave += delegate { NormalizeInstallPathBox(showWarning: false); };
            page.Controls.Add(_installPathBox);

            var browseButton = new Button();
            browseButton.Left = 466;
            browseButton.Top = 126;
            browseButton.Width = 110;
            browseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseButton.Text = "Обзор...";
            browseButton.Click += BrowseButton_Click;
            page.Controls.Add(browseButton);

            return page;
        }

        private Panel BuildInstallProgressPage()
        {
            var page = CreatePage("Установка", "Пожалуйста, подождите, пока Autorecord устанавливается.");

            _progressStatus.Left = 24;
            _progressStatus.Top = 118;
            _progressStatus.Width = 540;
            _progressStatus.Text = "Подготовка...";
            page.Controls.Add(_progressStatus);

            _progressBar.Left = 24;
            _progressBar.Top = 150;
            _progressBar.Width = 552;
            _progressBar.Height = 22;
            _progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            page.Controls.Add(_progressBar);

            return page;
        }

        private Panel BuildFinishPage()
        {
            var page = CreatePage("Установка завершена", "Autorecord успешно установлен на этот компьютер.");

            _openAppBox.Left = 24;
            _openAppBox.Top = 110;
            _openAppBox.Width = 260;
            _openAppBox.Text = "Открыть Autorecord";
            _openAppBox.Checked = true;
            page.Controls.Add(_openAppBox);

            return page;
        }

        private static Panel CreatePage(string title, string description)
        {
            var page = new Panel();
            page.Size = WizardPageSize;
            page.BackColor = Color.White;

            var titleLabel = new Label();
            titleLabel.Left = 24;
            titleLabel.Top = 22;
            titleLabel.Width = 540;
            titleLabel.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            titleLabel.Text = title;
            page.Controls.Add(titleLabel);

            var descriptionLabel = new Label();
            descriptionLabel.Left = 24;
            descriptionLabel.Top = 48;
            descriptionLabel.Width = 540;
            descriptionLabel.Height = 34;
            descriptionLabel.Text = description;
            page.Controls.Add(descriptionLabel);

            return page;
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку установки Autorecord";
                dialog.SelectedPath = _installPathBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _installPathBox.Text = NormalizeInstallRoot(dialog.SelectedPath);
                }
            }
        }

        private bool NormalizeInstallPathBox(bool showWarning)
        {
            try
            {
                var installRoot = NormalizeInstallRoot(_installPathBox.Text);
                AssertInstallRootIsSafe(installRoot);
                _installPathBox.Text = installRoot;
                return true;
            }
            catch (Exception ex)
            {
                if (showWarning)
                {
                    MessageBox.Show(this, ex.Message, "Некорректная папка установки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return false;
            }
        }

        private void NextButton_Click(object sender, EventArgs e)
        {
            if (_pageIndex == 0)
            {
                ShowPage(1);
                return;
            }

            if (_pageIndex == 1)
            {
                if (!NormalizeInstallPathBox(showWarning: true))
                {
                    return;
                }

                var installRoot = _installPathBox.Text;
                if (RequiresElevation(installRoot) && !IsRunningAsAdministrator())
                {
                    var answer = MessageBox.Show(
                        this,
                        "Для установки в эту папку нужны права администратора. Сейчас установщик будет запущен с повышенными правами.",
                        "Требуются права администратора",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Information);
                    if (answer != DialogResult.OK)
                    {
                        return;
                    }

                    Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        Arguments = InstallRootArgument + " " + QuoteArgument(installRoot)
                    });
                    DialogResult = DialogResult.OK;
                    return;
                }

                ShowPage(2);
                BeginInvoke(new MethodInvoker(StartInstallation));
                return;
            }

            if (_pageIndex == 3)
            {
                if (_openAppBox.Checked && File.Exists(_installedExePath))
                {
                    Process.Start(new ProcessStartInfo(_installedExePath) { UseShellExecute = true });
                }

                DialogResult = DialogResult.OK;
            }
        }

        private void StartInstallation()
        {
            try
            {
                var installRoot = _installPathBox.Text.Trim();
                _installAction(installRoot, ReportProgress);
                _installedExePath = Path.Combine(Path.GetFullPath(installRoot), "Autorecord.App.exe");
                ShowPage(3);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Ошибка установки", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowPage(1);
            }
        }

        private void ReportProgress(int percent, string message)
        {
            _progressBar.Value = Math.Max(_progressBar.Minimum, Math.Min(_progressBar.Maximum, percent));
            _progressStatus.Text = message;
            Application.DoEvents();
        }

        private void ShowPage(int pageIndex)
        {
            _pageIndex = pageIndex;
            for (var i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visible = i == pageIndex;
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            _backButton.Enabled = _pageIndex == 1;
            _cancelButton.Enabled = _pageIndex != 2 && _pageIndex != 3;
            _nextButton.Enabled = _pageIndex != 0 || _agreeBox.Checked;
            _nextButton.Text = _pageIndex == 3 ? "Завершить" : "Далее >";

            if (_pageIndex == 2)
            {
                _backButton.Enabled = false;
                _nextButton.Enabled = false;
            }
        }
    }

    private delegate void InstallAction(string installRoot, Action<int, string> report);

    private sealed class ModelInfo
    {
        public ModelInfo(string id, string displayName, string engine, string version)
        {
            Id = id;
            DisplayName = displayName;
            Engine = engine;
            Version = version;
        }

        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string Engine { get; private set; }
        public string Version { get; private set; }
    }

    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public BoundedReadStream(Stream inner, long start, long length)
        {
            _inner = inner;
            _start = start;
            _length = length;
            _position = 0;
            _inner.Position = _start;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _length; } }

        public override long Position
        {
            get { return _position; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length)
            {
                return 0;
            }

            var remaining = _length - _position;
            if (count > remaining)
            {
                count = (int)Math.Min(int.MaxValue, remaining);
            }

            _inner.Position = _start + _position;
            var read = _inner.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long next;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    next = offset;
                    break;
                case SeekOrigin.Current:
                    next = _position + offset;
                    break;
                case SeekOrigin.End:
                    next = _length + offset;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("origin");
            }

            if (next < 0 || next > _length)
            {
                throw new IOException("Attempted to seek outside payload stream.");
            }

            _position = next;
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
