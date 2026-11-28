/* Pure math, telemetry structs, and SDK 17 HfDraw Titanfall Tactical Cockpit HUD renderer (0.1.0). */
#ifndef TFH_RENDER_H
#define TFH_RENDER_H

#include "hellforge_mod.h"
#include "titanfall_hud_options.h"

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#define TFH_PI            3.14159265358979323846f
#define TFH_RAD2DEG       (180.0f / TFH_PI)
#define TFH_DEG2RAD       (TFH_PI / 180.0f)
#define TFH_BOOT_DURATION 0.55f
#define TFH_SHUT_DURATION 0.20f
#define TFH_MAX_MOUNTS    4u

typedef struct TfhVec3 {
    float x, y, z;
} TfhVec3;

typedef struct TfhMountTelemetry {
    uint8_t present;
    int32_t rounds;
    int32_t capacity;
} TfhMountTelemetry;

typedef struct TfhGameTelemetry {
    uint32_t avatar_entity;
    uint32_t avatar_ref;
    uint32_t walker_entity;
    uint32_t walker_ref;
    uint64_t walker_res;
    int32_t hull_hp;
    int32_t hull_max_hp;
    float speed_mps;
    uint32_t mount_count;
    TfhMountTelemetry mounts[TFH_MAX_MOUNTS];
} TfhGameTelemetry;

typedef struct TfhPoseTelemetry {
    int mounted;
    int is_atst;
    int first_person;
    int hud_hidden_by_user;
    float heading_deg;     /* 0 = +Y (North), 90 = +X (East), [0..360) */
    float walker_yaw_deg;  /* Chassis forward heading [0..360) */
    float pitch_deg;       /* [-89..+89] degrees */
    float cam_fov;
} TfhPoseTelemetry;

static inline TfhVec3 tfh_v3(float x, float y, float z)
{
    TfhVec3 v = {x, y, z};
    return v;
}

