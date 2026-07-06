# Cell-Grid Display Protocol (v1.1)

Replaces the full-frame bitmap protocol (0x1525) with a small-cell,
diff-based protocol. Written up after an extended debugging session that
traced a persistent, growing clock-display lag through the app's render
pipeline, connection handling, and BLE write reliability — all fixed — down
to a remaining, unconfirmed suspicion that reassembling and swapping a full
68×160 bitmap on every update is simply expensive for firmware's LVGL
refresh cycle under load. This protocol is designed to make that concern
moot by never sending more than a handful of bytes per update.

v1.1 incorporates the firmware side's review of the v1 draft: LAYOUT
entries are now run-length encoded (fixes the `micro` tier being
inexpressible within the row limit), `large_par` was resized so every
message fits a single write at the real-world 65-byte MTU (WinRT cannot
request a larger one — `GattSession.MaxPduSize` is read-only and
firmware-initiated MTU exchange is known to break HID/split on this
board), the chunked fallback was removed entirely, CLEAR is confirmed
in-protocol, and the reconnect full-repaint rule is now normative.

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
3. **Every message fits in a single BLE write at MTU 65 — no exceptions.**
   No offset/total chunk header, no reassembly, no partial-frame race
   condition, no fallback path. The exact class of bug (silent
   WriteWithoutResponse drops, ping-pong buffer races, backlog buildup)
   the old full-frame protocol suffered from becomes structurally
   impossible, for every tier. A protocol with zero reassembly cases is
   strictly better than one with "almost zero".
4. **Layout is data, not firmware logic.** The set of rows in a page (which
   tier each row uses, and their order) is sent as a small message from
   the app whenever the user changes the design in the Canvas Editor.
   Firmware never hardcodes a layout.
5. **Firmware is stateless beyond the canvas.** The existing RGB565 canvas
   buffer IS the display state; firmware blits cells into it and forgets
   them. It tracks nothing per-cell — only the ≤16 cumulative row Y
   offsets from the current LAYOUT (~34 bytes). The app owns all diffing
   state.

## Tier table (fixed, shared constant — do not send over the wire)

Both app and firmware embed this table as a compile-time constant. Adding
or resizing a tier is a protocol version bump requiring a coordinated
change on both sides.

| ID | Name | Cell W×H (px) | Cols (68px width) | Rows (160px height) | Bytes/cell (1bpp packed) | CELL msg total |
|----|------|------|------|------|------|------|
| 0 | `small_impar` | 6×10 | 11 | 16 | 10 | 14 |
| 1 | `small_par` | 8×13 | 8 | 12 | 13 | 17 |
| 2 | `medium_impar` | 9×15 | 7 | 10 | 30 | 34 |
| 3 | `medium_par` | 11×20 | 6 | 8 | 40 | 44 |
| 4 | `large_impar` | 13×22 | 5 | 7 | 44 | 48 |
| 5 | `large_par` | 16×28 | 4 | 5 | 56 | 60 |
| 6 | `micro` | 2×2 | 34 | 80 | 2 | 6 |

`large_impar` (5 columns) is the exact fit for `HH:mm`. `large_par`
(4 columns) suits 4-character content; at 16px wide × 4 columns it spans
64 of the 68px, leaving a 2px margin each side. `micro` is near-per-pixel
freeform art/icons (built with the app's own GDI+ rendering — no Unicode
tricks needed, since cells already carry real pixel data).

> `large_par` was 17×30 (90 bytes/cell) in the v1 draft — the only cell
> size that could not fit a single write at MTU 65 and would have required
> a chunked fallback. It was resized to 16×28 (56 bytes, aspect ratio 1.75,
> still 4 even columns) so the fallback could be deleted from the protocol
> entirely.

Bytes/cell = `ceil(width_px / 8) * height_px` (standard 1bpp packing,
row-major, MSB-first, each row padded to a byte boundary).

## GATT characteristic

New characteristic under the existing service `00001523-1212-efde-1523-785feabcd123`:

```
Cell Grid Char UUID: 00001527-1212-efde-1523-785feabcd123
Properties:           Write (WriteWithResponse)
```

**Why WriteWithResponse, not WriteWithoutResponse:** the session's own
debug logs proved WriteWithoutResponse reports `Success` from the local
BLE stack even when a chunk is silently dropped over the air — the exact
failure mode that made previous bugs invisible in this app's logs. Every
cell write gets a real ATT-level acknowledgment so a genuine delivery
failure is visible and retryable, not silent.

