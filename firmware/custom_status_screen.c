#include <zephyr/kernel.h>
#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/bluetooth/gatt.h>
#include <zephyr/init.h>
#include <lvgl.h>
#include <string.h>
#include <stdio.h>

/* ── Shared: clock + text buffer ─────────────────────────────────────────── */

#define TEXT_MAX_LEN 64
static char text_buf[TEXT_MAX_LEN + 1];

static int64_t clock_sync_unix_s;
static int64_t clock_sync_uptime_ms;
static bool    clock_12h;

static int64_t current_unix_seconds(void) {
    if (clock_sync_unix_s == 0) return 0;
    return clock_sync_unix_s + (k_uptime_get() - clock_sync_uptime_ms) / 1000;
}

static void fmt_clock(char *out, size_t sz) {
    int64_t t = current_unix_seconds();
    int sec = (int)(t % 86400);
    int hh  = sec / 3600;
    int mm  = (sec % 3600) / 60;
    if (clock_12h) {
        const char *sfx = (hh >= 12) ? "p" : "a";
        int h12 = hh % 12;
        if (h12 == 0) h12 = 12;
        snprintf(out, sz, "%02d:%02d%s", h12, mm, sfx);
    } else {
        snprintf(out, sz, "%02d:%02d", hh, mm);
    }
}

/* ── Shared: weather buffer (used by right side display) ─────────────────── */

#define WX_LEN 24
static char wx_city[WX_LEN];
static char wx_temp[WX_LEN];
static char wx_label[WX_LEN];
static char wx_icon[8];   /* single NF icon codepoint (UTF-8, ≤4 bytes) */

LV_FONT_DECLARE(mono_16);
LV_FONT_DECLARE(mono_8);
LV_FONT_DECLARE(mono_icon);

/* ── Canvas helpers ───────────────────────────────────────────────────────── */

#define CANVAS_SIZE 68

/*
 * Rotate canvas 90° CW in-place (copy to tmp first, then transform).
 * This is the same technique used by ZMK's nice_view WPM widget.
 */
static void rotate_canvas(lv_obj_t *canvas, lv_color_t *cbuf) {
    static lv_color_t tmp[CANVAS_SIZE * CANVAS_SIZE];
    memcpy(tmp, cbuf, sizeof(tmp));
    lv_img_dsc_t img = {
        .data       = (const uint8_t *)tmp,
        .header.cf  = LV_IMG_CF_TRUE_COLOR,
        .header.w   = CANVAS_SIZE,
        .header.h   = CANVAS_SIZE,
    };
    lv_canvas_transform(canvas, &img, 900, LV_IMG_ZOOM_NONE,
                        -1, 0, CANVAS_SIZE / 2, CANVAS_SIZE / 2, true);
}

