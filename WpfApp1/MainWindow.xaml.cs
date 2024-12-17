using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.Management.Automation;
using RSM_Server_Manager.Properties;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// This code was written and developed by Revin.
// This software is strictly and ONLY for the Fika Project.
// Redistribution of this code without consent is disallowed and strictly prohibited.
// Copying this code is strictly prohibited.
// Copying this code then publishing it on the SPT mod site is STRICTLY prohibited. 
// If ANY of this code is used in another program or project then you must reference me at any part, after contacting me and getting my approval to use the code.
// You are NOT allowed to claim this code, or software, as your own.
// ssh and Lacyway are goobers to the tenth power.
// Please join the Project Fika Discord here: https://discord.gg/project-fika

namespace WpfApp1
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (!CheckForDotNet8())
            {
                MessageBoxResult result = MessageBox.Show(
                    "This application requires .NET 8.0 or higher to run. Do you want to download it now?",
                    "Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo("https://dotnet.microsoft.com/download/dotnet/8.0")
                    {
                        UseShellExecute = true
                    });
                    Current.Shutdown();
                }
                else
                {
                    Current.Shutdown();
                }
            }
            else
            {
                base.OnStartup(e);
            }
        }

        private bool CheckForDotNet8()
        {
            try
            {
                // Path where .NET runtimes are typically installed
                string dotnetPath = Environment.GetEnvironmentVariable("ProgramFiles") + @"\dotnet\shared\Microsoft.NETCore.App";
                if (Directory.Exists(dotnetPath))
                {
                    // Check if any version is 8.0.0 or higher
                    return Directory.GetDirectories(dotnetPath)
                        .Select(dir => Version.Parse(Path.GetFileName(dir)))
                        .Any(version => version >= new Version(8, 0, 0));
                }
                return false;
            }
            catch
            {
                return false; // If something goes wrong, assume .NET 8.0 is not installed, or you just suck
            }
        }
    }
