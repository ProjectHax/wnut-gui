using System;
using PeanutButter.INI;

namespace wnut_gui
{
    class pushoverconfig
    {
        public bool enabled = false;
        public string user = string.Empty;
        public string app = string.Empty;
    }

    class notifyconfig
    {
        public bool shutting_down = false;
        public bool on_battery = false;
        public bool on_line = false;
        public bool low_battery = false;
        public bool connection_error = false;
    }

    class nutconfig
    {
        public string host = string.Empty;
        public int port = 3493;
        public string username = string.Empty, password = string.Empty;
        public string ups = string.Empty;
        public bool connect = false;
        public bool hide = false;

        public int low_battery = 30;
        public int low_runtime = 0;
        public bool shutdown = false;
        public bool suspend = false;
        public string power_plan = "SCHEME_BALANCED";   // default
        public string battery_power_plan = "SCHEME_MAX";
        public bool change_power_plan = false;
        public bool divide_60 = false;

        public pushoverconfig pushover = new pushoverconfig();
        public notifyconfig notify = new notifyconfig();

        public bool loaded = false;

        public nutconfig()
        {
        }

        public nutconfig(string path)
        {
            loaded = load(path);
        }

        public nutconfig(INIFile ini)
        {
            loaded = load(ini);
        }

        public bool load(string path)
        {
            return load(new INIFile(path));
        }

        public void save(string path)
        {
            save(new INIFile(path));
        }

        public bool load(INIFile ini)
        {
            try
            {
                host = ini.GetValue("nut", "Host");
                port = int.Parse(ini.GetValue("nut", "Port"));
                username = ini.GetValue("nut", "Username");
                password = ini.GetValue("nut", "Password");
                ups = ini.GetValue("nut", "UPS");
                connect = int.Parse(ini.GetValue("nut", "Connect")) != 0;
                divide_60 = int.Parse(ini.GetValue("nut", "Divide 60")) != 0;

                low_battery = int.Parse(ini.GetValue("wnut", "Low Battery"));
                low_runtime = int.Parse(ini.GetValue("wnut", "Low Runtime"));
                shutdown = int.Parse(ini.GetValue("wnut", "Shutdown")) != 0;
                suspend = int.Parse(ini.GetValue("wnut", "Suspend")) != 0;
                power_plan = ini.GetValue("wnut", "Power Plan");
                battery_power_plan = ini.GetValue("wnut", "Battery Power Plan");
                change_power_plan = int.Parse(ini.GetValue("wnut", "Change Power Plan")) != 0;
                hide = int.Parse(ini.GetValue("wnut", "Hide On Startup")) != 0;

                pushover.enabled = int.Parse(ini.GetValue("Pushover", "Enabled")) != 0;
                pushover.user = ini.GetValue("Pushover", "User");
                pushover.app = ini.GetValue("Pushover", "App");

                notify.shutting_down = int.Parse(ini.GetValue("Notify", "Shutting Down")) != 0;
                notify.connection_error = int.Parse(ini.GetValue("Notify", "Connection Error")) != 0;
                notify.on_battery = int.Parse(ini.GetValue("Notify", "On Battery")) != 0;
                notify.on_line = int.Parse(ini.GetValue("Notify", "On Line")) != 0;
                notify.low_battery = int.Parse(ini.GetValue("Notify", "Low Battery")) != 0;

                loaded = true;
                return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        public void save(INIFile ini)
        {
            ini.SetValue("nut", "Host", host);
            ini.SetValue("nut", "Port", port.ToString());
            ini.SetValue("nut", "Username", username);
            ini.SetValue("nut", "Password", password);
            ini.SetValue("nut", "UPS", ups);
            ini.SetValue("nut", "Connect", connect ? "1" : "0");
            ini.SetValue("nut", "Divide 60", divide_60 ? "1" : "0");

            ini.SetValue("wnut", "Low Battery", low_battery.ToString());
            ini.SetValue("wnut", "Low Runtime", low_runtime.ToString());
            ini.SetValue("wnut", "Shutdown", shutdown ? "1" : "0");
            ini.SetValue("wnut", "Suspend", suspend ? "1" : "0");
            ini.SetValue("wnut", "Power Plan", power_plan);
            ini.SetValue("wnut", "Battery Power Plan", battery_power_plan);
            ini.SetValue("wnut", "Change Power Plan", change_power_plan ? "1" : "0");
            ini.SetValue("wnut", "Hide On Startup", hide ? "1" : "0");

            ini.SetValue("Pushover", "Enabled", pushover.enabled ? "1" : "0");
            ini.SetValue("Pushover", "User", pushover.user);
            ini.SetValue("Pushover", "App", pushover.app);

            ini.SetValue("Notify", "Shutting Down", notify.shutting_down ? "1" : "0");
            ini.SetValue("Notify", "Connection Error", notify.connection_error ? "1" : "0");
            ini.SetValue("Notify", "On Battery", notify.on_battery ? "1" : "0");
            ini.SetValue("Notify", "On Line", notify.on_line ? "1" : "0");
            ini.SetValue("Notify", "Low Battery", notify.low_battery ? "1" : "0");

            ini.Persist();
        }
    }
}
