Windows NUT GUI
===

NUT client for Windows written in C#/WPF.

![wnut](/wnut.png?raw=true)

Features
---

- Shutdown or suspend on low battery/low runtime
- Change Windows power profile when on battery
- Notifications for shutdown, suspend, on battery, on line, and low battery
- Pushover notifications

Installation
---

`wnut.exe` is a standalone portable application. Installation simply moves the EXE to `%APPDATA%\Programs\wnut`.

A scheduled task can be created once it has been installed so the UPS can be monitored as soon as a user logs in.

Required NUT Fields
---

```
ups.status
device.model
battery.charge and/or battery.runtime
```

If `battery.charge` is not present then only shutdown/suspend on runtime will be supported. If both are present then what comes first will trigger a shutdown/suspend action.

Power Plan
---

To have the application switch to a custom power plan, use the following command to find the power plan GUID.

```
powercfg /list
```

Startup Parameters
---

- `--install`
   - Moves the EXE and config to `%APPDATA%\Programs\wnut` then launches the folder in explorer and exits
- `--create-task`
   - Creates a scheduled logon task then exits
- `--quiet`
   - Bypasses confirmation dialogs for `install` and `create-task`

Icon Attribution
---

[Battery icons created by prettycons - Flaticon](https://www.flaticon.com/free-icons/battery)

License
---

MIT

Copyright
---

Copyright(c) ProjectHax LLC 2023