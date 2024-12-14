using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using RSM_Server_Monitor.Properties;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private Process? cliProcess;
        private System.Timers.Timer? monitorTimer;
        private System.Timers.Timer? pingTimer;
        private PerformanceCounter? cpuCounter;
        private PerformanceCounter? ramCounter;
        private long successfulPackets = 0;
        private long failedPackets = 0;
        private string serverStatusMessage = "";

        public MainWindow()
        {
            InitializeComponent();
            LoadLastPath();
            SetupCountersAndTimers();
            this.Topmost = false;
            StartMonitoring();
            UpdateServerStatusDisplay();
        }

        private void LoadLastPath()
        {
            string lastPath = Settings.Default.LastPath;
            pathTextBox.Text = File.Exists(lastPath)
                ? lastPath
                : Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "", "SPT.Server.exe");
        }

        private void SetupCountersAndTimers()
        {
            cpuCounter = new PerformanceCounter("Process", "% Processor Time", "SPT.Server", true);
            ramCounter = new PerformanceCounter("Process", "Working Set", "SPT.Server", true);

            monitorTimer = new System.Timers.Timer(1000);
            monitorTimer.Elapsed += MonitorServer;

            pingTimer = new System.Timers.Timer(10000);
            pingTimer.Elapsed += PingServer;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(pathTextBox.Text))
            {
                StartCLIApplication();
                SaveLastPath();
            }
            else
            {
                MessageBox.Show("Please select a file path for SPT.Server.exe before starting.");
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to terminate the server?", "Confirm Termination", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await StopCLIApplication();
            }
        }

        private void Browse1_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = "Select SPT.Server.exe"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                pathTextBox.Text = openFileDialog.FileName;
            }
        }

        private void StartCLIApplication()
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                MessageBox.Show("A process is already running. Please stop it first.");
                return;
            }

            string exePath = pathTextBox.Text;
            string exeDirectory = Path.GetDirectoryName(exePath) ?? "";

            if (!Directory.Exists(Path.Combine(exeDirectory, "SPT_Data", "Server", "configs")))
            {
                MessageBox.Show("The configuration directory for SPT Server does not exist. Please check your installation.");
                return;
            }

            cliProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = exeDirectory
                }
            };

            try
            {
                cliProcess.Start();
                SetupProcessEvents();
                pingTimer?.Start();
                UpdateServerStatusDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start the CLI application: {ex.Message}");
                cliProcess = null;
                UpdateServerStatusDisplay();
            }
        }

        private void SetupProcessEvents()
        {
            cliProcess!.OutputDataReceived += CliProcess_OutputDataReceived;
            cliProcess!.ErrorDataReceived += CliProcess_ErrorDataReceived;
            cliProcess!.EnableRaisingEvents = true;
            cliProcess!.Exited += CliProcess_Exited;
            cliProcess!.BeginOutputReadLine();
            cliProcess!.BeginErrorReadLine();
        }

        private void CliProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (this.FindName("consoleOutput1") is TextBox textBox)
                    {
                        textBox.AppendText(SanitizeOutput(e.Data) + Environment.NewLine);
                        textBox.ScrollToEnd();
                    }
                });
            }
        }

        private void CliProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (this.FindName("consoleOutput1") is TextBox textBox)
                    {
                        textBox.AppendText($"Error: {SanitizeOutput(e.Data)}" + Environment.NewLine);
                        textBox.ScrollToEnd();
                    }
                });
            }
        }

        private string SanitizeOutput(string output)
        {
            return Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "").Replace("€", "").Replace("â", "").Replace("”", "");
        }

        private async Task StopCLIApplication()
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                try
                {
                    cliProcess.Kill();
                    await WaitForExitAsync(cliProcess);
                    MessageBox.Show("SPT Server has terminated successfully. It is now safe to close this application.");
                    cliProcess = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to stop the CLI application: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("No CLI application is running to stop.");
            }
        }

        private void CliProcess_Exited(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                cliProcess = null;
                successfulPackets = failedPackets = 0;
                serverStatusMessage = "The SPT-Fika Server has crashed and will be restarted. Please check your mods for any conflicts.";
                UpdateServerStatusDisplay();
            });
        }

        private Task WaitForExitAsync(Process process) => Task.Run(() => process.WaitForExit());

        private void StartMonitoring() => monitorTimer?.Start();

        private void MonitorServer(object sender, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (cliProcess == null || cliProcess.HasExited || cpuCounter == null || ramCounter == null) return;

                float cpu = cpuCounter.NextValue() / Environment.ProcessorCount;
                float ram = ramCounter.NextValue();
                double ramUsage = (ram / (Environment.WorkingSet * 1024.0)) * 100;

                string metrics = $"CPU Usage: {cpu:F2}%\nRAM Usage: {Math.Min(ramUsage, 100):F2}%\nNet Usage: {cpu:F2}%\n\nSuccessful Packets Sent: {successfulPackets}\nFailed Packets: {failedPackets}";

                _ = Task.Run(async () =>
                {
                    string webResponse = await GetWebResponseAsync();
                    metrics += "\n==========\n" + webResponse;
                    Dispatcher.Invoke(() => UpdateServerStatusDisplay(metrics));
                });
            });
        }

        private void PingServer(object sender, ElapsedEventArgs e)
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                var pingSender = new Ping();
                var reply = pingSender.Send("8.8.8.8", 1000);
                if (reply.Status == IPStatus.Success)
                {
                    successfulPackets++;
                }
                else
                {
                    failedPackets++;
                }
            }
        }

        private async Task<string> GetWebResponseAsync()
        {
            try
            {
                using (var runspace = RunspaceFactory.CreateRunspace())
                {
                    runspace.Open();
                    using (var ps = PowerShell.Create())
                    {
                        ps.Runspace = runspace;
                        ps.AddScript(@"Invoke-WebRequest -Headers @{""responsecompressed""=""0""} -Method ""GET"" ""http://26.61.37.157:6969/fika/presence/get""");
                        var results = await ps.InvokeAsync().ConfigureAwait(false);

                        if (ps.HadErrors)
                        {
                            var errors = ps.Streams.Error.ReadAll();
                            return $"Error fetching data: {string.Join(", ", errors.Select(e => e.Exception.Message))}";
                        }
                        return results[0]?.BaseObject?.ToString() ?? "No response received";
                    }
                }
            }
            catch (Exception ex)
            {
                return $"General Error: {ex.Message}";
            }
        }

        private void UpdateServerStatusDisplay(string metrics = "")
        {
            Dispatcher.Invoke(() =>
            {
                if (this.FindName("consoleOutput2") is TextBox textBox)
                {
                    textBox.Text = (cliProcess == null || cliProcess.HasExited ? "The SPT-Fika Server is not running." : "The SPT-Fika Server is currently running.")
                                    + "\n\n" + metrics;
                }
            });
        }

        private void SaveLastPath() => Settings.Default.LastPath = pathTextBox.Text;

        private void consoleOutput1_TextChanged(object sender, TextChangedEventArgs e)
        {

        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {

        }
    }
}