public partial class MainWindow : Window
    {
        private Process? cliProcess;
        private System.Timers.Timer? monitorTimer;
        private int serverCrashCount = 0;
        private bool serverRunning = false;
        private string? fikaJsoncPath;
        private bool hasDisplayedFikaNotFound = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadLastPath();
            SetupTimer();
            this.Topmost = false;
            StartMonitoring();
            DisplayStartupMessage();
            FindFikaJsonc();
        }

        private async void DisplayStartupMessage()
        {
            if (this.FindName("consoleOutput1") is TextBox textBox)
            {
                textBox.AppendText("RSM - Server Monitor\nFor Project Fika\nDeveloped by: Revin\nDecember 2024" + Environment.NewLine);
                await Task.Delay(7000); // Show message for 7 seconds
                textBox.Clear();
                UpdateServerStatusDisplay(); // Display fika.jsonc message after clearing
            }
        }

        private void LoadLastPath()
        {
            string lastPath = Settings.Default.LastPath;
            pathTextBox.Text = File.Exists(lastPath)
                ? lastPath
                : Path.Combine(Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? "", "SPT.Server.exe");
        }

        private void SetupTimer()
        {
            monitorTimer = new System.Timers.Timer(1000); // Every second for monitoring
            monitorTimer.Elapsed += MonitorServer;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(pathTextBox.Text))
            {
                if (cliProcess == null || cliProcess.HasExited)
                {
                    StartCLIApplication();
                    SaveLastPath();
                }
                else
                {
                    MessageBox.Show("Server is already running. Use the Restart button to restart it.");
                }
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
                FindFikaJsonc();
            }
        }

        private void FindFikaJsonc()
        {
            string exeDirectory = Path.GetDirectoryName(pathTextBox.Text) ?? string.Empty;
            string fikaJsoncSearchPath = Path.Combine(exeDirectory, "user", "mods", "fika-server", "assets", "configs", "fika.jsonc");

            if (File.Exists(fikaJsoncSearchPath))
            {
                fikaJsoncPath = fikaJsoncSearchPath;
                hasDisplayedFikaNotFound = false;
                UpdateServerStatusDisplay("fika.jsonc has been found.");
            }
            else
            {
                fikaJsoncPath = null;
                hasDisplayedFikaNotFound = true;
                UpdateServerStatusDisplay("fika.jsonc has not been found.");
            }
        }

        private void StartCLIApplication()
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                MessageBox.Show("A process is already running. Please stop it first.");
                return;
            }
            // Revin was here
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
                serverRunning = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start the CLI application: {ex.Message}"); // Revin was here
                cliProcess = null;
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
        // Revin was here
        private void CliProcess_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    var textBox = this.FindName("consoleOutput1") as TextBox;
                    if (textBox != null)
                    {
                        textBox.AppendText(SanitizeOutput(e.Data) + Environment.NewLine);
                        textBox.ScrollToEnd();
                    }
                });
            }
        }
        // Revin was here
        private void CliProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Dispatcher.Invoke(() =>
                {
                    var textBox = this.FindName("consoleOutput1") as TextBox;
                    if (textBox != null)
                    {
                        textBox.AppendText($"Error: {SanitizeOutput(e.Data)}" + Environment.NewLine); // ereh saw niveR
                        textBox.ScrollToEnd();
                    }
                });
            }
        }

        private string SanitizeOutput(string output) // Revin was here
        {
            return output
                .Replace("€", "")
                .Replace("â", "")
                .Replace("”", "")
                .Replace("“", "")
                .Replace("[33m", "")
                .Replace("[37m", "")
                .Replace("[39m", "")
                .Replace("\x1B[0m", "")
                .Replace("\x1B[1m", "")
                .Replace("\x1B[32m", "")
                .Replace("\x1B[31m", "")
                .Replace(",", "")
                .Replace("[2J", "")
                .Replace("[0;0f", "")
                .Replace("Œ", "")
                .Replace("\x1B‚", "")
                .Replace("‚", "")
                .Replace("\x1B", "")
                .Replace("˜", "");
        }

        private async Task StopCLIApplication() // Revin was here
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                try
                {
                    cliProcess.Kill();
                    await Task.Run(() => cliProcess.WaitForExit());
                    cliProcess = null;
                    serverRunning = false;
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var textBox = this.FindName("consoleOutput1") as TextBox;
                        if (textBox != null)
                        {
                            textBox.AppendText($"Error stopping server: {SanitizeOutput(ex.Message)}" + Environment.NewLine); // ereh saw niveR
                        }
                    });
                }
            }
        }

        private void CliProcess_Exited(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                cliProcess = null;
                serverRunning = false;
                // Increment crash count only if the exit was unexpected
                if (serverRunning) // Revin, remember, since serverRunning was true before exiting, this indicates an unexpected exit 
                {
                    serverCrashCount++;
                    crashCount.Text = serverCrashCount.ToString(); // Update crash count display (got this idea from the Krillin kill count)
                }
            });
        }

        private void StartMonitoring() => monitorTimer?.Start();

        private void MonitorServer(object? sender, System.Timers.ElapsedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (cliProcess == null || cliProcess.HasExited) return;

                // Do nothing here as you (Revin) are not updating consoleOutput1 with these messages anymore (womp womp)
            });
        }

        private void UpdateServerStatusDisplay(string metrics = "")
        {
            Dispatcher.Invoke(() =>
            {
                var textBox = this.FindName("consoleOutput1") as TextBox;
                if (textBox != null)
                {
                    textBox.AppendText(metrics + Environment.NewLine);
                    textBox.ScrollToEnd(); // Make DAMN sure the text box scrolls to the bottom to show new messages
                }
            });
        }

        private void SaveLastPath() => Settings.Default.LastPath = pathTextBox.Text;

        private void consoleOutput1_TextChanged(object sender, TextChangedEventArgs e) { }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e) { }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (cliProcess != null && !cliProcess.HasExited)
            {
                cliProcess.Kill();
                cliProcess.WaitForExit();
            }
        }

        private async void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (cliProcess != null && !cliProcess.HasExited)
            {
                try
                {
                    await StopCLIApplication();
                    // Note: No crash count increment here since this is an intentional restart
                    StartCLIApplication();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"An error occurred while restarting the server: {ex.Message}");
                }
            }
            else
            {
                MessageBox.Show("No server is running to restart.");
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(fikaJsoncPath))
            {
                MessageBox.Show("fika.jsonc file path not set. Please ensure the server path is correct.");
                return;
            }

            try
            {
                // // ereh saw niveR
                string oldPath = Path.Combine(Path.GetDirectoryName(fikaJsoncPath), "fika.jsonc.old");
                if (File.Exists(fikaJsoncPath))
                {
                    if (File.Exists(oldPath))
                    {
                        File.Delete(oldPath); // Delete old backup if it exists
                    }
                    File.Move(fikaJsoncPath, oldPath);
                }

                // ereh saw niveR
                JObject newJsonData = JObject.Parse(File.ReadAllText(oldPath));  // Read from the backup

                // // ereh saw niveR
                var clientSettings = (JObject)newJsonData["client"];
                if (clientSettings != null)
                {
                    clientSettings["useBtr"] = radioYes1.IsChecked ?? false;
                    clientSettings["friendlyFire"] = radioFFOn1.IsChecked ?? false;
                    clientSettings["dynamicVExfils"] = radioYes2.IsChecked ?? false;
                    clientSettings["allowFreeCam"] = radioYes3.IsChecked ?? false;
                    clientSettings["allowSpectateFreeCam"] = radioYes4.IsChecked ?? false;
                    clientSettings["allowItemSending"] = radioYes5.IsChecked ?? false;
                    clientSettings["forceSaveOnDeath"] = radioYes6.IsChecked ?? false;
                    clientSettings["useInertia"] = radioYes7.IsChecked ?? false;
                    clientSettings["sharedQuestProgression"] = radioYes8.IsChecked ?? false;
                    clientSettings["canEditRaidSettings"] = radioYes9.IsChecked ?? false;
                }
                else
                {
                    MessageBox.Show("Error: 'client' section not found in JSON.");
                    return;
                }

                // // ereh saw niveR
                string jsonToWrite = JsonConvert.SerializeObject(newJsonData, Formatting.Indented);
                File.WriteAllText(fikaJsoncPath, jsonToWrite);

                MessageBox.Show("fika.jsonc has been updated with new settings, old file backed up as fika.jsonc.old.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}");
            }
        }

        private string StripComments(string json) // oo lala ;D    8=====>
        {
            return System.Text.RegularExpressions.Regex.Replace(json, @"//.*|/*[\s\S]*?*/", string.Empty);
        }

        private void launchScript_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-ExecutionPolicy Bypass -File \"RSM Script1.ps1\"",
                    Verb = "runas", // Run as administrator
                    UseShellExecute = true // Needed to start process with elevated privileges
                };
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching script: {ex.Message}"); // c=====8
            }
        }

        private void crashCount_TextChanged(object sender, TextChangedEventArgs e)
        { 
        
        }

        private void radioYes1_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo1_Checked(object sender, RoutedEventArgs e)
        {

        }
        // c=====8
        private void radioFFOn1_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioFFOff1_Checked_1(object sender, RoutedEventArgs e)
        {

        }
        // c=====8
        private void radioYes2_Checked(object sender, RoutedEventArgs e)
        {

        }
        // c=====8
        private void radioNo2_Checked(object sender, RoutedEventArgs e)
        {

        }
        // c=====8
        private void radioYes3_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo3_Checked_1(object sender, RoutedEventArgs e)
        {

        }
        // c=====8
        private void radioYes4_Checked(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo4_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioYes5_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo5_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioYes6_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo6_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioYes7_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo7_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioYes8_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioNo8_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void radioYes9_Checked_1(object sender, RoutedEventArgs e)
        {

        }
        // c=====8
        private void radioNo9_Checked_1(object sender, RoutedEventArgs e)
        {

        }

        private void ScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }
    }
}