**MTU: plan for 65, don't request more.** WinRT provides no API to request
an ATT MTU (`GattSession.MaxPduSize` is read-only; Windows negotiates on
its own), and firmware-initiated MTU exchange was already shown (firmware
builds #150–151) to break HID and split communication on this board.
MTU 65 ⇒ max single-write payload 62 bytes. Every message in this
protocol, worst case `large_par` CELL at 60 bytes, fits with margin.

## Message formats

All multi-byte integers are little-endian.

### `0x01` — LAYOUT

Sent whenever the app applies a new page design (Canvas Editor "Apply",
or a page-cycle switch to a page with a different row structure than
what's currently active). Rare — not sent per content update.

Entries are run-length encoded so a full-page `micro` grid (80 physical
rows) is expressible as a single entry:

```
Byte 0:              msg_type = 0x01
Byte 1:              entry_count (0-16)
Byte 2 + 2i:         tier_id     (0-6 per the tier table)
Byte 3 + 2i:         repeat      (1-80: this many consecutive rows of tier_id)
```

Max message size: 2 + 16×2 = 34 bytes. The expanded row list (each entry
repeated `repeat` times, in order) defines the physical rows top-to-bottom;
`row_index` in CELL messages indexes into that **expanded** list. Firmware
computes each row's cumulative Y offset by summing expanded-row heights
from the shared tier table — no Y offsets are transmitted.

Firmware MUST reject (ignore, optionally log) a LAYOUT whose expanded
heights sum to more than 160px.

### `0x02` — CELL

Sent for every cell whose content changed since the last frame.

```
Byte 0:              msg_type = 0x02
Byte 1:              row_index  (0-based, into the current LAYOUT's expanded row list)
Byte 2:              col_index  (0-based, within that row's tier column count)
Byte 3:              bitmap_len (bytes following; MUST equal the tier's bytes/cell)
Byte 4..3+bitmap_len: packed 1bpp bitmap, row-major, MSB-first
```

Firmware MUST reject a CELL whose `row_index`, `col_index`, or
`bitmap_len` is out of range for the current LAYOUT and tier table.

### `0x03` — CLEAR

Blanks the entire canvas (fill black + full invalidate). Sent before a
new LAYOUT + full CELL set on page transitions and reconnects, so stale
content never shows through while new cells stream in, and so black cells
never need to be transmitted individually during a transition.

```
Byte 0:              msg_type = 0x03  (no payload)
```

## Client rules (app side, normative)

1. **Reconnect = dirty screen.** Firmware loses all state on reboot
   (battery swap, sleep, crash), and the app cannot distinguish a
   reconnect-after-reboot from a plain radio blip. On EVERY (re)connect
   the app MUST send `CLEAR` + `LAYOUT` + the complete CELL set for the
   active page, and reset its diff-tracking state to "nothing on screen".
   Diff-only updates are valid solely within one uninterrupted connection.
2. **Page switch** = `CLEAR` + (`LAYOUT` if the row structure differs
   from the currently active one) + complete CELL set for the new page.
3. **Steady state** = CELL messages only, for cells whose rendered bitmap
   differs from the last one sent.
4. **Periodic full refresh (recommended).** First hardware test observed
   one stale cell: a digit kept its previous glyph for a minute despite
   the app receiving an ATT ack for the write (suspected lost
   `lv_obj_invalidate_area()` — LVGL is not thread-safe, and an
   invalidation issued from the BT RX thread can race the LVGL timer;
   the old 0x1525 path hopped to the system workqueue via
   `k_work_submit()` before touching LVGL). Until that is fixed
   firmware-side, clients SHOULD periodically (e.g. every ~10 updates)
   resend all cells ignoring diff state, bounding any stale cell's
   lifetime. Cheap: a full page is only a handful of ≤60-byte writes.

## Worked example

The mockup page discussed earlier (icons / clock / date / profile bar /
weather):

```
LAYOUT (5 entries, each repeat=1):
  01 05  01 01  04 01  00 01  03 01  02 01
  (small_par ×1, large_impar ×1, small_impar ×1, medium_par ×1, medium_impar ×1)

CELL (clock "13:42", row 1, tier large_impar → 5 cols, cols 0-4 = '1','3',':','4','2'):
  02 01 00 2C <44 bytes of packed bitmap for '1'>
  02 01 01 2C <44 bytes for '3'>
  02 01 02 2C <44 bytes for ':'>
  02 01 03 2C <44 bytes for '4'>
  02 01 04 2C <44 bytes for '2'>
```

On the next minute tick, only the cells whose glyph actually changed are
resent (e.g., `13:42` → `13:43` only touches the last cell).

Full-page micro art:

```
LAYOUT: 01 01 06 50    (1 entry: tier micro, repeat 80)
```

## Resolved review items (firmware, v1 review)

1. **Per-cell blit without full-screen redraw: confirmed.** Firmware
   writes cell pixels directly into the existing LVGL canvas buffer and
   calls `lv_obj_invalidate_area()` with the cell rectangle; LVGL redraws
   only invalidated areas on its ~33ms cycle. Decoding ≤60-byte cells is
   negligible next to the old 1440-byte frames.
2. **RAM: non-issue.** The existing RGB565 canvas buffer is the display
   state; firmware tracks no per-cell data (only ≤16 row offsets). The
   old protocol's 2×1440B ping-pong buffers are deleted. Range validation
   (LAYOUT height sum, CELL indices/length) is the only firmware-side
   guard needed.
3. **CLEAR: in.** ~5 lines of firmware; avoids transmitting black cells
   during page transitions, which is when BLE traffic peaks.

## Migration & diagnostics

- Firmware keeps `0x1525` (full-frame) intact during the transition so
  the two paths can be A/B compared on real hardware.
- **Honest status of the original bug:** the root cause of the clock lag
  was never directly measured (firmware's received/drawn counters had no
  working log backend). This protocol makes that failure mode
  structurally improbable — which is not the same as understood. If any
  anomaly appears after migration (ghost cells, stale cells), there is no
  periodic full-frame resend to self-correct it, so firmware-side logging
  (CDC ACM devicetree overlay on a test build) should be pursued in
  parallel rather than after.
