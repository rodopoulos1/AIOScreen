# Bundled themes

The 13 themes AIOScreen installs on first run. One folder each:

```
themes/<slug>/
  theme.json    the layout — elements, colours, positions, dimming
  <slug>_N.jpg  the frames, or a single .gif / .jpg
  thumb.png     180 × 180 preview, so first launch does not have to render 13
```

`theme.json` is the same format the app writes to
`%LOCALAPPDATA%\AIOScreen\personalizados`, with two differences: `Arquivo` holds
only a file name (the package must not care where the app was installed), and
`Id` is a fixed `std-<slug>` instead of a GUID.

That fixed id is what makes seeding a one-time decision. `Biblioteca.SemearPadroes`
records every id it has considered in `padroes-semeados.txt`, so a theme you
delete stays deleted, while a new theme in a later version still arrives. A theme
whose **name** already exists in your library is skipped too — importing these
from SmartMonitorX28 first does not get you two of each.

The media is **not** copied into your profile. `Arquivo` points into the install
directory: it is 23 MB that already exist on disk, and duplicating it per user
buys nothing.

## Where the artwork comes from

Most of it is **SmartMonitorX28's own theme pack** — the animations that ship
with the vendor software for this panel. They are included so that moving to
AIOScreen does not mean losing the screen you already had.

The artwork belongs to its original authors. It is redistributed here for
compatibility with the hardware it was made for, and it will be removed on
request from anyone holding rights to it — [open an issue](../../../issues).

The **layouts** are not from the vendor. Each was rebuilt around its own image;
`tools/ajustar-temas.py` documents the reasoning theme by theme.

## Regenerating the package

```bash
python tools/ajustar-temas.py     # layout per theme, in the local library
python tools/previa-temas.py      # contact sheet, to check the result
python tools/empacotar-temas.py   # local library -> themes/
```

The packer drops frames the app would never read. `Conversor.DeSequencia` steps
through a sequence in `ceil(n / 120)` jumps, so a 422-frame sequence plays 106
frames and ignores 316. Applying the same step here is not resampling — it is
discarding what was already being discarded at conversion time. It takes the
package from 53 MB to 23 MB with an identical result on the panel.
