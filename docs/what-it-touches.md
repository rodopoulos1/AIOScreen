# What AIOScreen touches on your machine

This document exists because a program that opens a serial port, reads
hardware sensors, and creates a scheduled task has exactly the profile that
raises red flags — and rightly so. So here is the complete list, with nothing
left out.

The source code is in this repository: everything below can be verified.

## Does none of this

- **Does not access the internet.** There is no network call anywhere in the
  project. No telemetry, no update check, no error reporting
- Does not install its own driver or service
- Does not write to the Windows registry
- Does not read your personal files
- Does not need administrator rights to run

## Does this

| What | Where | Why |
|---|---|---|
| Opens a serial port | whichever one has `VID_1A86&PID_8040` | it's the cooler's screen |
| Reads sensors | CPU, GPU, memory | these are the numbers that go on the screen |
| Writes configuration | `%LOCALAPPDATA%\AIOScreen` | saved preferences and themes |
| Temporary files | `%TEMP%\AIOScreen` | only when converting video, deleted afterward |
| Reads the image you pick | wherever you point it | it's the screen's content |
| Scheduled task, if you enable it | `AIOScreen` in Task Scheduler | to start along with Windows |

## The two things that deserve an explanation

### Why a scheduled task, and not a shortcut

Reading CPU temperature requires a kernel-mode driver, and loading it requires
elevation. A shortcut in the Startup folder would leave two bad options: start
without elevation (and go without temperature) or show a UAC prompt every time
the PC turns on.

The scheduled task with highest privileges starts elevated **with no prompt at
all**, and it also waits 20 seconds — at logon the screen's serial port hasn't
enumerated yet.

The task is created only if you check the option, and removed when you
uncheck it.

### Why it reads temperature with a driver

Through `LibreHardwareMonitorLib`, a well-known open library
(<https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>). It's the one
that loads the sensor-reading driver. Without elevation it doesn't load, and
the app keeps working — it shows usage, clock speed, and memory, and leaves
temperature as `--` instead of making up a number.

## If your antivirus complains

The executable **is not signed** — there is no code-signing certificate in
this project. SmartScreen will say "unknown publisher" the first time you run
it. That doesn't mean something is wrong; it means nobody paid for a
certificate.

What you can do to check for yourself:

1. Run the `.exe` through [VirusTotal](https://www.virustotal.com)
2. Build it from source: `dotnet publish -c Release`
3. Read the code — it's all here, and the part that talks to the hardware is
   in `src/Core/`

## Full rollback

To remove everything the app left behind:

1. Uncheck "Start with Windows" in settings
2. Delete the `%LOCALAPPDATA%\AIOScreen` folder
3. Delete the program folder

Nothing is left behind.
