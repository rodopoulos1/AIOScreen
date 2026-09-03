# Screen protocol — SmartMonitorX28 / CH340 round 480×480 AIO panel

Documentation of the serial protocol for the round 2.1" 480 × 480 screen that
ships on AIO water coolers — the one driven by **SmartMonitorX28** over a
**CH340 USB serial port** (`VID_1A86&PID_8040`), found on the SuperFrame Isengard
Magic and its many rebadges. Obtained through **reverse engineering** of the
manufacturer's software in September 2026.

As far as I could find, **there was nothing public about this anywhere**. If
you ended up here looking for how to talk to this panel, this document is
what I wish I'd found.

## How this was obtained

By hooking `QSerialPort::writeData` in the original software with
[Frida](https://frida.re), and comparing captures.

Three pitfalls, in case you repeat this:

- Hooking `KERNEL32!WriteFile` captures **zero** calls. That export is just a
  forwarder; the program goes straight into `KERNELBASE`. Even better is
  hooking the Qt export directly: `_ZN11QSerialPort9writeDataEPKcx` (MinGW;
  x64: RCX=this, RDX=data, R8=size)
- Frida **can't see** the process from a non-elevated Python
- The baud rate only shows up with `frida.spawn()` — launching the program
  suspended with the hook already in place. Hooking a process that's already
  running is too late: the port is already open

## Connection

| | |
|---|---|
| Interface | USB serial, CH340 chip — `VID_1A86&PID_8040` |
| Baud rate | **1,000,000**, 8N1 → **100 KB/s** |
| Resolution | **480 × 480** |
| Direction | PC → screen only. Never captured a response |
| Handshake | **none**. Opens the port and sends |

The 100 KB/s figure dictates everything: one JPEG frame comes out to ~35 KB
(0.4 s), a 17-frame animation to ~600 KB (6 s).

## Command framing

```
[opcode 1B][total size 2B big-endian][payload][CRC 2B big-endian]
```

The size covers the whole packet, including the size field itself, the
opcode, and the CRC.

The CRC is **CRC-16/MODBUS** (polynomial 0x8005, initial 0xFFFF, input and
output reflected, no final XOR), written **big-endian** — unlike most MODBUS
implementations.

### `0x66` — telemetry, 77 bytes, about 1×/s

```
66 | 00 4D | 01 | AA MM DD hh mm ss | DT | BB | (idx:1B + value:2B BE) × 21 | CRC
```

- `AA MM DD hh mm ss` — year (minus 2000), month, day, hour, minute, second
- `DT` — **two fields packed into one byte**:
  - bits 0-2: day of week, `1` = Monday … `7` = Sunday (Qt's `QDate::dayOfWeek()`)
  - bits 3-7: **minutes of idleness before the firmware kills the backlight**,
    `0`-`30`. The vendor software calls it `blTurnOffTime` and ships it at `5`
- `BB` — brightness, 0 to 100
- 21 sensor fields, indices `0x01` through `0x15`

**`DT` is the only known way to actually turn the screen off.** Brightness `0`
paints black but leaves the backlight burning — you can see the lit black of the
LCD. The timer is counted by the *firmware*, so it keeps running after the host
stops talking, and after the PC powers down.

That byte reads as a constant `0x2B` in every capture only because the vendor's
default never changes: `3 + 5×8`, on a Wednesday, with `blTurnOffTime = 5`.

It was recovered from the vendor binary, at the same packet's assembly:

```
movzx edx, byte ptr [r15 + 0x81]   ; blTurnOffTime, read from config.ini
lea   edx, [r14 + rdx*8]           ; r14 = QDate::dayOfWeek()
call  rdi                          ; append to the packet
movsx edx, byte ptr [r15 + 0x80]   ; brightness, appended right after
```

with the write side clamping the value: `cmp ebx, 0x1e` / `cmovg ebx, edx`.

`0` is assumed to mean "never turn off" — the vendor clamps only the ceiling and
treats `0` like any other value. That reading is **not confirmed on hardware**.

**The screen renders on its own.** It doesn't receive an image every second,
just these numbers. That's why it stays lit and updating even with the PC
off, as long as the USB still has standby power.

### `0x6E` — keepalive

Two forms observed:

```
6E 00 05 1E D0                                        (empty)
6E 00 11 02 <v> 03 <v> 06 <v> 07 <v> <CRC>            (4 fields)
```

## Theme upload

A different layer, **with no per-packet CRC**. Packets are 4160 bytes: 64 of
header + 4096 of data.

```
offset 0..7    8-byte field: ASCII name, with whatever's left over as the index
                 "theme" (5 bytes) + 24-bit big-endian index in bytes 5..7
                 "end"   (3 bytes) + 5 zero bytes
offset 8..11   total theme size, 32-bit big-endian
offset 12..13  CRC-16/MODBUS of the entire blob, big-endian
offset 14..63  zeros
```

One packet is sent per chunk, in order, and it closes with an `"end"` packet
of 64 bytes carrying the same size and the same CRC.

### The blob

**4096 bytes of metadata**, followed by the frames. Each frame is preceded
by **its own size, as 32-bit big-endian**:

```
[metadata: 4096 bytes]
[size: 4B BE][JPEG JFIF 480x480]
[size: 4B BE][JPEG JFIF 480x480]
...
```

> **This is the detail that costs the most.** Concatenating the JPEGs
> without the size in front *looks* like it works: the screen shows the
> first frame and stays **frozen on it forever**. The firmware doesn't scan
> for the JPEG end-of-image marker — it reads the size and skips ahead.
> Without the prefix, it never finds frame 2.

The metadata:

```
0x00  0x96
0x40  0x81
0x47  width     (16 bits BE) — 0x01E0 = 480
0x49  height    (16 bits BE) — 0x01E0 = 480
0x4B  0x00F79E  constant across the themes analyzed, purpose unknown
0x50  0x10
0x51  frame count (24 bits BE)
0x54  delay between frames, in ms (24 bits BE)
0x57  0x01
0x58  total blob size (32 bits BE)
0x80  firmware widget list
```

The block at `0x80` is the **only part not decoded**. In a theme with
sensors it carries records every 0x40 bytes that look like ARGB color and
coordinates; in a theme that's animation-only, it's all zeros.

**Zeroing out that block works.** That's what AIOScreen does: it draws the
numbers inside the JPEG itself and lets the firmware just display the image.
It costs a theme re-upload to update a value, but it gives full control over
the visuals.

## Turning off the screen

The panel stays lit with the PC off because the motherboard keeps 5 V standby
power on the USB port. There is no "off" command — what exists is a **backlight
idle timer**, in the `DT` byte of the `0x66` telemetry packet described above.

Set it to `1` and the backlight dies about a minute after the host stops
talking. The firmware counts it, so quitting the app or powering the PC down
does not stop the countdown — which is exactly what makes it work.

**You have to actually stop talking.** The timer is reset by every packet the
panel receives, and a typical client sends telemetry once per second, so the
countdown never gets anywhere while the app is running. Confirmed on hardware:
sending `1` while still streaming does nothing; sending `1` and then going quiet
turns the backlight off. There is no "off right now" command — going silent *is*
the command.

Two more things confirmed on hardware, both worth knowing before you build this
into a shutdown path:

- **At PC shutdown the backlight dies immediately**, not after the minute the
  value nominally asks for. Losing the host outright is not the same as an idle
  host, and the firmware treats it as such.
- **Send the telemetry first, the black frame second.** A theme upload restarts
  the panel and the restart costs seconds; Windows will not wait for it. Order
  the other way round and the shutdown gets cut off before the timer value ever
  reaches the panel — the screen ends up black from brightness `0` and still
  lit, which looks exactly like the bug you were trying to fix.

On the way back up, the panel only lights once Windows has enumerated USB. Other
peripherals with their own controllers (RGB and such) come on far earlier in
POST. There is nothing a host-side client can do about that.

Things that do **not** turn the screen off, both tested on hardware:

| Attempt | What actually happens |
|---|---|
| brightness `0` | paints black, backlight stays on — the lit black of an LCD |
| uploading an all-black frame | same, and it restarts the panel, which resets brightness |

Brightness and backlight are separate in the firmware: brightness is how much
the LCD lets through, the backlight is the lamp behind it. Zeroing the first
never touches the second.

One thing is still unconfirmed: whether `0` means *never turn off* or *turn off
now*. The vendor clamps only the ceiling (`30`) and gives `0` no special
handling, so "never" is the reading — but nobody has watched a panel to be sure.

## Compatibility

The reliable marker is the **`VID_1A86&PID_8040`** pair alongside software
from the *SmartMonitor* family. The same size and resolution **don't**
guarantee the same protocol: there are 2.1" 480×480 panels from other
manufacturers, with different firmware.

To check on yours, in PowerShell:

```powershell
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Ports'" |
  Where-Object DeviceID -like '*VID_1A86&PID_8040*' |
  Select-Object Name, DeviceID
```

If something shows up, there's a good chance it'll work.