static inline float tfh_dot(TfhVec3 a, TfhVec3 b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

static inline float tfh_len(TfhVec3 a)
{
    return sqrtf(tfh_dot(a, a));
}

static inline int tfh_normalize(TfhVec3* v)
{
    float l = tfh_len(*v);
    if (!isfinite(l) || l < 1e-5f) return 0;
    v->x /= l;
    v->y /= l;
    v->z /= l;
    return 1;
}

/* Converts a Stingray forward vector (+X East, +Y North, +Z Up) into compass heading [0, 360) and pitch [-89, +89]. */
static inline void tfh_angles_from_forward(TfhVec3 fwd, float* out_heading_deg, float* out_pitch_deg)
{
    if (!tfh_normalize(&fwd)) {
        if (out_heading_deg) *out_heading_deg = 0.0f;
        if (out_pitch_deg) *out_pitch_deg = 0.0f;
        return;
    }
    float hdg = atan2f(fwd.x, fwd.y) * TFH_RAD2DEG;
    if (!isfinite(hdg)) hdg = 0.0f;
    while (hdg < 0.0f) hdg += 360.0f;
    while (hdg >= 360.0f) hdg -= 360.0f;

    float cz = fwd.z;
    if (cz > 0.9998f) cz = 0.9998f;
    if (cz < -0.9998f) cz = -0.9998f;
    float pitch = asinf(cz) * TFH_RAD2DEG;
    if (!isfinite(pitch)) pitch = 0.0f;
    if (pitch > 89.0f) pitch = 89.0f;
    if (pitch < -89.0f) pitch = -89.0f;

    if (out_heading_deg) *out_heading_deg = hdg;
    if (out_pitch_deg) *out_pitch_deg = pitch;
}

/* Signed shortest angular delta (target - ref) in [-180, +180]. */
static inline float tfh_wrap_delta_deg(float target_deg, float ref_deg)
{
    float d = fmodf(target_deg - ref_deg, 360.0f);
    if (!isfinite(d)) return 0.0f;
    if (d > 180.0f) d -= 360.0f;
    if (d < -180.0f) d += 360.0f;
    return d;
}

static inline uint8_t tfh_u8_alpha(float base_a, float scale)
{
    float v = base_a * scale;
    if (!isfinite(v) || v <= 0.0f) return 0u;
    if (v >= 255.0f) return 255u;
    return (uint8_t)(v + 0.5f);
}

static inline HfColour tfh_rgba(uint8_t r, uint8_t g, uint8_t b, float base_a, float alpha_scale)
{
    return HF_RGBA(r, g, b, tfh_u8_alpha(base_a, alpha_scale));
}

/* Draws the complete Titanfall-inspired tactical walker visor HUD into HfDraw (1080-unit height, aspect-adaptive width).
 * Purely deterministic and unit-testable with a mock HfDraw struct. */
static inline void tfh_render_cockpit_hud(
    const HfDraw* d,
    const TfhOptions* opts,
    const TfhGameTelemetry* gt,
    const TfhPoseTelemetry* pt,
    float boot_progress,
    float anim_time)
{
    if (!d || !opts || !gt || !pt) return;
    if (!d->rect || !d->lines || !d->polygon || !d->circle || !d->text) return;
    if (d->width < 320.0f || d->height < 240.0f) return;

    float boot = tfh_clampf(boot_progress, 0.0f, 1.0f, 1.0f);
    if (boot <= 0.01f) return;

    float op = tfh_clampf(opts->hud_opacity, 0.15f, 1.0f, 0.90f) * boot;
    const float w = d->width;
    const float h = d->height; /* 1080.0f */
    const float cx = w * 0.5f;
    const float cy = h * 0.5f;

    /* Hull health fraction & tactical color palette (Republic Tactical Cyan -> Amber -> Critical Crimson). */
    float hp_frac = 1.0f;
    if (gt->hull_max_hp > 0 && gt->hull_hp >= 0) {
        hp_frac = (float)gt->hull_hp / (float)gt->hull_max_hp;
        hp_frac = tfh_clampf(hp_frac, 0.0f, 1.0f, 1.0f);
    }
    int crit_pulse = (hp_frac < 0.30f) ? 1 : 0;
    float pulse = crit_pulse ? (0.65f + 0.35f * sinf(anim_time * 9.0f)) : 1.0f;

    uint8_t pri_r = 76u, pri_g = 216u, pri_b = 255u; /* Titanfall / Republic Holo-Cyan */
    if (pt->is_atst) {
        pri_r = 120u; pri_g = 240u; pri_b = 156u;     /* Imperial Targeting Green if opted in */
    }
    uint8_t hull_r = pri_r, hull_g = pri_g, hull_b = pri_b;
    if (hp_frac < 0.30f) {
        hull_r = 255u; hull_g = 68u; hull_b = 56u;
    } else if (hp_frac < 0.60f) {
        hull_r = 255u; hull_g = 196u; hull_b = 58u;
    }

    HfColour col_bezel      = tfh_rgba(8u, 14u, 22u, pt->first_person ? 228.0f : 170.0f, op);
    HfColour col_panel      = tfh_rgba(10u, 20u, 32u, 168.0f, op);
    HfColour col_trim       = tfh_rgba(pri_r, pri_g, pri_b, 190.0f, op);
    HfColour col_trim_dim   = tfh_rgba(pri_r, pri_g, pri_b, 85.0f, op);
    HfColour col_text       = tfh_rgba(225u, 246u, 255u, 235.0f, op);
    HfColour col_text_dim   = tfh_rgba(150u, 198u, 220u, 185.0f, op);
    HfColour col_hull       = tfh_rgba(hull_r, hull_g, hull_b, 230.0f * pulse, op);
    HfColour col_amber      = tfh_rgba(255u, 196u, 64u, 220.0f, op);

    /* 1. Optical Visor Boot-Up Scanline Shutter (first 0.55s after mount). */
    if (boot < 0.995f) {
        float aperture_half = (h * 0.5f) * boot;
        float top_shut = cy - aperture_half;
        float bot_shut = cy + aperture_half;
        if (top_shut > 0.0f) {
            d->rect(0.0f, 0.0f, w, top_shut, tfh_rgba(4u, 10u, 18u, 220.0f, 1.0f - boot * 0.65f), 0u, 0.0f);
        }
        if (bot_shut < h) {
            d->rect(0.0f, bot_shut, w, h, tfh_rgba(4u, 10u, 18u, 220.0f, 1.0f - boot * 0.65f), 0u, 0.0f);
        }
        HfPoint scan_t[2] = {{0.0f, top_shut}, {w, top_shut}};
        HfPoint scan_b[2] = {{0.0f, bot_shut}, {w, bot_shut}};
        d->lines(scan_t, 2u, tfh_rgba(pri_r, pri_g, pri_b, 220.0f, 1.0f), 2.0f, 0);
        d->lines(scan_b, 2u, tfh_rgba(pri_r, pri_g, pri_b, 220.0f, 1.0f), 2.0f, 0);
        d->text(
            cx,
            cy - 18.0f,
            pt->is_atst ? "KUAT DRIVE YARDS // AT-ST TACTICAL VISOR // ONLINE"
                        : "ROTHANA HEAVY ENG. // AT-TE TACTICAL OS v2.3 // NEURAL LINK",
            20.0f,
            HF_FACE_TITLE,
            HF_ALIGN_CENTRE,
            tfh_rgba(pri_r, pri_g, pri_b, 245.0f, 1.0f - boot * 0.35f));
    }

    /* 2. Armored Canopy Top/Bottom Bezels & Angled Titanfall A-Pillar Struts. */
    float strut_w = pt->first_person ? (w * 0.068f) : (w * 0.036f);
    float top_h = pt->first_person ? 52.0f : 34.0f;
    float bot_h = pt->first_person ? 44.0f : 26.0f;

    HfPoint top_bezel[4] = {
        {0.0f, 0.0f},
        {w, 0.0f},
        {w - strut_w * 1.4f, top_h},
        {strut_w * 1.4f, top_h},
    };
    HfPoint bot_bezel[4] = {
        {strut_w * 1.6f, h - bot_h},
        {w - strut_w * 1.6f, h - bot_h},
        {w, h},
        {0.0f, h},
    };
    HfPoint left_strut[4] = {
        {0.0f, 0.0f},
        {strut_w * 1.4f, top_h},
        {strut_w * 0.55f, cy},
        {0.0f, cy},
    };
    HfPoint left_strut_lo[4] = {
        {0.0f, cy},
        {strut_w * 0.55f, cy},
        {strut_w * 1.6f, h - bot_h},
        {0.0f, h},
    };
    HfPoint right_strut[4] = {
        {w - strut_w * 1.4f, top_h},
        {w, 0.0f},
        {w, cy},
        {w - strut_w * 0.55f, cy},
    };
    HfPoint right_strut_lo[4] = {
        {w - strut_w * 0.55f, cy},
        {w, cy},
        {w, h},
        {w - strut_w * 1.6f, h - bot_h},
    };

    d->polygon(top_bezel, 4u, col_bezel);
    d->polygon(bot_bezel, 4u, col_bezel);
    d->polygon(left_strut, 4u, col_bezel);
    d->polygon(left_strut_lo, 4u, col_bezel);
    d->polygon(right_strut, 4u, col_bezel);
    d->polygon(right_strut_lo, 4u, col_bezel);

    HfPoint left_rail[3] = {
        {strut_w * 1.4f + 4.0f, top_h},
        {strut_w * 0.55f + 4.0f, cy},
        {strut_w * 1.6f + 4.0f, h - bot_h},
    };
    HfPoint right_rail[3] = {
        {w - strut_w * 1.4f - 4.0f, top_h},
        {w - strut_w * 0.55f - 4.0f, cy},
        {w - strut_w * 1.6f - 4.0f, h - bot_h},
    };
    d->lines(left_rail, 3u, col_trim, 2.2f, 0);
    d->lines(right_rail, 3u, col_trim, 2.2f, 0);

    /* 3. Top Tactical Compass Tape (000..359 deg + Cardinal/Intercardinal + Chassis Nose Marker). */
    const float comp_half_w = 270.0f;
    const float comp_x0 = cx - comp_half_w;
    const float comp_x1 = cx + comp_half_w;
    const float comp_y0 = top_h + 10.0f;
    const float comp_y1 = comp_y0 + 38.0f;
    const float px_per_deg = comp_half_w / 60.0f; /* +-60 deg visible window */

    d->rect(comp_x0, comp_y0, comp_x1, comp_y1, col_panel, col_trim_dim, 1.5f);
    if (d->clip_push && d->clip_pop) {
        d->clip_push(comp_x0 + 2.0f, comp_y0, comp_x1 - 2.0f, comp_y1);
    }

    int base_tick = ((int)floorf(pt->heading_deg / 5.0f)) * 5;
    for (int offset_deg = -65; offset_deg <= 65; offset_deg += 5) {
        int deg = (base_tick + offset_deg) % 360;
        if (deg < 0) deg += 360;
        float rel = tfh_wrap_delta_deg((float)deg, pt->heading_deg);
        float tx = cx + rel * px_per_deg;
        if (tx < comp_x0 + 4.0f || tx > comp_x1 - 4.0f) continue;

        int major = (deg % 15) == 0;
        HfPoint tick[2] = {{tx, comp_y1 - (major ? 12.0f : 6.0f)}, {tx, comp_y1 - 1.5f}};
        d->lines(tick, 2u, major ? col_trim : col_trim_dim, major ? 1.8f : 1.1f, 0);

        if ((deg % 45) == 0) {
            static const char* const kCardinals[8] = {"N", "NE", "E", "SE", "S", "SW", "W", "NW"};
            const char* lbl = kCardinals[(deg / 45) & 7];
            d->text(tx, comp_y0 + 13.0f, lbl, 16.0f, HF_FACE_TITLE, HF_ALIGN_CENTRE,
                    (deg == 0) ? col_amber : col_text);
        } else if (major) {
            char nbuf[8];
            snprintf(nbuf, sizeof(nbuf), "%03d", deg);
            d->text(tx, comp_y0 + 13.0f, nbuf, 13.0f, HF_FACE_BODY, HF_ALIGN_CENTRE, col_text_dim);
        }
    }

    /* Chassis hull nose marker on compass (shows where walker legs face vs camera aim). */
    float hull_rel = tfh_wrap_delta_deg(pt->walker_yaw_deg, pt->heading_deg);
    float hx = tfh_clampf(cx + hull_rel * px_per_deg, comp_x0 + 10.0f, comp_x1 - 10.0f, cx);
    HfPoint hull_tri[3] = {{hx, comp_y1 - 2.0f}, {hx - 6.0f, comp_y1 - 10.0f}, {hx + 6.0f, comp_y1 - 10.0f}};
    d->polygon(hull_tri, 3u, col_amber);

    if (d->clip_push && d->clip_pop) {
        d->clip_pop();
    }

    /* Exact digital heading readout box directly above/below compass center. */
    char hdg_buf[16];
    int hdg_int = ((int)floorf(pt->heading_deg + 0.5f)) % 360;
    if (hdg_int < 0) hdg_int += 360;
    snprintf(hdg_buf, sizeof(hdg_buf), "%03d", hdg_int);
    d->rect(cx - 28.0f, comp_y1 + 2.0f, cx + 28.0f, comp_y1 + 22.0f, col_bezel, col_trim, 1.4f);
    d->text(cx, comp_y1 + 12.0f, hdg_buf, 15.0f, HF_FACE_TITLE, HF_ALIGN_CENTRE, col_text);

    /* 4. Hull Integrity Bar (below heading readout). */
    const float hb_w = 340.0f;
    const float hb_x0 = cx - hb_w * 0.5f;
    const float hb_x1 = cx + hb_w * 0.5f;
    const float hb_y0 = comp_y1 + 28.0f;
    const float hb_y1 = hb_y0 + 14.0f;
    d->rect(hb_x0, hb_y0, hb_x1, hb_y1, col_panel, col_trim_dim, 1.3f);
    if (hp_frac > 0.005f) {
        float fill_x1 = hb_x0 + 2.0f + (hb_w - 4.0f) * hp_frac;
        d->rect(hb_x0 + 2.0f, hb_y0 + 2.0f, fill_x1, hb_y1 - 2.0f, col_hull, 0u, 0.0f);
    }

    char hp_buf[64];
    if (gt->hull_max_hp > 0) {
        int pct = (int)(hp_frac * 100.0f + 0.5f);
        snprintf(hp_buf, sizeof(hp_buf), "ARMOR INTEGRITY %3d%%  [%d / %d]", pct, gt->hull_hp, gt->hull_max_hp);
    } else {
        snprintf(hp_buf, sizeof(hp_buf), "ARMOR INTEGRITY ONLINE");
    }
    d->text(cx, hb_y1 + 12.0f, hp_buf, 13.5f, HF_FACE_TITLE, HF_ALIGN_CENTRE, crit_pulse ? col_hull : col_text_dim);

    if (crit_pulse) {
        d->text(cx, hb_y1 + 32.0f, "WARNING // HULL CRITICAL // EVASIVE MANEUVERS", 18.0f, HF_FACE_TITLE, HF_ALIGN_CENTRE, col_hull);
    }

    /* 5. Center Pitch Ladder & Non-Intrusive Tactical Reticle Brackets (96-unit center deadzone). */
    const float lad_inner = 112.0f;
    const float lad_outer = 148.0f;
    const float pitch_px_per_deg = 5.2f;
    for (int pdeg = -30; pdeg <= 30; pdeg += 10) {
        float py = cy + (pt->pitch_deg - (float)pdeg) * pitch_px_per_deg;
        if (py < cy - 155.0f || py > cy + 155.0f) continue;
        float tick_len = (pdeg == 0) ? (lad_outer - lad_inner + 14.0f) : (lad_outer - lad_inner);
        HfPoint l_bar[3] = {
            {cx - lad_inner - tick_len, py},
            {cx - lad_inner, py},
            {cx - lad_inner, py + (pdeg == 0 ? 0.0f : (pdeg > 0 ? 6.0f : -6.0f))},
        };
        HfPoint r_bar[3] = {
            {cx + lad_inner + tick_len, py},
            {cx + lad_inner, py},
            {cx + lad_inner, py + (pdeg == 0 ? 0.0f : (pdeg > 0 ? 6.0f : -6.0f))},
        };
        d->lines(l_bar, pdeg == 0 ? 2u : 3u, pdeg == 0 ? col_trim : col_trim_dim, pdeg == 0 ? 1.9f : 1.3f, 0);
        d->lines(r_bar, pdeg == 0 ? 2u : 3u, pdeg == 0 ? col_trim : col_trim_dim, pdeg == 0 ? 1.9f : 1.3f, 0);
    }

    /* Subtle Titanfall corner brackets framing the native aiming zone without covering the center dot. */
    const float brk_r = 68.0f;
    const float brk_l = 16.0f;
    HfPoint b_tl[3] = {{cx - brk_r, cy - brk_r + brk_l}, {cx - brk_r, cy - brk_r}, {cx - brk_r + brk_l, cy - brk_r}};
    HfPoint b_tr[3] = {{cx + brk_r - brk_l, cy - brk_r}, {cx + brk_r, cy - brk_r}, {cx + brk_r, cy - brk_r + brk_l}};
    HfPoint b_bl[3] = {{cx - brk_r, cy + brk_r - brk_l}, {cx - brk_r, cy + brk_r}, {cx - brk_r + brk_l, cy + brk_r}};
    HfPoint b_br[3] = {{cx + brk_r - brk_l, cy + brk_r}, {cx + brk_r, cy + brk_r}, {cx + brk_r, cy + brk_r - brk_l}};
    d->lines(b_tl, 3u, col_trim, 1.8f, 0);
    d->lines(b_tr, 3u, col_trim, 1.8f, 0);
    d->lines(b_bl, 3u, col_trim, 1.8f, 0);
    d->lines(b_br, 3u, col_trim, 1.8f, 0);

    /* 6. Lower-Left Panel: 6-Leg AT-TE Hexapod Schematic, Ground Speed & Dual-View Status. */
    const float lp_x0 = strut_w * 1.6f + 24.0f;
    const float lp_y1 = h - bot_h - 22.0f;
    const float lp_x1 = lp_x0 + 318.0f;
    const float lp_y0 = lp_y1 - 126.0f;

    d->rect(lp_x0, lp_y0, lp_x1, lp_y1, col_panel, col_trim_dim, 1.6f);
    d->text(lp_x0 + 14.0f, lp_y0 + 18.0f,
            pt->is_atst ? "CHASSIS // AT-ST SCOUT" : "CHASSIS // AT-TE HEXAPOD",
            14.5f, HF_FACE_TITLE, HF_ALIGN_LEFT, col_trim);

    /* Mini walker hull + leg status wireframe on left half of panel */
    float sx = lp_x0 + 56.0f;
    float sy = lp_y0 + 74.0f;
    d->rect(sx - 14.0f, sy - 26.0f, sx + 14.0f, sy + 26.0f, 0u, col_hull, 1.8f);
    if (!pt->is_atst) {
        /* 6 AT-TE legs (fore, mid, aft pairs) */
        for (int row = -1; row <= 1; row++) {
            float ly = sy + (float)row * 18.0f;
            HfPoint leg_l[3] = {{sx - 14.0f, ly}, {sx - 28.0f, ly - 3.0f}, {sx - 34.0f, ly + 4.0f}};
            HfPoint leg_r[3] = {{sx + 14.0f, ly}, {sx + 28.0f, ly - 3.0f}, {sx + 34.0f, ly + 4.0f}};
            d->lines(leg_l, 3u, col_hull, 1.7f, 0);
            d->lines(leg_r, 3u, col_hull, 1.7f, 0);
        }
    } else {
        /* 2 bipedal AT-ST legs */
        HfPoint leg_l[2] = {{sx - 14.0f, sy}, {sx - 30.0f, sy + 14.0f}};
        HfPoint leg_r[2] = {{sx + 14.0f, sy}, {sx + 30.0f, sy + 14.0f}};
        d->lines(leg_l, 2u, col_hull, 2.0f, 0);
        d->lines(leg_r, 2u, col_hull, 2.0f, 0);
    }
    /* Turret / look vector tick from schematic center */
    float rel_rad = hull_rel * (-TFH_DEG2RAD);
    HfPoint look_ray[2] = {{sx, sy}, {sx - sinf(rel_rad) * 24.0f, sy - cosf(rel_rad) * 24.0f}};
    d->lines(look_ray, 2u, col_amber, 1.8f, 0);
    d->circle(sx, sy, 3.5f, col_amber, 0u, 0.0f);

    char spd_buf[48];
    snprintf(spd_buf, sizeof(spd_buf), "VEL   %04.1f M/S", gt->speed_mps >= 0.0f ? gt->speed_mps : 0.0f);
    d->text(lp_x0 + 112.0f, lp_y0 + 48.0f, spd_buf, 16.0f, HF_FACE_TITLE, HF_ALIGN_LEFT, col_text);

    char pch_buf[48];
    snprintf(pch_buf, sizeof(pch_buf), "ELEV  %+05.1f DEG", pt->pitch_deg);
    d->text(lp_x0 + 112.0f, lp_y0 + 74.0f, pch_buf, 14.5f, HF_FACE_BODY, HF_ALIGN_LEFT, col_text_dim);

    if (opts->dual_view_enabled && opts->dual_view_vk != 0u) {
        const char* view_str = pt->first_person ? "OPTICS: 1P CANOPY [F7]" : "OPTICS: 3P TACTICAL [F7]";
        if (opts->dual_view_vk != 0x76u) {
            view_str = pt->first_person ? "OPTICS: 1P CANOPY" : "OPTICS: 3P TACTICAL";
        }
        d->text(lp_x0 + 112.0f, lp_y0 + 102.0f, view_str, 13.5f, HF_FACE_TITLE, HF_ALIGN_LEFT,
                pt->first_person ? col_amber : col_trim);
    }

    /* 7. Lower-Right Panel: Live Walker Ordnance Counters (MountManager + MagazineRounds/Capacity). */
    const float rp_x1 = w - strut_w * 1.6f - 24.0f;
    const float rp_x0 = rp_x1 - 318.0f;
    const float rp_y1 = lp_y1;
    const float rp_y0 = lp_y0;

    d->rect(rp_x0, rp_y0, rp_x1, rp_y1, col_panel, col_trim_dim, 1.6f);
    d->text(rp_x1 - 14.0f, rp_y0 + 18.0f, "ORDNANCE // WEAPON BAYS", 14.5f, HF_FACE_TITLE, HF_ALIGN_RIGHT, col_trim);

    uint32_t shown = 0u;
    for (uint32_t m = 0u; m < TFH_MAX_MOUNTS && shown < 2u; m++) {
        if (!gt->mounts[m].present) continue;
        float ry = rp_y0 + 48.0f + (float)shown * 38.0f;
        int32_t rnd = gt->mounts[m].rounds;
        int32_t cap = gt->mounts[m].capacity;
        if (cap < rnd && rnd > 0) cap = rnd;

        char wlbl[64];
        snprintf(wlbl, sizeof(wlbl), "MOUNT %u", shown + 1u);
        d->text(rp_x0 + 14.0f, ry, wlbl, 14.0f, HF_FACE_TITLE, HF_ALIGN_LEFT, col_text_dim);

        char abuf[48];
        if (cap > 0) {
            snprintf(abuf, sizeof(abuf), "%d / %d", rnd >= 0 ? rnd : 0, cap);
        } else {
            snprintf(abuf, sizeof(abuf), "%d RDS", rnd >= 0 ? rnd : 0);
        }
        HfColour acol = (cap > 0 && rnd * 5 <= cap) ? tfh_rgba(255u, 82u, 64u, 235.0f, op) : col_text;
        d->text(rp_x1 - 14.0f, ry, abuf, 16.0f, HF_FACE_TITLE, HF_ALIGN_RIGHT, acol);

        float bar_x0 = rp_x0 + 14.0f;
        float bar_x1 = rp_x1 - 14.0f;
        float bar_y0 = ry + 12.0f;
        float bar_y1 = bar_y0 + 8.0f;
        d->rect(bar_x0, bar_y0, bar_x1, bar_y1, col_bezel, col_trim_dim, 1.0f);
        float afrac = (cap > 0 && rnd > 0) ? tfh_clampf((float)rnd / (float)cap, 0.0f, 1.0f, 0.0f) : (rnd > 0 ? 1.0f : 0.0f);
        if (afrac > 0.005f) {
            d->rect(bar_x0 + 1.5f, bar_y0 + 1.5f, bar_x0 + 1.5f + (bar_x1 - bar_x0 - 3.0f) * afrac, bar_y1 - 1.5f,
                    (afrac <= 0.20f) ? tfh_rgba(255u, 82u, 64u, 230.0f, op) : col_trim, 0u, 0.0f);
        }
        shown++;
    }

    if (shown == 0u) {
        d->text(rp_x0 + 14.0f, rp_y0 + 58.0f, "PRIMARY CANNONS: READY", 14.5f, HF_FACE_TITLE, HF_ALIGN_LEFT, col_text);
        d->text(rp_x0 + 14.0f, rp_y0 + 86.0f, "FIRE CONTROL: LINKED", 13.5f, HF_FACE_BODY, HF_ALIGN_LEFT, col_text_dim);
    }
}

#endif /* TFH_RENDER_H */
