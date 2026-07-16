using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace WebAppInstaller
{
    public partial class MainWindow : Window
    {
        #region Fields & Constants

        private const int ApiPort = 5000;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        private bool _isDeploying = false;

        #endregion

        #region Initialization

        public MainWindow()
        {
            InitializeComponent();
        }

        #endregion

        #region Region: Deployment

        private async void Deploy_Click(object sender, RoutedEventArgs e)
        {
            if (_isDeploying)
                return;

            if (string.IsNullOrWhiteSpace(txtAppName.Text))
            {
                MessageBox.Show("Please enter the application name.");
                return;
            }

            string appName = txtAppName.Text.Trim();

            if (appName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || appName.Contains(".."))
            {
                MessageBox.Show("Application name contains invalid characters.");
                return;
            }

            _isDeploying = true;
            btnDeploy.IsEnabled = false;

            try
            {
                string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
                string assetsFolder = Path.Combine(baseFolder, "Assets");

                string apiSource = Path.Combine(assetsFolder, "Api");
                string webSource = Path.Combine(assetsFolder, "Web");
                string caddySource = Path.Combine(assetsFolder, "Caddy");

                if (!Directory.Exists(apiSource))
                    throw new DirectoryNotFoundException($"API source folder not found: {apiSource}");
                if (!Directory.Exists(webSource))
                    throw new DirectoryNotFoundException($"Web source folder not found: {webSource}");
                if (!Directory.Exists(caddySource))
                    throw new DirectoryNotFoundException($"Caddy source folder not found: {caddySource}");

                // إنشاء مجلد رئيسي DeployedApps داخل C:\ ليحتوي على كافة التطبيقات
                string parentFolder = Path.Combine(@"C:\", "DeployedApps");
                string installFolder = Path.Combine(parentFolder, appName);

                string apiDestination = Path.Combine(installFolder, "Api");
                string webDestination = Path.Combine(installFolder, "Web");
                string caddyDestination = Path.Combine(installFolder, "Caddy");

                Directory.CreateDirectory(parentFolder);
                Directory.CreateDirectory(installFolder);

                // 1. إنهاء العمليات السابقة إجبارياً
                txtStatus.Text = "Stopping previous instances...";
                await Task.Run(() =>
                {
                    KillAllPreviousProcesses(appName);
                });

                // 2. نسخ الملفات وإعادة تسمية ملف الـ API
                txtStatus.Text = "Copying files...";
                await Task.Run(() =>
                {
                    CopyDirectoryWithRetry(apiSource, apiDestination);
                    CopyDirectoryWithRetry(webSource, webDestination);
                    CopyDirectoryWithRetry(caddySource, caddyDestination);

                    // تغيير اسم الملف التنفيذي إلى الاسم الجديد
                    string originalApiExe = Path.Combine(apiDestination, "MCS app.exe");
                    string newApiExe = Path.Combine(apiDestination, $"{appName}.exe");

                    if (File.Exists(originalApiExe))
                    {
                        if (File.Exists(newApiExe))
                        {
                            File.Delete(newApiExe);
                        }
                        File.Move(originalApiExe, newApiExe);
                    }
                });

                // 3. إنشاء Caddyfile المُحدث بدون Cache
                txtStatus.Text = "Creating Caddyfile...";
                CreateCaddyFile(caddyDestination, webDestination, appName);

                // 4. تشغيل الـ API بالاسم الجديد
                txtStatus.Text = "Starting API Application...";
                string apiExe = Path.Combine(apiDestination, $"{appName}.exe");
                if (!File.Exists(apiExe))
                    throw new FileNotFoundException($"API executable not found at: {apiExe}");

                StartBackgroundProcess(apiExe, apiDestination, "");

                if (!await WaitUntilAsync(IsApiRunning, TimeSpan.FromSeconds(15)))
                {
                    throw new InvalidOperationException($"API did not respond on port {ApiPort} within 15 seconds.");
                }

                // 5. إضافة استثناء في الجدار الناري لـ Caddy لمنع ظهور رسالة Allow / Cancel
                txtStatus.Text = "Configuring Firewall...";
                string caddyExe = Path.Combine(caddyDestination, "caddy.exe");
                if (!File.Exists(caddyExe))
                    throw new FileNotFoundException($"caddy.exe not found at: {caddyExe}");

                await Task.Run(() => AddFirewallRule(caddyExe));

                // 6. تشغيل Caddy Server
                txtStatus.Text = "Starting Caddy Server...";
                StartBackgroundProcess(caddyExe, caddyDestination, "run --config Caddyfile");

                if (!await WaitUntilAsync(IsCaddyRunning, TimeSpan.FromSeconds(10)))
                {
                    throw new InvalidOperationException("Caddy server did not start responding on port 80 within 10 seconds.");
                }

                // 7. فتح المتصفح فوراً والتركيز عليه
                txtStatus.Text = "Opening Browser...";
                string machineName = Environment.MachineName;
                string targetUrl = $"http://{machineName}/{appName}/";

                Process.Start(new ProcessStartInfo
                {
                    FileName = targetUrl,
                    UseShellExecute = true
                });

                txtStatus.Text = "Deployment Completed";
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Deployment failed";
                MessageBox.Show(ex.ToString(), "Deployment Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isDeploying = false;
                btnDeploy.IsEnabled = true;
            }
        }

        #endregion

        #region Region: Process Controller

        private void StartBackgroundProcess(string exePath, string workingDirectory, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process.Start(psi);
        }

        private void KillAllPreviousProcesses(string appName)
        {
            try
            {
                // إنهاء Caddy
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /IM caddy.exe /T",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();

                // إنهاء الـ API بالاسم الجديد
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /IM \"{appName}.exe\" /T",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();

                // إنهاء الاسم القديم للـ API احترازيًا
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /IM \"MCS app.exe\" /T",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();
            }
            catch
            {
                // تجاهل أي استثناء في حالة عدم وجود عمليات مفعلة
            }
        }

        private void AddFirewallRule(string caddyExePath)
        {
            try
            {
                // إضافة قاعدة السماح لبرنامج Caddy في الجدار الناري لتجنب مطالبات المستخدم
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"advfirewall firewall add rule name=\"CaddyServerRule\" dir=in action=allow program=\"{caddyExePath}\" enable=yes",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi)?.WaitForExit();
            }
            catch
            {
                // تجاهل خطأ الجدار الناري في حال عدم التشغيل كـ Admin
            }
        }

        #endregion

        #region Region: File Copy

        private void CopyDirectoryWithRetry(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destinationFile = Path.Combine(destinationDir, Path.GetFileName(file));
                CopyFileWithRetry(file, destinationFile);
            }

            foreach (string directory in Directory.GetDirectories(sourceDir))
            {
                string destinationDirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
                CopyDirectoryWithRetry(directory, destinationDirectory);
            }
        }

        private void CopyFileWithRetry(string sourceFile, string destinationFile, int maxAttempts = 5)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    File.Copy(sourceFile, destinationFile, true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(300);
                }
            }

            File.Copy(sourceFile, destinationFile, true);
        }

        #endregion

        #region Region: Configuration

        private void CreateCaddyFile(string caddyFolder, string webFolder, string appName)
        {
            string caddyFile = Path.Combine(caddyFolder, "Caddyfile");

            string content = $@":80 {{

    header {{
        Cache-Control ""no-cache, no-store, must-revalidate""
        Pragma ""no-cache""
        Expires ""0""
    }}

    handle /{appName}/api {{
        uri strip_prefix /{appName}
        rewrite * /api/employees
        reverse_proxy localhost:{ApiPort}
    }}

    handle /{appName}/api* {{
        uri strip_prefix /{appName}
        reverse_proxy localhost:{ApiPort}
    }}

    handle /api* {{
        reverse_proxy localhost:{ApiPort}
    }}

    handle /{appName}* {{
        uri strip_prefix /{appName}
        root * ""{webFolder}""
        try_files {{path}} {{path}}/ /index.html
        file_server
    }}
}}
";
            File.WriteAllText(caddyFile, content);
        }

        #endregion

        #region Region: Health Checks

        private async Task<bool> IsApiRunning()
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://localhost:{ApiPort}/");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsCaddyRunning()
        {
            try
            {
                var response = await _httpClient.GetAsync("http://localhost");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (await condition())
                    return true;
                await Task.Delay(500);
            }
            return await condition();
        }

        #endregion
    }
}