static lv_obj_t *make_clip(lv_obj_t *screen, int x, int y, int w, int h) {
    lv_obj_t *clip = lv_obj_create(screen);
    lv_obj_set_pos(clip, x, y);
    lv_obj_set_size(clip, w, h);
    lv_obj_set_style_bg_opa(clip, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(clip, 0, 0);
    lv_obj_set_style_pad_all(clip, 0, 0);
    lv_obj_set_style_radius(clip, 0, 0);
    lv_obj_set_scrollbar_mode(clip, LV_SCROLLBAR_MODE_OFF);
    lv_obj_clear_flag(clip, LV_OBJ_FLAG_OVERFLOW_VISIBLE);
    return clip;
}

/* ════════════════════════════════════════════════════════════════════════════
 * LEFT SIDE (central) — narrow widget strip at LVGL x=97..138 (42px)
 * ════════════════════════════════════════════════════════════════════════════ */
#if !IS_ENABLED(CONFIG_ZMK_SPLIT_BLE_ROLE_PERIPHERAL)

static lv_color_t ble_cbuf[CANVAS_SIZE * CANVAS_SIZE];
static lv_obj_t  *ble_clip;
static lv_obj_t  *ble_canvas;
static lv_obj_t  *ble_attached_screen;

static void create_ble_overlay(lv_obj_t *screen) {
    ble_clip = make_clip(screen, 97, 0, 42, 68);
    ble_canvas = lv_canvas_create(ble_clip);
    lv_canvas_set_buffer(ble_canvas, ble_cbuf, CANVAS_SIZE, CANVAS_SIZE,
                         LV_IMG_CF_TRUE_COLOR);
    lv_obj_set_pos(ble_canvas, -26, 0);
    ble_attached_screen = screen;
}

static void draw_ble_canvas(void) {
    if (!ble_canvas || !ble_clip) return;
    lv_obj_move_foreground(ble_clip);
    lv_canvas_fill_bg(ble_canvas, lv_color_black(), LV_OPA_COVER);

    lv_draw_label_dsc_t dsc;
    lv_draw_label_dsc_init(&dsc);
    dsc.color = lv_color_white();

    if (text_buf[0] == '\0' && clock_sync_unix_s != 0) {
        /* Clock mode */
        dsc.font = &mono_16;
        char clk[8];
        fmt_clock(clk, sizeof(clk));
        lv_canvas_draw_text(ble_canvas, 0, 12, CANVAS_SIZE, &dsc, clk);
    } else if (text_buf[0] != '\0') {
        /* Text / mode display */
        dsc.font = &mono_8;
        char *sep = strchr(text_buf, '\x01');
        if (sep) {
            char left[TEXT_MAX_LEN + 1];
            uint16_t n = (uint16_t)(sep - text_buf);
            if (n > TEXT_MAX_LEN) n = TEXT_MAX_LEN;
            memcpy(left, text_buf, n);
            left[n] = '\0';
            lv_canvas_draw_text(ble_canvas, 0, 0, 42, &dsc, left);

            lv_draw_label_dsc_t dsc2;
            lv_draw_label_dsc_init(&dsc2);
            dsc2.color = lv_color_white();
            dsc2.font  = &mono_icon;
            lv_canvas_draw_text(ble_canvas, 44, 6, 24, &dsc2, sep + 1);
        } else {
            lv_canvas_draw_text(ble_canvas, 0, 0, CANVAS_SIZE, &dsc, text_buf);
        }
    }

    rotate_canvas(ble_canvas, ble_cbuf);
}

static void ensure_overlay_timer_cb(lv_timer_t *timer) {
    lv_obj_t *screen = lv_scr_act();
    if (!screen) return;

    if (ble_clip && ble_attached_screen == screen) {
        lv_obj_move_foreground(ble_clip);
        if (text_buf[0] == '\0' && clock_sync_unix_s != 0) {
            draw_ble_canvas();
        }
        return;
    }
    if (ble_clip) {
        lv_obj_del(ble_clip);
        ble_clip = ble_canvas = NULL;
    }
    create_ble_overlay(screen);
    draw_ble_canvas();
}

/* ════════════════════════════════════════════════════════════════════════════
 * RIGHT SIDE (peripheral) — full-screen clock + weather
 *
 * Two canvases, both 68×68, rotated 90° CW:
 *
 *  R1 (clock):  clip LVGL x=91..132 (42px) → physical_y=27..68
 *               pre_y=0..41 visible → clock centered at pre_y=12 (18px font)
 *
 *  R2 (weather): clip LVGL x=22..89 (68px) → physical_y=70..137
 *               pre_y=0..67 all visible
 *               city  @ pre_x=1,  pre_y=1
 *               temp  @ pre_x=1,  pre_y=12
 *               icon  @ pre_x=25, pre_y=23  (30px, centered)
 *               label @ pre_x=1,  pre_y=55
 * ════════════════════════════════════════════════════════════════════════════ */
#else   /* IS_ENABLED(CONFIG_ZMK_SPLIT_BLE_ROLE_PERIPHERAL) */

static lv_color_t r1_cbuf[CANVAS_SIZE * CANVAS_SIZE];
static lv_color_t r2_cbuf[CANVAS_SIZE * CANVAS_SIZE];
static lv_obj_t  *r1_clip, *r1_canvas;
static lv_obj_t  *r2_clip, *r2_canvas;
static lv_obj_t  *r_attached_screen;

static void create_ble_overlay(lv_obj_t *screen) {
    /* R1: clock strip (42px wide at physical_y=27..68) */
    r1_clip = make_clip(screen, 91, 0, 42, 68);
    r1_canvas = lv_canvas_create(r1_clip);
    lv_canvas_set_buffer(r1_canvas, r1_cbuf, CANVAS_SIZE, CANVAS_SIZE,
                         LV_IMG_CF_TRUE_COLOR);
    lv_obj_set_pos(r1_canvas, -26, 0);

    /* R2: weather block (68px wide at physical_y=70..137) */
    r2_clip = make_clip(screen, 22, 0, 68, 68);
    r2_canvas = lv_canvas_create(r2_clip);
    lv_canvas_set_buffer(r2_canvas, r2_cbuf, CANVAS_SIZE, CANVAS_SIZE,
                         LV_IMG_CF_TRUE_COLOR);
    lv_obj_set_pos(r2_canvas, 0, 0);

    r_attached_screen = screen;
}

static void draw_ble_canvas(void) {
    if (!r1_canvas || !r2_canvas) return;
    lv_obj_move_foreground(r1_clip);
    lv_obj_move_foreground(r2_clip);

    /* ── R1: clock ── */
    lv_canvas_fill_bg(r1_canvas, lv_color_black(), LV_OPA_COVER);
    if (clock_sync_unix_s != 0) {
        lv_draw_label_dsc_t dsc;
        lv_draw_label_dsc_init(&dsc);
        dsc.color = lv_color_white();
        dsc.font  = &mono_16;
        char clk[8];
        fmt_clock(clk, sizeof(clk));
        lv_canvas_draw_text(r1_canvas, 2, 12, CANVAS_SIZE, &dsc, clk);
    }
    rotate_canvas(r1_canvas, r1_cbuf);

    /* ── R2: weather ── */
    lv_canvas_fill_bg(r2_canvas, lv_color_black(), LV_OPA_COVER);
    if (wx_city[0] != '\0') {
        lv_draw_label_dsc_t dsc;
        lv_draw_label_dsc_init(&dsc);
        dsc.color = lv_color_white();
        dsc.font  = &mono_8;

        lv_canvas_draw_text(r2_canvas, 1, 1,  CANVAS_SIZE, &dsc, wx_city);
        lv_canvas_draw_text(r2_canvas, 1, 12, CANVAS_SIZE, &dsc, wx_temp);
        lv_canvas_draw_text(r2_canvas, 1, 55, CANVAS_SIZE, &dsc, wx_label);

        if (wx_icon[0] != '\0') {
            lv_draw_label_dsc_t dsc2;
            lv_draw_label_dsc_init(&dsc2);
            dsc2.color = lv_color_white();
            dsc2.font  = &mono_icon;
            /* pre_x=25 centers a ~18px-wide glyph in 68px canvas */
            lv_canvas_draw_text(r2_canvas, 25, 23, 18, &dsc2, wx_icon);
        }
    }
    rotate_canvas(r2_canvas, r2_cbuf);
}

static void ensure_overlay_timer_cb(lv_timer_t *timer) {
    lv_obj_t *screen = lv_scr_act();
    if (!screen) return;

    if (r1_clip && r_attached_screen == screen) {
        lv_obj_move_foreground(r1_clip);
        lv_obj_move_foreground(r2_clip);
        /* Update clock every second */
        if (clock_sync_unix_s != 0) {
            lv_canvas_fill_bg(r1_canvas, lv_color_black(), LV_OPA_COVER);
            lv_draw_label_dsc_t dsc;
            lv_draw_label_dsc_init(&dsc);
            dsc.color = lv_color_white();
            dsc.font  = &mono_16;
            char clk[8];
            fmt_clock(clk, sizeof(clk));
            lv_canvas_draw_text(r1_canvas, 2, 12, CANVAS_SIZE, &dsc, clk);
            rotate_canvas(r1_canvas, r1_cbuf);
        }
        return;
    }
    if (r1_clip) {
        lv_obj_del(r1_clip);
        lv_obj_del(r2_clip);
        r1_clip = r1_canvas = r2_clip = r2_canvas = NULL;
    }
    create_ble_overlay(screen);
    draw_ble_canvas();
}

/* Re-advertise so Python can connect while right side is connected to central */
static void kbd_right_start_adv(struct k_work *w);
static K_WORK_DELAYABLE_DEFINE(kbd_right_adv_work, kbd_right_start_adv);

static void kbd_right_start_adv(struct k_work *w) {
    static const struct bt_le_adv_param param = BT_LE_ADV_PARAM_INIT(
        BT_LE_ADV_OPT_CONNECTABLE,
        BT_GAP_ADV_FAST_INT_MIN_2,
        BT_GAP_ADV_FAST_INT_MAX_2,
        NULL);
    static const struct bt_data ad[] = {
        BT_DATA_BYTES(BT_DATA_FLAGS, BT_LE_AD_GENERAL | BT_LE_AD_NO_BREDR),
        BT_DATA(BT_DATA_NAME_COMPLETE, "Eyelash Corne R", 15),
    };
    int err = bt_le_adv_start(&param, ad, ARRAY_SIZE(ad), NULL, 0);
    if (err && err != -EALREADY) {
        /* retry in 5s if not already advertising */
        k_work_reschedule(&kbd_right_adv_work, K_SECONDS(5));
    }
}

static void kbd_right_connected(struct bt_conn *conn, uint8_t err) {
    if (err) return;
    k_work_reschedule(&kbd_right_adv_work, K_SECONDS(1));
}

BT_CONN_CB_DEFINE(kbd_right_conn_cb) = {
    .connected = kbd_right_connected,
};

#endif /* left / right */

/* ── BLE GATT service (shared) ──────────────────────────────────────────── */

static void refresh_ble_canvas(struct k_work *work);
static K_WORK_DEFINE(refresh_work, refresh_ble_canvas);

static ssize_t on_ble_write(struct bt_conn *conn, const struct bt_gatt_attr *attr,
                            const void *buf, uint16_t len, uint16_t offset,
                            uint8_t flags) {
    if (offset >= TEXT_MAX_LEN) {
        return BT_GATT_ERR(BT_ATT_ERR_INVALID_OFFSET);
    }
    uint16_t n = MIN(len, TEXT_MAX_LEN - offset);
    memcpy(text_buf + offset, buf, n);
    text_buf[offset + n] = '\0';

    /* Clock sync: "T:<unix>:A|H" */
    if (text_buf[0] == 'T' && text_buf[1] == ':') {
        int64_t unix_s = 0;
        const char *p = text_buf + 2;
        while (*p >= '0' && *p <= '9') {
            unix_s = unix_s * 10 + (*p++ - '0');
        }
        if (unix_s > 0) {
            clock_sync_unix_s    = unix_s;
            clock_sync_uptime_ms = k_uptime_get();
            clock_12h = (*p == ':' && *(p + 1) == 'A');
        }
        text_buf[0] = '\0';
    }

    /* Weather data for right side: "W:City\nTemp\nLabel\x01Icon" */
    if (text_buf[0] == 'W' && text_buf[1] == ':') {
        char *p = text_buf + 2;
        char *nl1 = strchr(p, '\n');
        if (nl1) {
            uint8_t m = (uint8_t)(nl1 - p);
            if (m >= WX_LEN) m = WX_LEN - 1;
            memcpy(wx_city, p, m); wx_city[m] = '\0';
            p = nl1 + 1;
            char *nl2 = strchr(p, '\n');
            if (nl2) {
                m = (uint8_t)(nl2 - p);
                if (m >= WX_LEN) m = WX_LEN - 1;
                memcpy(wx_temp, p, m); wx_temp[m] = '\0';
                p = nl2 + 1;
                char *sep = strchr(p, '\x01');
                if (sep) {
                    m = (uint8_t)(sep - p);
                    if (m >= WX_LEN) m = WX_LEN - 1;
                    memcpy(wx_label, p, m); wx_label[m] = '\0';
                    strncpy(wx_icon, sep + 1, sizeof(wx_icon) - 1);
                    wx_icon[sizeof(wx_icon) - 1] = '\0';
                } else {
                    strncpy(wx_label, p, WX_LEN - 1);
                    wx_label[WX_LEN - 1] = '\0';
                    wx_icon[0] = '\0';
                }
            }
        } else {
            /* "W:" alone clears weather */
            wx_city[0] = '\0';
        }
        text_buf[0] = '\0';
    }

    k_work_submit(&refresh_work);
    return n;
}

static void refresh_ble_canvas(struct k_work *work) { draw_ble_canvas(); }

#define KBD_DISPLAY_SVC_UUID \
    BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0x00001523, 0x1212, 0xefde, 0x1523, 0x785feabcd123ULL))
