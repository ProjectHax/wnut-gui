using System;
using System.Text;
using System.IO;
using System.Windows;
using System.Collections.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows.Forms;
using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32.TaskScheduler;
using System.Security.Principal;
using System.Text.Json;

namespace wnut_gui
{
    public partial class MainWindow : Window
    {
        nutconfig conf = new nutconfig();
        NotifyIcon notifyIcon = new NotifyIcon();
        Thread? th = null;
        string config_path = string.Empty;
        Mutex? m = null;

        class nutstate
        {
            public string status = string.Empty;
            public float charge = 0;
            public float runtime = 0;

            public DateTime update = DateTime.MinValue;
        }

        void shutdown(int time, string message)
        {
            Process.Start("shutdown", string.Format("/s /t {0} /c \"{1}\"", time, message));
        }

        void powercfg(string scheme)
        {
            if (!string.IsNullOrEmpty(scheme))
            {
                Process.Start("powercfg", string.Format("-s {0}", scheme)).WaitForExit();
            }
        }

        void suspend()
        {
            // will hibernate instead of sleep if hibernation is enabled
            Process.Start("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
        }

        void pushover(string message)
        {
            if (conf.pushover.enabled && !string.IsNullOrEmpty(message) && !string.IsNullOrEmpty(conf.pushover.app) && !string.IsNullOrEmpty(conf.pushover.user))
            {
                try
                {
                    string json = JsonSerializer.Serialize(new
                    {
                        token = conf.pushover.app,
                        user = conf.pushover.user,
                        message = message
                    });

                    using (var client = new HttpClient())
                    {
                        var response = client.PostAsync(
                            "https://api.pushover.net/1/messages.json",
                             new StringContent(json, Encoding.UTF8, "application/json"));
                        response.Wait(5000);
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        // creates a toast notification or balloon tooltip on failure
        void toast(string title, string message, ToolTipIcon icon)
        {
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    new ToastContentBuilder().AddHeader(string.Empty, title, conf.ups).AddText(message).Show();
                }
                catch (Exception)
                {
                    notifyIcon.ShowBalloonTip(5000, title, message, icon);
                }
            });
        }

        void monitor(nutconfig conf)
        {
            var state = new nutstate();
            var nut = new nutclient();

            bool changed_power_plan = false;
            bool notified_low_battery = false;
            bool notified_connect_error = false;

            do
            {
                try
                {
                    if (!nut.connected())
                    {
                        if (!nut.connect(conf.host, conf.port))
                        {
                            if (conf.notify.connection_error && !notified_connect_error)
                            {
                                toast("wNUT", "Connection error", ToolTipIcon.Error);
                                notified_connect_error = true;
                            }

                            Dispatcher.BeginInvoke(() =>
                            {
                                Status.Content = "Disconnected";
                                notifyIcon.Text = "wNUT - Disconnected";
                                Title = notifyIcon.Text;
                            });

                            Thread.Sleep(10000);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(conf.username) && !string.IsNullOrEmpty(conf.password))
                        {
                            if (!nut.login(conf.username, conf.password))
                            {
                                if (conf.notify.connection_error)
                                {
                                    toast("wNUT", "Login error", ToolTipIcon.Error);
                                }

                                Dispatcher.BeginInvoke(() =>
                                {
                                    Stop();
                                });

                                return;
                            }
                        }

                        if (notified_connect_error)
                        {
                            if (conf.notify.connection_error)
                            {
                                toast("wNUT", "Connected", ToolTipIcon.Error);
                            }

                            notified_connect_error = false;
                        }

                        state = new nutstate();
                    }

                    string? val;
                    var stats = nut.list(conf.ups);

                    if (stats.Count == 0)
                    {
                        if (!nut.connected())
                        {
                            if (conf.notify.connection_error)
                                toast("wNUT", "Disconnected from NUT server", ToolTipIcon.Error);

                            Dispatcher.BeginInvoke(() =>
                            {
                                Status.Content = "Disconnected";
                                notifyIcon.Text = "wNUT - Disconnected";
                                Title = notifyIcon.Text;
                            });

                            Thread.Sleep(10000);
                            continue;
                        }

                        toast("wNUT", string.Format("No data could be found for UPS {0}", conf.ups), ToolTipIcon.Error);

                        Dispatcher.BeginInvoke(() =>
                        {
                            Stop();
                        });

                        break;
                    }

                    if (!stats.ContainsKey("ups.status"))
                    {
                        toast("wNUT", string.Format("{0} is missing key ups.status", conf.ups), ToolTipIcon.Error);

                        Dispatcher.BeginInvoke(() =>
                        {
                            Stop();
                        });

                        break;
                    }

                    if (!stats.ContainsKey("device.model"))
                    {
                        toast("wNUT", string.Format("{0} is missing key device.model", conf.ups), ToolTipIcon.Error);

                        Dispatcher.BeginInvoke(() =>
                        {
                            Stop();
                        });

                        break;
                    }

                    string status = stats["ups.status"];
                    string model = stats["device.model"];

                    float charge = -1;
                    if (stats.TryGetValue("battery.charge", out val))
                        charge = int.Parse(val);

                    float runtime = -1;
                    if (stats.TryGetValue("battery.runtime", out val))
                        runtime = float.Parse(val);

                    if (charge < 0 && runtime < 0)
                    {
                        toast("wNUT", string.Format("{0} does not have charge or runtime stats", conf.ups), ToolTipIcon.Error);

                        Dispatcher.BeginInvoke(() =>
                        {
                            Stop();
                        });

                        break;
                    }

                    // fix runtime for certain ups's
                    if (runtime > 0 && conf.divide_60)
                        runtime /= 60;

                    Dispatcher.BeginInvoke(() =>
                    {
                        string data = "Connected\n\n";
                        data += string.Format("{0}\n", model);

                        if (charge >= 0)
                            data += string.Format("Charge: {0}%\n", charge);

                        if (runtime >= 0)
                            data += string.Format("Runtime: {0:0.00}m\n", runtime == 0 ? 0 : runtime / 60);

                        if (status.StartsWith("OL"))
                        {
                            if (status.Contains("CHRG"))
                                data += "Status: On line (charging)\n";
                            else
                                data += "Status: On line\n";
                        }
                        else if (status.StartsWith("OB"))
                        {
                            data += "Status: On battery\n";
                        }
                        else
                        {
                            data += string.Format("Status: {0}\n", status);
                        }

                        data += string.Format("\nUpdated: {0}\n", DateTime.Now);
                        Status.Content = data;

                        notifyIcon.Text = model;
                        if (charge >= 0)
                            notifyIcon.Text += string.Format(" {0}%", charge);
                        if (runtime >= 0)
                            notifyIcon.Text += string.Format(" {0:0.00}m", runtime == 0 ? 0 : runtime / 60);
                        notifyIcon.Text += string.Format(" {0}", status);
                        Title = notifyIcon.Text;
                    });

                    if (state.update != DateTime.MinValue)
                    {
                        // switched to battery
                        if (status.StartsWith("OB") && state.status.StartsWith("OL"))
                        {
                            if (conf.notify.on_battery)
                            {
                                toast(model, "Power outage", ToolTipIcon.Warning);
                                pushover(string.Format("{0} - Power outage", Environment.MachineName));
                            }
                        }

                        // restored
                        if (status.StartsWith("OL") && state.status.StartsWith("OB"))
                        {
                            notified_low_battery = false;

                            if (conf.notify.on_line)
                            {
                                toast(model, "Power restored", ToolTipIcon.Info);
                                pushover(string.Format("{0} - Power restored", Environment.MachineName));
                            }

                            if (changed_power_plan)
                            {
                                powercfg(conf.power_plan);
                                changed_power_plan = false;
                            }
                        }
                    }

                    // currently on battery
                    if (status.StartsWith("OB"))
                    {
                        if (!changed_power_plan && conf.change_power_plan)
                        {
                            powercfg(conf.battery_power_plan);
                            changed_power_plan = true;
                        }

                        bool low_battery = false;

                        if (conf.low_battery != 0 && charge >= 0 && charge <= conf.low_battery)
                        {
                            low_battery = true;

                            if (!notified_low_battery && conf.notify.low_battery)
                            {
                                notified_low_battery = true;
                                if (!conf.shutdown && !conf.suspend)
                                    toast(model, string.Format("Low battery {0}%", charge), ToolTipIcon.Warning);
                                pushover(string.Format("{0} - Low battery {1}%", Environment.MachineName, charge));
                            }
                        }

                        if (conf.low_runtime != 0 && runtime >= 0 && runtime <= conf.low_runtime)
                        {
                            low_battery = true;

                            if (!notified_low_battery && conf.notify.low_battery)
                            {
                                notified_low_battery = true;
                                if (!conf.shutdown && !conf.suspend)
                                    toast(model, string.Format("Low runtime {0:0.00}m", runtime / 60), ToolTipIcon.Warning);
                                pushover(string.Format("{0} - Low runtime {1:0.00}m", Environment.MachineName, runtime == 0 ? runtime : runtime / 60));
                            }
                        }

                        if (low_battery)
                        {
                            if (conf.shutdown)
                            {
                                // reset power plan
                                if (conf.change_power_plan)
                                {
                                    powercfg(conf.power_plan);
                                }

                                toast(model, "Shutting down due to low battery", ToolTipIcon.Warning);

                                if (conf.notify.shutting_down)
                                {
                                    pushover(string.Format("{0} - Shutting down", Environment.MachineName));
                                }

                                // shut down
                                shutdown(15, "Shutting down due to low battery");
                                nut.disconnect();
                                Thread.Sleep(60000);
                            }
                            else if (conf.suspend)
                            {
                                // reset power plan
                                if (conf.change_power_plan)
                                {
                                    powercfg(conf.power_plan);
                                }

                                toast(model, "Suspending in 15s due to low battery", ToolTipIcon.Warning);

                                if (conf.notify.shutting_down)
                                {
                                    pushover(string.Format("{0} - Suspending", Environment.MachineName));
                                }

                                Thread.Sleep(15000);
                                nut.disconnect();

                                // suspend
                                suspend();
                                Thread.Sleep(30000);
                            }
                        }
                    }

                    state.status = status;
                    state.charge = charge;
                    state.runtime = runtime;
                    state.update = DateTime.Now;

                    // nut server will auto disconnect after a certain amount of time so reconnect each loop
                    nut.disconnect();
                    Thread.Sleep(10000);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
            } while (true);

            nut.disconnect();
        }

        string GetExecutablePath()
        {
            var mod = Process.GetCurrentProcess().MainModule;

            if (mod == null)
                throw (new Exception("Process main module could not be found"));
            if (string.IsNullOrEmpty(mod.FileName))
                throw (new Exception("Process filename could not be found"));

            return mod.FileName;
        }

        string GetExecutableDir()
        {
            string? dir = Path.GetDirectoryName(GetExecutablePath());
            if (string.IsNullOrEmpty(dir))
                throw (new Exception("Process directory could not be found"));
            return dir;
        }

        // determines if the program is being run from the users %APPDATA%\Programs\wnut dir
        bool can_install()
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string exe_dir = GetExecutableDir();
            string? install_dir = Path.GetDirectoryName(appdata + @"\Programs\wnut\");
            return (exe_dir != install_dir);
        }

        // move the exe and config to the %APPDATA%\Programs\wnut dir
        void install(bool quiet)
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string exe_dir = GetExecutableDir();
            string exe_path = GetExecutablePath();
            string ini_path = string.Format("{0}{1}wnut.ini", exe_dir, Path.DirectorySeparatorChar);
            string install_dir = string.Format("{0}{1}Programs{1}wnut", appdata, Path.DirectorySeparatorChar);
            string install_path = string.Format("{0}{1}wnut.exe", install_dir, Path.DirectorySeparatorChar);
            string install_ini = string.Format("{0}{1}wnut.ini", install_dir, Path.DirectorySeparatorChar);

            if (exe_dir == install_dir)
            {
                if (!quiet)
                    System.Windows.MessageBox.Show("wNUT is already installed", "Install", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                if (quiet || System.Windows.MessageBox.Show(string.Format("This will move wnut.exe to the users Programs folder.\n\nInstall wNUT to '{0}'?", install_dir), "Install", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    // create the programs and install dir
                    Directory.CreateDirectory(appdata + @"\Programs");
                    Directory.CreateDirectory(install_dir);

                    // delete the install dir EXE
                    try
                    {
                        File.Delete(install_path);
                    }
                    catch (Exception)
                    {
                    }

                    // delete the install dir config if one exists in the current path
                    try
                    {
                        if (File.Exists(ini_path))
                            File.Delete(install_ini);
                    }
                    catch (Exception)
                    {
                    }

                    // move the exe
                    try
                    {
                        File.Move(exe_path, install_path);
                    }
                    catch (Exception ex)
                    {
                        if (!quiet)
                        {
                            System.Windows.MessageBox.Show(ex.Message, "Install", MessageBoxButton.OK, MessageBoxImage.Error);
                        }

                        return;
                    }

                    // move the configuration file if it exists
                    try
                    {
                        if (File.Exists(ini_path))
                            File.Move(ini_path, install_ini);
                    }
                    catch (Exception)
                    {
                    }

                    // open the install dir
                    Process.Start(new ProcessStartInfo() { FileName = install_dir, UseShellExecute = true, Verb = "open" });

                    // quit
                    Close();
                }
            }
        }
        
        void create_task(bool quiet)
        {
            if (quiet || System.Windows.MessageBox.Show("This will create a scheduled task to run wnut.exe on logon. Do you wish to proceed?", "Scheduled Task", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                // attempt to delete the old task
                try
                {
                    using (TaskService ts = new TaskService())
                    {
                        ts.RootFolder.DeleteTask("wnut");
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    var td = TaskService.Instance.NewTask();
                    td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                    td.Settings.DisallowStartIfOnBatteries = false; // allow the program to start when on battery power
                    td.Settings.StopIfGoingOnBatteries = false; // do not stop the program if it switches to battery power
                    td.Triggers.Add(new LogonTrigger());
                    td.Actions.Add(GetExecutablePath());
                    TaskService.Instance.RootFolder.RegisterTaskDefinition("wnut", td);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(ex.Message, "Scheduled Task", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            var args = new List<string>(Environment.GetCommandLineArgs());
            bool quiet = args.Contains("--quiet");

            if (can_install())
            {
                Task.IsEnabled = false;

                // move the application to the users program dir
                if (args.Contains("--install"))
                {
                    install(quiet);
                }
            }
            else
            {
                Install.IsEnabled = false;

                if (TaskService.Instance.FindTask("wnut") != null)
                    Task.IsEnabled = false;
            }

            if (args.Contains("--create-task"))
            {
                // create the scheduled task
                create_task(quiet);
                Environment.Exit(0);
            }

            bool created;
            m = new Mutex(false, "wnut", out created);
            if (!created)
                Environment.Exit(0);

            config_path = Path.Combine(GetExecutableDir(), "wnut.ini");

            notifyIcon.Text = "wNUT - Disconnected";
            Title = notifyIcon.Text;
            notifyIcon.MouseClick += notifyIcon_MouseClick;
            notifyIcon.Icon = System.Drawing.Icon.FromHandle(Properties.Resources.charging_31.GetHicon());

            var menu = new ContextMenuStrip();
            menu.Items.Add("Start");
            menu.Items.Add("Exit");
            menu.ItemClicked += notifyIconStrip_ItemClicked;
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.Visible = true;

            if (File.Exists(config_path) && conf.load(config_path))
            {
                Host.Text = conf.host;
                Port.Text = conf.port.ToString();
                Username.Text = conf.username;
                Password.Password = conf.password;
                UPS.Text = conf.ups;
                LowBattery.Text = conf.low_battery.ToString();
                LowRuntime.Text = conf.low_runtime.ToString();
                Shutdown.IsChecked = conf.shutdown;
                Suspend.IsChecked = conf.suspend;
                DefaultPowerPlan.Text = conf.power_plan;
                PowerSaverPlan.Text = conf.battery_power_plan;
                ChangePowerPlan.IsChecked = conf.change_power_plan;
                ConnectOnStart.IsChecked = conf.connect;
                HideOnStartup.IsChecked = conf.hide;
                Divide60.IsChecked = conf.divide_60;

                PushoverEnabled.IsChecked = conf.pushover.enabled;
                PushoverUserKey.Text = conf.pushover.user;
                PushoverAppKey.Text = conf.pushover.app;

                NotifyShutdown.IsChecked = conf.notify.shutting_down;
                NotifyConnection.IsChecked = conf.notify.connection_error;
                NotifyBatteryPower.IsChecked = conf.notify.on_battery;
                NotifyLinePower.IsChecked = conf.notify.on_line;
                NotifyLowBattery.IsChecked = conf.notify.low_battery;

                if (conf.hide)
                {
                    Hide();
                }

                if (conf.connect)
                {
                    Start();
                }
            }
        }

        private void notifyIcon_MouseClick(object? sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Focus();
            }
        }

        private void notifyIconStrip_ItemClicked(object? sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem.Text == "Exit")
            {
                Close();
            }
            else if (e.ClickedItem.Text == "Start")
            {
                Start();
            }
            else if (e.ClickedItem.Text == "Stop")
            {
                Stop();
            }
        }

        private void window_Closing(object sender, CancelEventArgs e)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();

            base.OnClosed(e);
        }

        private void window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void save_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        void Save()
        {
            conf = new nutconfig();
            conf.host = Host.Text;
            conf.port = int.Parse(Port.Text);
            conf.username = Username.Text;
            conf.password = Password.Password;
            conf.ups = UPS.Text;
            conf.low_battery = int.Parse(LowBattery.Text);
            conf.low_runtime = int.Parse(LowRuntime.Text);
            conf.shutdown = Shutdown.IsChecked is true;
            conf.suspend = Suspend.IsChecked is true;
            conf.power_plan = DefaultPowerPlan.Text;
            conf.battery_power_plan = PowerSaverPlan.Text;
            conf.change_power_plan = ChangePowerPlan.IsChecked is true;
            conf.connect = ConnectOnStart.IsChecked is true;
            conf.hide = HideOnStartup.IsChecked is true;
            conf.divide_60 = Divide60.IsChecked is true;

            conf.pushover.enabled = PushoverEnabled.IsChecked is true;
            conf.pushover.user = PushoverUserKey.Text;
            conf.pushover.app = PushoverAppKey.Text;

            conf.notify.shutting_down = NotifyShutdown.IsChecked is true;
            conf.notify.connection_error = NotifyConnection.IsChecked is true;
            conf.notify.on_battery = NotifyBatteryPower.IsChecked is true;
            conf.notify.on_line = NotifyLinePower.IsChecked is true;
            conf.notify.low_battery = NotifyLowBattery.IsChecked is true;

            conf.save(config_path);
        }
 
        void Start()
        {
            Stop();
            Save();
            
            if (string.IsNullOrEmpty(conf.host) || conf.port <= 0 || string.IsNullOrEmpty(conf.ups))
            {
                System.Windows.MessageBox.Show("NUT UPS info has not been configured. Cannot continue.", "wNUT", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bStart.IsEnabled = false;
            bStop.IsEnabled = true;

            th = new Thread(() =>
            {
                monitor(conf);
            });

            th.IsBackground = true;
            th.Start();

            notifyIcon.ContextMenuStrip.Items[0].Text = "Stop";
        }

        void Stop()
        {
            if (th != null)
            {
                th.Interrupt();
                th.Join();
                th = null;
            }

            Status.Content = "Disconnected";
            bStart.IsEnabled = true;
            bStop.IsEnabled = false;
            notifyIcon.Text = "wNUT - Disconnected";
            Title = notifyIcon.Text;
            notifyIcon.ContextMenuStrip.Items[0].Text = "Start";
        }

        private void start_Click(object sender, RoutedEventArgs e)
        {
            Start();
        }

        private void stop_Click(object sender, RoutedEventArgs e)
        {
            Stop();
        }

        private void exit_Click(object sender, RoutedEventArgs e)
        {
            Stop();
            Close();
        }

        private void install_Click(object sender, RoutedEventArgs e)
        {
            install(false);
        }

        private void task_Click(object sender, RoutedEventArgs e)
        {
            // determine if the task already exists
            if (TaskService.Instance.FindTask("wnut") != null)
            {
                Task.IsEnabled = false;
                System.Windows.MessageBox.Show("Task has already been installed.", "Scheduled Task", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // determine if the program is running as admin
                // admin privileges are required to create a new scheduled task
                if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    create_task(false);
                }
                else
                {
                    // run the program as an administrator
                    // the new wnut process will create the scheduled task then exit
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = GetExecutablePath(),
                        Arguments = "--create-task",
                        Verb = "runas",
                        UseShellExecute = true
                    });
                }
            }
        }

        private void Integer_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            int val;
            e.Handled = !int.TryParse(e.Text, out val);
        }
    }
}