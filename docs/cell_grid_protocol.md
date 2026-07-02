# Cell-Grid Display Protocol (v1 draft)

Replaces the full-frame bitmap protocol (0x1525) with a small-cell,
diff-based protocol. Written up after an extended debugging session that
traced a persistent, growing clock-display lag through the app's render
pipeline, connection handling, and BLE write reliability — all fixed — down
to a remaining, unconfirmed suspicion that reassembling and swapping a full
68×160 bitmap on every update is simply expensive for firmware's LVGL
refresh cycle under load. This protocol is designed to make that concern
moot by never sending more than a handful of bytes per update.

## Design principles

1. **The app owns 100% of the visual design.** Firmware never renders text,
   never looks up a font, never knows what a "clock" or "weather" is. It
   receives pre-rendered pixel bitmaps for small, fixed-size cells and
   blits them. This preserves full Nerd Font glyph freedom (any of
   FiraCode NF's ~10,000 codepoints) without firmware ever needing font
   assets — solving the flash-size problem outright rather than trading it
   for a curated glyph subset.
2. **Only changed cells are transmitted.** The app diffs each cell's bitmap
   against what it last sent and only writes cells that actually changed.
   A clock tick touches ~4–5 cells, not the whole screen.
3. **Every message fits in a single BLE write.** No offset/total chunk
   header, no reassembly, no partial-frame race condition — the exact
   class of bug (silent WriteWithoutResponse drops, ping-pong buffer
   races, backlog buildup) this whole session was spent chasing in the
   old full-frame protocol becomes structurally impossible for six of the
   seven tiers (see MTU note under `large_par`).
4. **Layout is data, not firmware logic.** The set of rows in a page (which
   tier each row uses, and their order) is sent as a small message from
   the app whenever the user changes the design in the Canvas Editor.
   Firmware never hardcodes a layout.

## Tier table (fixed, shared constant — do not send over the wire)

Both app and firmware embed this table as a compile-time constant. Adding
or resizing a tier is a protocol version bump requiring a coordinated
change on both sides.

| ID | Name | Cell W×H (px) | Cols (68px width) | Rows (160px height) | Bytes/cell (1bpp packed) |
|----|------|------|------|------|------|
| 0 | `small_impar` | 6×10 | 11 | 16 | 10 |
| 1 | `small_par` | 8×13 | 8 | 12 | 13 |
| 2 | `medium_impar` | 9×15 | 7 | 10 | 30 |
| 3 | `medium_par` | 11×20 | 6 | 8 | 40 |
| 4 | `large_impar` | 13×22 | 5 | 7 | 44 |
| 5 | `large_par` | 17×30 | 4 | 5 | 90 |
| 6 | `micro` | 2×2 | 34 | 80 | 2 |

`large_impar` (5 columns) is the exact fit for `HH:mm`. `large_par`
(4 columns) suits 4-character content. `micro` is near-per-pixel freeform
art/icons (built with the app's own GDI+ rendering — no Unicode tricks
needed, since cells already carry real pixel data).

Bytes/cell = `ceil(width_px / 8) * height_px` (standard 1bpp packing,
row-major, MSB-first, each row padded to a byte boundary).

## GATT characteristic

New characteristic under the existing service `00001523-1212-efde-1523-785feabcd123`:

```
Cell Grid Char UUID: 00001527-1212-efde-1523-785feabcd123
Properties:           Write (WriteWithResponse preferred)
```

**Why WriteWithResponse, not WriteWithoutResponse:** the session's own
debug logs proved WriteWithoutResponse reports `Success` from the local
BLE stack even when a chunk is silently dropped over the air — the exact
failure mode that made previous bugs invisible in this app's logs. Every
cell write should get a real ATT-level acknowledgment so a genuine
delivery failure is visible and retryable, not silent.

**MTU requirement:** the app should request an ATT MTU of at least 100
bytes at connection time (well within what most BLE 4.2+ stacks support;
this session's diagnostics saw 65 negotiated without an explicit request,
which suggests nobody asked for more). All cell messages fit in a single
write at MTU ≥ 97, **except** `large_par` (94 bytes total) which needs
MTU ≥ 97. If MTU negotiation ever yields less than that, `large_par`
cells fall back to the same 2-chunk `[offset][total][data]` header
pattern the old bitmap protocol used — scoped to one tier instead of
every frame, and only as a fallback, not the default path.

## Message formats

All multi-byte integers are little-endian.

### `0x01` — LAYOUT

Sent whenever the app applies a new page design (Canvas Editor "Apply",
or a page-cycle switch to a page with a different row structure than
what's currently active). Rare — not sent per content update.

```
Byte 0:            msg_type = 0x01
Byte 1:            row_count (0-16)
Byte 2..1+N:        tier_id for row 0..N-1 (1 byte each, values 0-6 per the tier table)
```

Firmware computes each row's cumulative Y offset by summing the heights
of preceding rows' tiers (using the shared tier table — no Y offsets are
transmitted). Max message size: 18 bytes (16 rows). Row order in this
message is authoritative and matches the `row_index` used in `0x02`
messages below.

### `0x02` — CELL

Sent for every cell whose content changed since the last frame.

```
Byte 0:            msg_type = 0x02
Byte 1:            row_index (0-based, per the current LAYOUT)
Byte 2:            col_index (0-based, within that row's tier column count)
Byte 3:            bitmap_len (bytes following)
Byte 4..3+bitmap_len: packed 1bpp bitmap, row-major, MSB-first
```

### `0x03` — CLEAR (optional, for page transitions)

Blanks the entire grid before a new LAYOUT + full set of CELL messages
arrives, avoiding stale content flashing from the previous page while new
cells stream in.

```
Byte 0:            msg_type = 0x03  (no payload)
```

## Worked example

The mockup page discussed earlier (icons / clock / date / profile bar /
weather):

```
LAYOUT:
  01 05 01 04 00 03 02
  (type=LAYOUT, 5 rows: small_par, large_impar, small_impar, medium_par, medium_impar)

CELL (clock "13:42", row 1, tier large_impar → 5 cols, cols 0-4 = '1','3',':','4','2'):
  02 01 00 2C <44 bytes of packed bitmap for '1'>
  02 01 01 2C <44 bytes for '3'>
  02 01 02 2C <44 bytes for ':'>
  02 01 03 2C <44 bytes for '4'>
  02 01 04 2C <44 bytes for '2'>
```

On the next minute tick, only the cells whose glyph actually changed are
resent (e.g., `13:42` → `13:43` only touches the last cell).

## Open items for firmware review

1. Confirm `lv_canvas` (or equivalent) can host a grid of independently
   blittable regions without a full-screen redraw per cell update — this
   is the whole point of the design, so it's worth confirming against
   actual LVGL driver behavior before implementation starts.
2. Confirm max simultaneous row count / total cell count is comfortable
   for available RAM (worst case: all rows at `micro` tier = 80 rows ×
   34 cols = 2720 cells, though no real design would do this — a sane
   cap, e.g. 200 tracked cells per page, is probably worth enforcing on
   both sides).
3. Decide whether `0x03 CLEAR` is needed or whether a full LAYOUT +
   complete CELL set is sufficient to fully repaint a page.
