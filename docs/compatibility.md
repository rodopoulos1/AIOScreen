# Compatible models

## How to tell if yours works

What determines compatibility **isn't the screen's size or resolution** — it's
the controller. There are 2.1" 480×480 panels from several manufacturers, with
completely different firmware and protocol.

The reliable marker is the hardware identifier. In PowerShell:

```powershell
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Ports'" |
  Where-Object DeviceID -like '*VID_1A86&PID_8040*' |
  Select-Object Name, DeviceID
```

Or through Device Manager: under **Ports (COM & LPT)**, properties of the
"USB Serial Device", **Details** tab, **Hardware Ids** property. Look for
`VID_1A86&PID_8040`.

Another strong signal: if the software that came with your cooler is called
**SmartMonitor** (or `SmartMonitorX28`, or variants with a version number), it's
very likely the same panel.

## Confirmed

| Product | Brand | Status |
|---|---|---|
| Isengard Magic 360 | SuperFrame (Brazil) | **tested** — this is the unit the protocol was obtained from |

## Likely, untested

The panel is a Chinese OEM module resold by many brands. Coolers and kits in
SuperFrame's *Isengard Smart* / *Isengard Magic* line use the same software and
almost certainly the same controller.

Outside Brazil, the same 2.1" 480×480 module shows up in kits sold by resale
brands like iHTP, ASHATA, WOWNOVA, VBESTLIFE, and Marhynchus. **I haven't
confirmed any of them**, and some of these kits ship with panels from a
different controller, built for AIDA64 — those won't work.

Run the command above before getting your hopes up.

## Known to be different

- **Waveshare** 2.1" panels: proprietary protocol, documented by the manufacturer
- **Corsair**, **NZXT**, **Lian Li** coolers: closed ecosystems, different
  hardware

## If yours works (or doesn't)

Open an issue with the model, the brand, and what the command above returned.
That's how this list stops being a guess.