#define KBD_DISPLAY_CHAR_UUID \
    BT_UUID_DECLARE_128(BT_UUID_128_ENCODE(0x00001524, 0x1212, 0xefde, 0x1523, 0x785feabcd123ULL))

BT_GATT_SERVICE_DEFINE(keyboard_display_svc,
    BT_GATT_PRIMARY_SERVICE(KBD_DISPLAY_SVC_UUID),
    BT_GATT_CHARACTERISTIC(KBD_DISPLAY_CHAR_UUID,
        BT_GATT_CHRC_WRITE | BT_GATT_CHRC_WRITE_WITHOUT_RESP,
        BT_GATT_PERM_WRITE, NULL, on_ble_write, NULL),
);

/* ── Startup (shared) ───────────────────────────────────────────────────── */

static void add_ble_canvas_fn(struct k_work *work);
static K_WORK_DELAYABLE_DEFINE(add_ble_canvas_work, add_ble_canvas_fn);

static void add_ble_canvas_fn(struct k_work *work) {
    lv_obj_t *screen = lv_scr_act();
    if (!screen) {
        k_work_schedule(&add_ble_canvas_work, K_MSEC(500));
        return;
    }
    create_ble_overlay(screen);
    draw_ble_canvas();
    lv_timer_create(ensure_overlay_timer_cb, 1000, NULL);
}

static int kbd_ble_display_init(void) {
    k_work_schedule(&add_ble_canvas_work, K_MSEC(3000));
    return 0;
}
SYS_INIT(kbd_ble_display_init, APPLICATION, 99);
