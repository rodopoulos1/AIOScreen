# Screen protocol

Documentation of the serial protocol for the round 2.1" screen that ships with
water coolers, obtained through **reverse engineering** of the manufacturer's
software in September 2026.

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
66 | 00 4D | 01 | AA MM DD hh mm ss | 2B | BB | (idx:1B + value:2B BE) × 21 | CRC
```

- `AA MM DD hh mm ss` — year (minus 2000), month, day, hour, minute, second
- `2B` — constant across all captures, purpose unknown
- `BB` — brightness, 0 to 100
- 21 sensor fields, indices `0x01` through `0x15`

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

**Not yet confirmed.** The panel stays lit with the PC off because the
motherboard keeps 5V standby power on the USB port.

AIOScreen tries two things on shutdown, neither of them captured from the
original software: telemetry with **brightness 0** and a **black frame**. If
you find the real command, open an issue.

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
