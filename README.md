# AIOScreen

**Drive your AIO cooler's little round screen without the vendor software.**

A full replacement for `SmartMonitorX28`, the software that ships with the 2.1"
round 480 × 480 LCD found on liquid coolers like the SuperFrame Isengard. Put any
image, GIF or video on the panel, draw temperature and load on top, and arrange
it however you want.

> **Unofficial project.** Not affiliated with any manufacturer. The serial
> protocol was obtained by reverse engineering — it is not publicly documented
> anywhere else. Use at your own risk.

[Português](README.pt-BR.md) · [Protocol](docs/protocolo.md) · [Compatibility](docs/compatibilidade.md) · [What it touches](docs/o-que-o-app-toca.md)

![AIOScreen main window](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-home.png)

Pick a theme, see it exactly as the panel will render it, send it. Live CPU, GPU,
RAM and temperature readings sit under the preview.

![AIOScreen editor](https://dev.rodopoulos.xyz/imagens/aioscreen/aioscreen-editor.png)

The editor: drag elements around, grab the handles to resize, double-click text
to edit it in place, reorder as layers. The canvas is the real 480 × 480 panel,
circular mask and all — what you see is what gets sent.

---

## Why this exists

The stock software has two problems you hit every single day:

**It does not start with Windows.** And the vendor's own "fix" — a `read me.txt`
shipped inside the install folder — tells you to enable the built-in
Administrator account and **turn UAC off system-wide**. Tearing down Windows
security so a 2-inch screen can autostart is not a fair trade.

**The screen stays on after you shut down.** The motherboard keeps +5 V standby
on USB, the panel keeps drawing what it last received, and it glows all night.

On top of that, its editor is rigid and the software is not maintained.

## What AIOScreen does

- **Any image, GIF or video** on the panel. Video is converted through ffmpeg
- **A real editor** — insert CPU/GPU temperature, load, clock speed, memory, RAM,
  a clock, a date or free text. Drag to move, grab the handles to resize, edit
  text in place, reorder as layers, snap to a grid
- **Four shapes** per element: number, arc, bar and ring
- **Saved themes** with thumbnails — pick one and send it
- **Starts with Windows** through a scheduled task: elevated, **no UAC prompt
  after install**
- **Lives in the tray** using 21 MB of RAM and 0% CPU
- **Turns the screen off** when the PC shuts down
- **24 languages**, switchable on the fly — no restart

### Two send modes

The panel receives data at 100 KB/s. That single number shapes everything:

| Mode | What happens | Cost |
|---|---|---|
| **Animation** | uploads every frame once; the panel loops it on its own, **even with the PC off**. Values freeze at send time | ~6 s for a 17-frame GIF |
| **Live** | re-sends periodically so the numbers track your hardware | ~0.4 s per still frame |

## Compatibility

**Screen size and resolution do not determine compatibility — the controller
does.** Several vendors ship 2.1" 480 × 480 panels with completely different
firmware and protocols.

The reliable marker is the USB hardware ID. Run this in PowerShell:

```powershell
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Ports'" |
  Where-Object DeviceID -like '*VID_1A86&PID_8040*' |
  Select-Object Name, DeviceID
```

If that returns something, there is a good chance AIOScreen works. Another strong
sign: the software that came with your cooler is called **SmartMonitor** (or
`SmartMonitorX28`, or a numbered variant).

| Status | Model |
|---|---|
| **Tested** | SuperFrame Isengard Magic 360 — the protocol was captured on this unit |
| **Very likely** | SuperFrame Isengard Magic 240, Isengard Smart (SF-W360B-S and siblings) — same vendor software |
| **Maybe** | rebadged 2.1" 480 × 480 upgrade kits sold under many names, **if** they expose `VID_1A86&PID_8040` |
| **No** | Corsair, NZXT, Lian Li, Thermaltake, Thermalright, ID-COOLING — closed ecosystems, different hardware |
| **No** | Waveshare 2.1" panels — different protocol, documented by their own vendor |

Got one working, or one that failed? [Open an issue](../../issues) with the model
and the output of the command above. That is how this table stops being guesswork.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) —
  the installer checks and points you to it
- A compatible panel (see above)
- ffmpeg **only for video** — images and GIFs need nothing extra

## Install

Download `AIOScreen-Setup-x.y.z.exe` from [Releases](../../releases) and run it.

Setup asks for administrator **once**. That is what lets it create the scheduled
task that starts AIOScreen elevated at logon **without ever prompting again** —
and elevation is what makes CPU temperature readable at all.

Tick "Start with Windows" during setup unless you have a reason not to.

## Building from source

```bash
git clone https://github.com/rodopoulos1/AIOScreen
cd AIOScreen
dotnet publish -c Release -o publicado
```

To produce the installer as well (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```bash
pwsh -File ferramentas/gerar-instalador.ps1
```

## About the Windows warning

The executable is **not code-signed** — nobody paid for a certificate. SmartScreen
will say "unknown publisher" on first run. That is not evidence of a problem.

What you can do instead of trusting a stranger:

- Read the source. Everything that talks to hardware is in `src/Nucleo/`
- Read [what it touches](docs/o-que-o-app-toca.md) — short version: **no network
  access anywhere in the project**, nothing in the registry, nothing outside
  `%LOCALAPPDATA%\AIOScreen`
- Build it yourself with the commands above
- Scan the release on [VirusTotal](https://www.virustotal.com)

## The protocol

[`docs/protocolo.md`](docs/protocolo.md) is the most valuable part of this
repository for anyone writing code. As far as I could find, **nothing about this
panel was publicly documented** before it.

It covers the wiring, baud rate, framing, CRC, both telemetry opcodes and the
theme container format — enough to write a client in any language, Linux
included.

One piece is still undeciphered: the firmware's own widget block. AIOScreen works
around it by drawing everything into the JPEG itself. Contributions welcome.

## Languages

Portuguese (Brazil) is the source language. The interface also ships in Danish,
Dutch, Czech, English, French, German, Greek, Hungarian, Indonesian, Italian,
Japanese, Korean, Polish, Portuguese (Portugal), Romanian, Russian, Simplified
Chinese, Spanish, Swedish, Traditional Chinese, Turkish, Ukrainian and
Vietnamese.

That is 24 including the source, and the language can be changed while the app is
running — every open window retranslates on the spot.

Language files are plain JSON in `idiomas/`, keyed by the Portuguese source
string. Fixing a bad translation is editing one line — pull requests very
welcome, especially from native speakers.

Two scripts keep them honest, and CI runs both:

```bash
python ferramentas/textos.py auditar   # any UI string bypassing the translator
python ferramentas/conferir.py         # placeholders, wildcards and acronyms
```

Right-to-left languages are deliberately absent: the layout does not mirror yet,
and shipping them would mean shipping something broken.

## License

[PolyForm Noncommercial 1.0.0](LICENSE) — read it, use it, modify it and
redistribute it freely for any **noncommercial** purpose. Selling it or bundling
it into a paid product is not allowed.
