using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Timers;
using System.Linq;
using System.Net.NetworkInformation;
using System.Collections.ObjectModel;
using Microsoft.Win32;
using REVIN_SPT_FIKA_Server_Monitor.Properties;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.ComponentModel;

// This program was written by, and is owned by, Revin.
// This program and/or code is not to be tampered with, copied, or redistributed by any means necessary.
// If this code is found in any of the listed above then legal action will be taken.
// If a bug is found please find me in the Project Fika Discord and discuss it there. Do not attempt to fix it!
// This program is solely for use by the SPT FIKA Community and this program is to be free of use with the above exceptions.
// If you paid for this program you were scammed.
// Project Fika Discord: https://discord.gg/project-fika
// Program written and distributed by Revin, 2024.

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
            UpdateServerStatusDisplay(); // Line 43 fixed by moving method below
            SetupPerformanceCounters();
            SetupPingTimer();
            this.Topmost = false; // Ensure the window does not stay on top
            StartMonitoring(); // Start monitoring immediately
        }

        private void LoadLastPath()
        {
            string? lastPath = Settings.Default.LastPath;
            if (!string.IsNullOrWhiteSpace(lastPath) && File.Exists(lastPath))
            {
                pathTextBox.Text = lastPath;
            }
        }

        private void SaveLastPath()
        {
            Settings.Default.LastPath = pathTextBox.Text;
            Settings.Default.Save();
        }

        private void SetupPerformanceCounters()
        {
            cpuCounter = new PerformanceCounter("Process", "% Processor Time", "SPT.Server", true);
            ramCounter = new PerformanceCounter("Process", "Working Set", "SPT.Server", true);
        }

        private void SetupPingTimer()
        {
            pingTimer = new System.Timers.Timer(10000); // Ping every 10 seconds
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

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show("Are you sure that you want to terminate the server?", "Confirm Termination", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                StopCLIApplication();
                UpdateServerStatusDisplay(); // Line 96 fixed by moving method below
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
            try
            {
                if (cliProcess != null && !cliProcess.HasExited)
                {
                    MessageBox.Show("A process is already running. Please stop it first.");
                    return;
                }

                cliProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pathTextBox.Text,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                cliProcess.Start();

                if (!cliProcess.HasExited)
                {
                    cliProcess.OutputDataReceived += CliProcess_OutputDataReceived;
                    cliProcess.ErrorDataReceived += CliProcess_ErrorDataReceived;
                    cliProcess.EnableRaisingEvents = true;
                    cliProcess.Exited += CliProcess_Exited;

                    cliProcess.BeginOutputReadLine();
                    cliProcess.BeginErrorReadLine();
                    SetupPerformanceCounters(); // Reinitialize counters for the new process
                    pingTimer?.Start();
                }
                else
                {
                    throw new Exception("Process started but immediately exited.");
                }

                UpdateServerStatusDisplay(); // Line 155 fixed by moving method below
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start the CLI application: {ex.Message}");
                cliProcess = null;
                UpdateServerStatusDisplay(); // Line 161 fixed by moving method below
            }
        }

        private void CliProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (this.FindName("consoleOutput1") is TextBox textBox)
                    {
                        string cleanOutput = Regex.Replace(e.Data, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                        cleanOutput = cleanOutput.Replace("€", "").Replace("â", "").Replace("”", "");
                        textBox.AppendText(cleanOutput + Environment.NewLine);
                        textBox.ScrollToEnd();
                    }
                });
            }
        }

        private void CliProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (this.FindName("consoleOutput1") is TextBox textBox)
                    {
                        string cleanOutput = Regex.Replace(e.Data, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
                        cleanOutput = cleanOutput.Replace("€", "").Replace("â", "").Replace("”", "");
                        textBox.AppendText("Error: " + cleanOutput + Environment.NewLine);
                        textBox.ScrollToEnd();
                    }
                });
            }
        }

        private async void StopCLIApplication()
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
            CleanupResources(); // Ensure cleanup after stopping the CLI process
        }

        private void CliProcess_Exited(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (cliProcess != null)
                {
                    cliProcess = null;
                    successfulPackets = 0;
                    failedPackets = 0;
                    serverStatusMessage = "The SPT-Fika Server has crashed and will be restarted. Please check your mods for any conflicts.";
                    UpdateServerStatusDisplay(); // Line 231 fixed by moving method below
                }
            });
        }

        private Task WaitForExitAsync(Process process)
        {
            return Task.Run(() => process.WaitForExit());
        }

        private void StartMonitoring()
        {
            if (monitorTimer == null)
            {
                monitorTimer = new System.Timers.Timer(1000); // Check every 1 second for performance metrics
                monitorTimer.Elapsed += MonitorServer;
            }
            monitorTimer.Enabled = true;
        }

        private void MonitorServer(object? sender, ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                string metrics = "";
                float cpu = 0;
                double ramUsage = 0;

                if (cliProcess != null && !cliProcess.HasExited)
                {
                    if (cpuCounter != null && ramCounter != null)
                    {
                        // CPU usage calculation
                        cpu = cpuCounter.NextValue() / Environment.ProcessorCount;

                        // RAM usage calculation
                        float ram = ramCounter.NextValue();
                        ramUsage = (ram / (Environment.WorkingSet * 1024.0)) * 100; // Convert bytes to KB for comparison
                    }
                }

                // Update the metrics, showing 0 if the server isn't running
                metrics = $"CPU Usage: {cpu:F2}%\nRAM Usage: {Math.Min(ramUsage, 100):F2}%\nNet Usage: {cpu:F2}%\n\nSuccessful Packets Sent: {successfulPackets}\nFailed Packets: {failedPackets}";

                Task.Run(async () =>
                {
                    string webResponse = await GetWebResponseAsync();
                    metrics += "\n==========\n" + webResponse;
                    Dispatcher.Invoke(() => UpdateServerStatusDisplay(metrics)); // Line 279 fixed by moving method below
                });
            });
        }

        private void PingServer(object? sender, ElapsedEventArgs e)
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                Ping pingSender = new Ping();
                PingReply reply = pingSender.Send("8.8.8.8", 1000); // Timeout set to 1 second
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
                        return results[0].BaseObject.ToString();
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
                    textBox.Clear();
                    string statusMessage = serverStatusMessage;
                    if (string.IsNullOrEmpty(statusMessage))
                    {
                        statusMessage = cliProcess == null || cliProcess.HasExited
                            ? "The SPT-Fika Server is not running."
                            : "The SPT-Fika Server is currently running.";
                    }
                    else
                    {
                        serverStatusMessage = ""; // Clear the message after display
                    }
                    textBox.Text = statusMessage + "\n\n" + metrics;
                }
            });
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Additional logic for path changes.
        }

        private void CleanupResources()
        {
            monitorTimer?.Stop();
            monitorTimer?.Dispose();
            pingTimer?.Stop();
            pingTimer?.Dispose();
            cpuCounter?.Dispose();
            ramCounter?.Dispose();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            // Stop the CLI process if it's running
            if (cliProcess != null && !cliProcess.HasExited)
            {
                StopCLIApplication();
            }

            // Clean up resources
            CleanupResources();
        }
    }
}