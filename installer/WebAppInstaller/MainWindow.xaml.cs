using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.ServiceProcess;
using System.Text;
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

                string apiTaskName = $"{appName}-Api";
                string caddyTaskName = $"{appName}-Caddy";

                Directory.CreateDirectory(parentFolder);
                Directory.CreateDirectory(installFolder);

                // 1. تنصيب SQL Server Express صامتاً إن لم يكن موجوداً، ثم تشغيله
                if (FindSqlService() == null)
                {
                    txtStatus.Text = "Installing SQL Server Express (first time only, this can take 10-15 minutes)...";
                    string sqlSetupExe = Path.Combine(assetsFolder, "Sql", "SQLEXPR_x64_ENU.exe");
                    await Task.Run(() => InstallSqlServerExpress(sqlSetupExe));
                }

                txtStatus.Text = "Starting SQL Server...";
                await Task.Run(() => EnsureSqlServerRunning());

                // 2. إيقاف المهام المجدولة والعمليات السابقة قبل استبدال الملفات
                txtStatus.Text = "Stopping previous instances...";
                await Task.Run(() =>
                {
                    RemoveApiService(apiTaskName);   // remove any prior API service so re-installs don't pile up
                    StopScheduledTask(caddyTaskName);
                    KillAllPreviousProcesses(appName);
                });

                // 3. نسخ الملفات وإعادة تسمية ملف الـ API
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

                // 4. إنشاء Caddyfile المُحدث بدون Cache
                txtStatus.Text = "Creating Caddyfile...";
                CreateCaddyFile(caddyDestination, webDestination, appName);

                // 5. إضافة استثناء في الجدار الناري لـ Caddy لمنع ظهور رسالة Allow / Cancel
                txtStatus.Text = "Configuring Firewall...";
                string caddyExe = Path.Combine(caddyDestination, "caddy.exe");
                if (!File.Exists(caddyExe))
                    throw new FileNotFoundException($"caddy.exe not found at: {caddyExe}");

                await Task.Run(() => AddFirewallRule(caddyExe));

                // 6. تسجيل الـ API كخدمة Windows (تعمل كـ LocalSystem، تبدأ تلقائياً
                //    مع الإقلاع، وتُعاد تشغيلها عند الفشل). Caddy يبقى كمهمة مجدولة.
                txtStatus.Text = "Registering startup tasks...";
                string apiExe = Path.Combine(apiDestination, $"{appName}.exe");
                if (!File.Exists(apiExe))
                    throw new FileNotFoundException($"API executable not found at: {apiExe}");

                await Task.Run(() =>
                {
                    RegisterApiService(apiTaskName, apiExe,
                        $"Runs the {appName} API (Kestrel) as a Windows Service.");
                    RegisterBootTask(caddyTaskName, caddyExe, "run --config Caddyfile", caddyDestination,
                        $"Runs the Caddy web server for {appName} at system startup.");
                });

                // 7. تشغيل خدمة الـ API
                txtStatus.Text = "Starting API Application...";
                await Task.Run(() => StartApiService(apiTaskName));

                if (!await WaitUntilAsync(IsApiRunning, TimeSpan.FromSeconds(30)))
                {
                    throw new InvalidOperationException($"API did not respond on port {ApiPort} within 30 seconds.");
                }

                // 8. تشغيل Caddy Server
                txtStatus.Text = "Starting Caddy Server...";
                await Task.Run(() => RunScheduledTask(caddyTaskName));

                if (!await WaitUntilAsync(IsCaddyRunning, TimeSpan.FromSeconds(15)))
                {
                    throw new InvalidOperationException("Caddy server did not start responding on port 80 within 15 seconds.");
                }

                string machineName = Environment.MachineName;
                string targetUrl = $"http://{machineName}/{appName}/";

                // 9. اختصار على سطح المكتب حتى يفتح المستخدم الصفحة في أي وقت
                txtStatus.Text = "Creating desktop shortcut...";
                CreateDesktopShortcut(appName, targetUrl);

                // 10. فتح المتصفح فوراً والتركيز عليه
                txtStatus.Text = "Opening Browser...";
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetUrl,
                    UseShellExecute = true
                });

                txtStatus.Text = "Deployment Completed - the app now starts automatically with Windows.";
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

        #region Region: Prerequisites

        private ServiceController? FindSqlService()
        {
            // الـ API يتصل بـ localhost\SQLEXPRESS تحديداً (appsettings.json)،
            // لذلك نبحث عن خدمة هذه النسخة بالذات وليس أي SQL Server آخر
            return ServiceController.GetServices().FirstOrDefault(s =>
                s.ServiceName.Equals("MSSQL$SQLEXPRESS", StringComparison.OrdinalIgnoreCase));
        }

        private void InstallSqlServerExpress(string sqlSetupExe)
        {
            if (!File.Exists(sqlSetupExe))
            {
                throw new FileNotFoundException(
                    $"SQL Server Express setup not found at: {sqlSetupExe}\n\n" +
                    "The installer package is incomplete - the Assets\\Sql folder must contain SQLEXPR_x64_ENU.exe.");
            }

            // تنصيب صامت: نسخة SQLEXPRESS (تطابق سلسلة الاتصال في appsettings.json)،
            // تشغيل تلقائي مع الويندوز، وإضافة SYSTEM كمسؤول حتى تستطيع المهمة
            // المجدولة (التي تعمل كـ SYSTEM) إنشاء قاعدة البيانات والاتصال بها.
            // /QS يعرض شريط التقدم فقط بدون أي أسئلة للمستخدم.
            string arguments =
                "/QS /IACCEPTSQLSERVERLICENSETERMS /ACTION=Install /FEATURES=SQLEngine " +
                "/INSTANCENAME=SQLEXPRESS /SQLSVCSTARTUPTYPE=Automatic " +
                "/SQLSYSADMINACCOUNTS=\"BUILTIN\\Administrators\" \"NT AUTHORITY\\SYSTEM\" " +
                "/TCPENABLED=1";

            int exitCode = RunTool(sqlSetupExe, arguments);

            // 3010 = نجاح مع الحاجة لإعادة تشغيل الجهاز لاحقاً (الخدمة تعمل الآن بالفعل)
            if (exitCode != 0 && exitCode != 3010)
            {
                throw new InvalidOperationException(
                    $"SQL Server Express installation failed (setup exit code {exitCode}).\n\n" +
                    "Setup logs: C:\\Program Files\\Microsoft SQL Server\\160\\Setup Bootstrap\\Log");
            }

            if (FindSqlService() == null)
            {
                throw new InvalidOperationException(
                    "SQL Server Express setup finished but no SQL Server service was found.");
            }
        }

        private void EnsureSqlServerRunning()
        {
            ServiceController? sql = FindSqlService();

            if (sql == null)
            {
                throw new InvalidOperationException(
                    "SQL Server is not installed on this machine and automatic installation did not run.");
            }

            sql.Refresh();
            if (sql.Status != ServiceControllerStatus.Running)
            {
                sql.Start();
                sql.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
            }

            // ضبط الخدمة على التشغيل التلقائي حتى تعمل قاعدة البيانات بعد إعادة التشغيل
            RunTool("sc", $"config \"{sql.ServiceName}\" start= auto");
        }

        #endregion

        #region Region: Windows Service (API)

      
        private void RegisterApiService(string serviceName, string exePath, string description)
        {
           
            int create = RunTool("sc", $"create \"{serviceName}\" binPath= \"\\\"{exePath}\\\"\" start= auto");
            if (create != 0)
                throw new InvalidOperationException(
                    $"Failed to register the Windows Service \"{serviceName}\" (sc create exit code {create}). " +
                    "Make sure the installer is running as Administrator.");

            RunTool("sc", $"description \"{serviceName}\" \"{description}\"");

            RunTool("sc", $"failure \"{serviceName}\" reset= 86400 actions= restart/60000/restart/60000/restart/60000");
        }

        private void StartApiService(string serviceName)
        {
            using var svc = new ServiceController(serviceName);
            if (svc.Status != ServiceControllerStatus.Running)
            {
                svc.Start();
                svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            }
        }

        private void RemoveApiService(string serviceName)
        {
            bool exists = ServiceController.GetServices()
                .Any(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                return;

            try
            {
                using var svc = new ServiceController(serviceName);
                if (svc.Status != ServiceControllerStatus.Stopped)
                {
                    svc.Stop();
                    svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
            }
            catch
            {
            }

            RunTool("sc", $"delete \"{serviceName}\"");
        }

        #endregion

        #region Region: Scheduled Tasks (24/7 auto-start)

        private void RegisterBootTask(string taskName, string exePath, string arguments, string workingDirectory, string description)
        {
         
            string argumentsXml = string.IsNullOrEmpty(arguments)
                ? ""
                : $"<Arguments>{SecurityElement.Escape(arguments)}</Arguments>";

            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>{SecurityElement.Escape(description)}</Description>
  </RegistrationInfo>
  <Triggers>
    <BootTrigger>
      <Enabled>true</Enabled>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <RestartOnFailure>
      <Interval>PT1M</Interval>
      <Count>999</Count>
    </RestartOnFailure>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exePath)}</Command>
      {argumentsXml}
      <WorkingDirectory>{SecurityElement.Escape(workingDirectory)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

            string xmlPath = Path.Combine(Path.GetTempPath(), $"{taskName}.xml");
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);

            try
            {
                int exitCode = RunTool("schtasks", $"/Create /TN \"{taskName}\" /XML \"{xmlPath}\" /F");
                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"Failed to register the startup task \"{taskName}\" (schtasks exit code {exitCode}). " +
                        "Make sure the installer is running as Administrator.");
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }

        private void RunScheduledTask(string taskName)
        {
            int exitCode = RunTool("schtasks", $"/Run /TN \"{taskName}\"");
            if (exitCode != 0)
                throw new InvalidOperationException($"Failed to start the scheduled task \"{taskName}\".");
        }

        private void StopScheduledTask(string taskName)
        {
            RunTool("schtasks", $"/End /TN \"{taskName}\"");
        }

        #endregion

        #region Region: Process Controller

        private int RunTool(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null)
                return -1;

            process.WaitForExit();
            return process.ExitCode;
        }

        private void KillAllPreviousProcesses(string appName)
        {
            try
            {
                RunTool("taskkill", "/F /IM caddy.exe /T");

                RunTool("taskkill", $"/F /IM \"{appName}.exe\" /T");

                RunTool("taskkill", "/F /IM \"MCS app.exe\" /T");
            }
            catch
            {
            }
        }

        private void AddFirewallRule(string caddyExePath)
        {
            try
            {
                RunTool("netsh",
                    $"advfirewall firewall add rule name=\"CaddyServerRule\" dir=in action=allow program=\"{caddyExePath}\" enable=yes");
            }
            catch
            {
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

        private void CreateDesktopShortcut(string appName, string url)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            string shortcutPath = Path.Combine(desktop, $"{appName}.url");
            File.WriteAllText(shortcutPath, $"[InternetShortcut]\r\nURL={url}\r\n");
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
