#!/usr/bin/env python3
"""Build EmpireDivers r55 & Clones r26 voice pronunciation and lore fixes.

Fixes "some voices misprounce stuff", DSS -> Death Star spoken bridge VO, and
removes the need for Death Trooper encrypted Slot 4 by updating clean British
Imperial Slot 4 (`patch_1033`) alongside `patch_1032` and Clones `patch_33`:

1. DSS -> Death Star (5 Star Destroyer bridge VO lines in patch_1032):
   - Rewrites spoken "the DSS" -> "the Death Star" so bridge crew audio matches
     the DSS -> Death Star localization strings.
2. Star Wars Acronyms & Proper Nouns:
   - Fixes Qwen3-TTS mispronunciations across Empire & Clones:
     * "TIE One" -> "Tie-1" (fixes "Taiwan" merge across 12 TIE pilot lines)
     * "TIE" -> "Tie" (fixes "T.I. pilot" / "TI strike" / "Thai")
     * "Tibanna" / "tibanna" -> "tih-bah-nah" (fixes "Habana" / "T-Banner")
     * "Jedha" -> "Jed-ah"
     * "Geonosian(s)" -> "Gee-oh-noh-shun(s)" (fixes "Genosian" / "Genoesean")
     * "FTL" -> "F-T-L" (fixes "FTO jump")
     * "S.O.S.!!!" -> "S-O-S!" (fixes "OS!")
     * "TAC PAC" / "Tac's out" -> "tac-pack" / "Tac-pack's out" (fixes "Tax out")
     * All-caps planet systems ("FURY SYSTEM" -> "Fury System", "PAS" -> "Pahss",
       "HET" -> "Het", "VACA" -> "Vah-kah", "Tor E-System" -> "Tor-E System",
       "Fracaneria" -> "Frah-kah-neh-ree-ah")
     * Interjections & numbers ("UGH!" -> "Ugh!", "50 billion" -> "Fifty billion",
       "0400" -> "oh-four-hundred", "AWESOME!" -> "Awesome!", "NEVER DOUBT!" -> "Never doubt!")
3. Vanilla Whisper Mistranscriptions, Truncated Callouts & Double Determiners:
   - 14 "Century" stratagem callouts -> "Sentry" ("Calling down a sentry.", "Requesting sentry.", "Sentry!")
   - 4 "Max empty." -> "Mag's empty!"
   - 2 "Armless animal." -> "Harmless animal."
   - 2 "Art effect collected." -> "Artifact collected."
   - "Ugh! need stamps!" -> "Ugh! Need stims!"
   - "Grab that stem" -> "Grab that stim"
   - "For superards!" -> "For the Empire!"
   - "SWAG Imperial order PLEASE FORGIVE ME!" -> "Sweet Emperor, please forgive me!"
   - "LIGHTNING Imperial order LOAD!" -> "Lightening the Empire's load!"
   - "EGOT to obey!" -> "Eager to obey!"
   - "Flag! forsake it!" -> "Frag! For the Empire!"
   - "Oh, minds! Someone's gonna get a lightbulb off!" -> "Oh, mines! Someone's gonna get a leg blown off!"
   - "Deploying SIEV" / "Seafix traction" / "CIFAR out" / "Seafon route" / "See support" -> Republic garrison callouts
   - "The Illumina" -> "The Syndicate", "within the hell pod" -> "within the drop pod",
     double determiners ("189 million the Republic", "your the Republic, officer", "New the Republic Light",
     "Top the Republic scientists", "under the Republic Control", "the Republic. the Republic.")
   - 12 truncated (<0.35s) or slurred short combat callouts regenerated cleanly.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import struct
import sys
import time

import numpy as np
import pyloudnorm as pyln
from scipy import signal
import soundfile as sf
import torch

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import empire_port as P

STREAM = 0x504B55235D21440E
BANK = 0x535A7BD3E650D799
UNTOUCHED_P1033_KEY = (0xB7CC016F2537E3D3, STREAM)

DEFAULT_V24_DIR = Path(r"D:\ClaudeWork\cache\v24_voices")
DEFAULT_V22_DIR = Path(r"D:\ClaudeWork\cache\v22_voices")
DEFAULT_MODEL_SNAP = Path(
    r"D:\ClaudeWork\cache\hf\hub\models--Qwen--Qwen3-TTS-12Hz-1.7B-Base\snapshots\fd4b254389122332181a7c3db7f27e918eec64e3"
)
DEFAULT_R54_EXP = Path(r"D:\Clonedivers-HellForge-cache\exports\empire-017fd5e5140344768bd55de41dcd6789")
DEFAULT_BLOB_CACHE = Path(r"D:\ClaudeWork\cache\r54-blobs")
DEFAULT_CLONES_R26 = Path(r"D:\ClaudeWork\swarm-packs\clones-r26")
DEFAULT_OUT_ROOT = Path(r"D:\ClaudeWork\swarm-packs\empire-r55")

WEM_FMT_CHUNK = bytes.fromhex("feff010080bb000000770100020010000600000001410000")

# Explicit exact job_id -> clean spoken TTS text overrides
EXPLICIT_JOB_FIXES: dict[str, tuple[str, str]] = {
    # 1. DSS -> Death Star (5 Star Destroyer bridge VO lines)
    "20ba3ebad9bf3eca": (
        "The Death Star is so large, it's run by ten shipmasters. Can you imagine?",
        "dss_to_death_star",
    ),
    "6504287a2f27bd4e": (
        "With the Death Star around, this planet will be ours in no time.",
        "dss_to_death_star",
    ),
    "4ad2352886f9bfc5": (
        "The Imperial direction of the Death Star is an inspiration to behold. It is enough to bring a tear to the eye.",
        "dss_to_death_star",
    ),
    "53c9280d56971e9c": (
        "The Death Star, it's like a super, super Star Destroyer.",
        "dss_to_death_star",
    ),
    "848c63174de24428": (
        "The logistics involved in running the Death Star. I could think about it all day.",
        "dss_to_death_star",
    ),
    # 2. Whisper-mangled vanilla combat & SEAF callouts
    "fe17342ed67fa130": ("Ugh! Need stims!", "whisper_mangle_stims"),
    "46442e93a62c617a": (
        "Grit your teeth, soldier. Grab that stim and patch yourself up.",
        "whisper_mangle_stim",
    ),
    "1173562e21e105a4": (
        "Grit your teeth, soldier. Grab that stim and patch yourself up.",
        "whisper_mangle_stim",
    ),
    "65c25e1cbfe3a535": ("Artifact collected.", "whisper_mangle_artifact"),
    "d8bad71f00ea0530": ("Artifact collected.", "whisper_mangle_artifact"),
    "f5ed5ad100a46570": ("Harmless animal.", "whisper_mangle_harmless"),
    "d1125bd361aeacba": ("Harmless animal.", "whisper_mangle_harmless"),
    "2915cc7dbdc2f897": ("Mag's empty!", "whisper_mangle_mags_empty"),
    "25d3c75de83bcf28": ("Mag's empty!", "whisper_mangle_mags_empty"),
    "6a38d4862c1b998e": ("Mag's empty!", "whisper_mangle_mags_empty"),
    "400987e3c1ccd358": ("Mag's empty!", "whisper_mangle_mags_empty"),
    "533e2b016355b52e": ("For the Empire!", "whisper_mangle_superards"),
    "5b7ab0c9a5add2e7": ("Sweet Emperor, please forgive me!", "whisper_mangle_swag"),
    "c8b5bf09d71dee92": ("Lightening the Empire's load!", "whisper_mangle_lightning_load"),
    "c3cbf7478d27822d": ("Eager to obey!", "whisper_mangle_egot"),
    "0a4f447ba3013f6a": ("Frag! For the Empire!", "whisper_mangle_forsake"),
    "2dbec5b6fc0eb3ff": (
        "Oh, mines! Someone's going to get a leg blown off!",
        "whisper_mangle_lightbulb",
    ),
    "a28e242fcfedaa81": ("Manning emplacement!", "whisper_mangle_man_in"),
    "126bfc98591a8904": ("Manning emplacement!", "asr_slurred_emplacement"),
    "beb9242d71d97e92": ("Manning emplacement!", "asr_slurred_emplacement"),
    "81d765c3a41ed273": ("Forward!", "whisper_mangle_ford"),
    "577d1fa042657cf7": ("Hold the line! This can't be happening!", "whisper_mangle_same_throw"),
    # 3. Awkward Empire lore substitutions
    "de2020c420b10644": ("Tie-1 returns, locked and loaded.", "lore_grammar_empire"),
    "ce8dea0843eab95a": ("Thank the Emperor for the Stormtroopers!", "lore_grammar_empire"),
    "d5eabd8801a56ae4": ("Thank the Emperor for the Stormtroopers!", "lore_grammar_empire"),
    "b96bdafc5d94760f": ("For the Empire!", "lore_grammar_empire"),
    "957c3806b71e6d5f": (
        "The void. I've got the destroyer set to seventy-three degrees Imperial standard, but it still feels cold.",
        "lore_grammar_empire",
    ),
    "1996619252db80f1": (
        "Did you know that most tank production facilities were turned into theme parks after the First Galactic War? "
        "My parents used to take me to this fairy-tale themed one. The chimney towers were painted pink and they said "
        "Empress Victory lived there. And the Stormtroopers all protected her from the evil Dissident King.",
        "lore_grammar_empire",
    ),
    "b98f89fe6fb37cbe": (
        "Those who sacrifice for Imperial glory live forever in our memory.",
        "lore_grammar_empire",
    ),
    "94a0e43de898d802": (
        "So the Techno Union are back, after all those years of peaceful service to the Galactic Empire. "
        "And they'd rather die on the battlefield than live under the Empire? That's stupid.",
        "lore_grammar_empire",
    ),
    # 4. Clones SEAF / lore / double-determiner fixes
    "61992f63abf81a0c": ("Deploying Republic garrison.", "clones_seaf_mangle"),
    "a750c5990ca22ec7": ("Republic garrison extraction complete.", "clones_seaf_mangle"),
    "787433a3ea5a4af0": ("Republic garrison out of the combat zone.", "clones_seaf_mangle"),
    "2713d76adbd602ca": ("Republic garrison en route to objective.", "clones_seaf_mangle"),
    "6d930b2857718d57": ("Republic garrison support inbound.", "clones_seaf_mangle"),
    "6ab6b2b9c6051600": (
        "Senator John Killjoy Junior declares: The Syndicate are back, and they bring with them the threat of total "
        "annihilation. Which is why I say, let us annihilate them first.",
        "clones_lore_illumina",
    ),
    "cfad66629c721bdd": (
        "Close your eyes. Picture yourself within the drop pod. It is a mother's womb. Inside, you are safe. "
        "You have everything you need. You are on the way to where you belong. You will arrive exactly when you are meant to.",
        "clones_lore_hellpod",
    ),
    "49b0380ff3627a14": (
        "In the first intragalactic war alone, the Gee-oh-noh-shuns accounted for the deaths of one hundred eighty-nine "
        "million Republic citizens, many of them babies. Don't let the Gee-oh-noh-shuns kill babies. Fight the good fight.",
        "clones_grammar_pronunciation",
    ),
    "14927867bb045905": (
        "You're finally ready for the realest deal of them all. The Galactic War. Honor. The Republic. Justice. "
        "They're in your hands now. General Brasch, over and out.",
        "clones_grammar_duplicate",
    ),
    "43f8e7723d21e117": (
        "The word of your Republic officer is law. Laws must not be broken.",
        "clones_grammar_determiner",
    ),
    "fd816b34d66b4dba": (
        "The Republic is proud to recommend New Republic Light, an official sponsor of the War Effort.",
        "clones_grammar_determiner",
    ),
    "e73eff82b4ffbf6f": (
        "Don't worry. Top Republic scientists have proven that Gee-oh-noh-shuns don't feel pain.",
        "clones_grammar_pronunciation",
    ),
    "fcbf9e47b8d52576": (
        "Don't forget to smile. Republic Cams are active twenty-four seven.",
        "clones_grammar_determiner",
    ),
    # 5. Spelled-out interjections, numbers, system names & truncated short clips
    "df3e111d8e7f51c8": ("Ugh! That all you got?", "spelled_out_interjection"),
    "4713fa74efc86339": ("I feel... I feel... unstoppable!", "spelled_out_scream"),
    "dfbb32aa9ece7d50": ("Never doubt! Never doubt!", "spelled_out_caps"),
    "879a8637608589f2": ("Awesome!", "spelled_out_caps"),
    "3e2deccad5428a7e": (
        "Fifty billion tons of termicide deployed indiscriminately across an entire planet. "
        "The genius of the T-C-S lies in its simplicity.",
        "number_50_billion",
    ),
    "28a265bb61a7df9c": (
        "Reminder, all personnel must submit weekly urine sample by oh-four-hundred.",
        "number_0400",
    ),
    "ab8e99819fd3b785": ("S-O-S!", "truncated_sos"),
    "12cb87a1deb7c2fc": ("Tac-pack is out!", "homophone_tacs_out"),
    "5eb0555c4910579f": ("The Fury System.", "spelled_out_system"),
    "8987029cba52a3d4": ("The Pahss System.", "spelled_out_system"),
    "ca5b4a15fe0fa6d9": ("The Het System.", "spelled_out_system"),
    "d0572b1281cbe778": ("The Vah-kah System.", "spelled_out_system"),
    "85bd2b77046d64d1": ("The Tor-E System.", "spelled_out_system"),
    "2b3c02a1ea645360": ("The Frah-kah-neh-ree-ah System.", "mispronounced_system"),
    "756621e6bd936d62": ("Alpha!", "truncated_short_callout"),
    "20385d8dde14d29a": ("Changing mag!", "truncated_short_callout"),
    "54e889db6b0407b5": ("Up!", "truncated_short_callout"),
    "f48a013949713e2b": ("Zero!", "truncated_short_callout"),
    "9ac3b36147025974": ("Rebels!", "truncated_short_callout"),
    "0f88e4bb78279fe8": ("Far!", "slurred_short_callout"),
    "199cb18a0cbb62ca": ("Rebel outpost spotted!", "slurred_callout"),
    "a968c6aaa13800b2": ("Rebels teleporting in!", "slurred_callout"),
    "48e346210aedf8a5": ("Gotta reload!", "slurred_callout"),
    "1e9127203bba6fa3": ("Reloading you!", "slurred_callout"),
    "8326aa33439458b9": ("Coming in hot!", "slurred_callout"),
    "80f7bc18456874b6": ("Should have stayed home, scum!", "slurred_callout"),
}


def apply_phonetic_rules(jid: str, text: str) -> tuple[str | None, list[str]]:
    """Return (rewritten_tts_text, reasons) if this job needs pronunciation/lore regeneration, else (None, [])."""
    if jid in EXPLICIT_JOB_FIXES:
        new_txt, reason = EXPLICIT_JOB_FIXES[jid]
        return new_txt, [reason]

    cur = text
    reasons: list[str] = []

    # Rule 1: Whisper "Century" -> "Sentry" (14 stratagem lines)
    if re.search(r"\bcentury\b", cur, re.IGNORECASE):
        cur = re.sub(r"\bCentury\b", "Sentry", cur)
        cur = re.sub(r"\bcentury\b", "sentry", cur)
        reasons.append("whisper_century_to_sentry")

    # Rule 2: "TIE One" -> "Tie-1" and standalone "TIE" / "Tie." -> "Tie" / "Tie fighter!"
    if re.search(r"\bTIE\s+One\b", cur, re.IGNORECASE):
        cur = re.sub(r"\bTIE\s+One\b", "Tie-1", cur, flags=re.IGNORECASE)
        reasons.append("tie_one_taiwan_fix")
    if re.fullmatch(r"\s*TIE[.!]?\s*", cur, re.IGNORECASE):
        cur = "Tie fighter!"
        reasons.append("standalone_tie_clarity")
    elif re.search(r"\bTIE\b", cur):
        # Uppercase TIE gets spelled T-I by Qwen3-TTS on female/pilot voices
        cur = re.sub(r"\bTIE\b", "Tie", cur)
        reasons.append("uppercase_tie_phonetic")

    # Rule 3: Star Wars acronyms AT-ST, AT-AT, AT-TE, LAAT, FTL, TAC PAC
    acronym_Subs = [
        (r"\bAT-STs\b", "A-T-S-Ts", "acronym_atst"),
        (r"\bAT-ST\b", "A-T-S-T", "acronym_atst"),
        (r"\bAT-ATs\b", "A-T-A-Ts", "acronym_atat"),
        (r"\bAT-AT\b", "A-T-A-T", "acronym_atat"),
        (r"\bAT-TEs\b", "A-T-T-Es", "acronym_atte"),
        (r"\bAT-TE\b", "A-T-T-E", "acronym_atte"),
        (r"\bLAAT\b", "L-A-A-T", "acronym_laat"),
        (r"\bFTL\b", "F-T-L", "acronym_ftl"),
        (r"\bTAC\s+PAC\b", "tac-pack", "acronym_tac_pac"),
    ]
    for pat, rep, tag in acronym_Subs:
        if re.search(pat, cur):
            cur = re.sub(pat, rep, cur)
            reasons.append(tag)

    # Rule 4: Star Wars proper nouns (Tibanna, Jedha, Geonosian)
    if re.search(r"\btibanna\b", cur, re.IGNORECASE):
        cur = re.sub(r"\btibanna\b", "tih-bah-nah", cur, flags=re.IGNORECASE)
        reasons.append("proper_noun_tibanna")
    if re.search(r"\bJedha\b", cur, re.IGNORECASE):
        cur = re.sub(r"\bJedha\b", "Jed-ah", cur, flags=re.IGNORECASE)
        reasons.append("proper_noun_jedha")
    if re.search(r"\bGeonosians\b", cur, re.IGNORECASE):
        cur = re.sub(r"\bGeonosians\b", "Gee-oh-noh-shuns", cur, flags=re.IGNORECASE)
        reasons.append("proper_noun_geonosians")
    elif re.search(r"\bGeonosian\b", cur, re.IGNORECASE):
        cur = re.sub(r"\bGeonosian\b", "Gee-oh-noh-shun", cur, flags=re.IGNORECASE)
        reasons.append("proper_noun_geonosian")

    # Rule 5: Clones double determiner "under the Republic Control" / "protect our the Republic"
    if re.search(r"\bunder\s+the\s+Republic\s+control\b", cur, re.IGNORECASE):
        cur = re.sub(r"\bunder\s+the\s+Republic\s+[Cc]ontrol\b", "under Republic control", cur)
        reasons.append("clones_grammar_under_republic")
    if re.search(r"\b(our|your|my|their)\s+the\s+Republic\b", cur, re.IGNORECASE):
        cur = re.sub(r"\b(our|your|my|their)\s+the\s+Republic\b", r"\1 Republic", cur, flags=re.IGNORECASE)
        reasons.append("clones_grammar_possessive_republic")

    if not reasons:
        return None, []
    return cur, reasons


def file_sha256(path: Path) -> str:
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()


def extract_click_tail_template(p1032_main: Path, p1032_stream: Path, m24: dict) -> np.ndarray:
    """Extract the exact 90ms (4,320 samples @ 48 kHz) radio squelch click tail from r54 patch_1032."""
    rows = {f"{r[0]:016x}.{r[1]:016x}": r for r in P.directory(p1032_main)}
    with p1032_stream.open("rb") as fs:
        for skey, info in m24["empire_tts_streams"].items():
            p = info.get("persona")
            if p == "stormtrooper_male1" and m24["personas"][p].get("add_click_tail") and skey in rows:
                row = rows[skey]
                if row[8] > 64 + 48000 * 2:  # > 1s stream for clean separation
                    fs.seek(row[3])
                    wem = fs.read(row[8])
                    pcm = np.frombuffer(wem[64:], dtype=np.int16).astype(np.float32) / 32768.0
                    tail = pcm[-4320:].copy()
                    # Normalize template to unit RMS so we scale precisely before LUFS normalization
                    rms = float(np.sqrt(np.mean(tail * tail)))
                    if rms > 1e-4:
                        return tail / rms
    raise RuntimeError("Could not extract radio click tail template from patch_1032")


def trim_silence_24k(y: np.ndarray, sr: int = 24000) -> np.ndarray:
    y = np.asarray(y, dtype=np.float32).reshape(-1)
    if len(y) == 0:
        return y
    y = y - float(np.mean(y))
    peak = float(np.max(np.abs(y)))
    if peak < 1e-5:
        return y
    # Smooth 10ms envelope
    win = max(1, int(0.010 * sr))
    env = np.sqrt(np.convolve(y * y, np.ones(win, dtype=np.float32) / win, mode="same"))
    thr = max(peak * 0.012, 1e-4)  # ~ -38.4 dB relative to peak
    idx = np.flatnonzero(env > thr)
    if len(idx) == 0:
        return y
    pad_head = int(0.022 * sr)
    pad_tail = int(0.045 * sr)
    i0 = max(0, int(idx[0]) - pad_head)
    i1 = min(len(y), int(idx[-1]) + pad_tail)
    out = y[i0:i1].copy()
    # 8ms cosine fade-in / 12ms fade-out to prevent boundary clicks
    fi = min(int(0.008 * sr), len(out) // 4)
    fo = min(int(0.012 * sr), len(out) // 4)
    if fi > 1:
        out[:fi] *= 0.5 * (1.0 - np.cos(np.linspace(0.0, math.pi, fi, dtype=np.float32)))
    if fo > 1:
        out[-fo:] *= 0.5 * (1.0 + np.cos(np.linspace(0.0, math.pi, fo, dtype=np.float32)))
    return out


def apply_v24_dsp_48k(
    y24: np.ndarray,
    sr_in: int,
    dsp_mode: str,
    add_click_tail: bool,
    target_lufs: float,
    click_template_unit_rms: np.ndarray,
    meter48: pyln.Meter,
) -> tuple[bytes, float, float, float]:
    """Resample to 48 kHz, apply exact v24 persona radio/room DSP, LUFS normalize, and wrap as Wwise PCM .wem."""
    trimmed = trim_silence_24k(y24, sr=sr_in)
    if sr_in == 24000:
        y48 = signal.resample_poly(trimmed, 2, 1).astype(np.float32)
    elif sr_in == 48000:
        y48 = trimmed.astype(np.float32)
    else:
        g = math.gcd(48000, sr_in)
        y48 = signal.resample_poly(trimmed, 48000 // g, sr_in // g).astype(np.float32)

    nyq = 24000.0
    if dsp_mode in ("helmet_radio", "stormtrooper_helmet_comm"):
        sos = signal.butter(4, [310.0 / nyq, 3450.0 / nyq], btype="bandpass", output="sos")
        y48 = signal.sosfiltfilt(sos, y48).astype(np.float32)
        # Subtle 1.8 kHz presence + helmet comm soft saturation matching v24 (-9 dBFS crest @ -19 LUFS)
        b_pk, a_pk = signal.iirpeak(1850.0 / nyq, Q=1.6)
        y48 = (y48 + 0.35 * signal.lfilter(b_pk, a_pk, y48)).astype(np.float32)
        rms = max(float(np.sqrt(np.mean(y48 * y48))), 1e-6)
        y48 = (np.tanh(1.55 * (y48 / (3.0 * rms))) / math.tanh(1.55) * (3.0 * rms)).astype(np.float32)
    elif dsp_mode == "orbital_comm":
        sos = signal.butter(4, [250.0 / nyq, 3950.0 / nyq], btype="bandpass", output="sos")
        y48 = signal.sosfiltfilt(sos, y48).astype(np.float32)
        rms = max(float(np.sqrt(np.mean(y48 * y48))), 1e-6)
        y48 = (np.tanh(1.30 * (y48 / (3.2 * rms))) / math.tanh(1.30) * (3.2 * rms)).astype(np.float32)
    elif dsp_mode == "tactical_officer":
        sos = signal.butter(4, [240.0 / nyq, 4150.0 / nyq], btype="bandpass", output="sos")
        y48 = signal.sosfiltfilt(sos, y48).astype(np.float32)
        rms = max(float(np.sqrt(np.mean(y48 * y48))), 1e-6)
        y48 = (np.tanh(1.35 * (y48 / (3.2 * rms))) / math.tanh(1.35) * (3.2 * rms)).astype(np.float32)
    elif dsp_mode == "bridge_officer":
        if add_click_tail:
            sos = signal.butter(4, [250.0 / nyq, 4200.0 / nyq], btype="bandpass", output="sos")
            y48 = signal.sosfiltfilt(sos, y48).astype(np.float32)
            rms = max(float(np.sqrt(np.mean(y48 * y48))), 1e-6)
            y48 = (np.tanh(1.25 * (y48 / (3.4 * rms))) / math.tanh(1.25) * (3.4 * rms)).astype(np.float32)
        else:
            sos = signal.butter(2, [85.0 / nyq, 11500.0 / nyq], btype="bandpass", output="sos")
            y48 = signal.sosfiltfilt(sos, y48).astype(np.float32)
    else:  # ship_crew
        sos = signal.butter(2, [80.0 / nyq, 12000.0 / nyq], btype="bandpass", output="sos")
        y48 = signal.sosfiltfilt(sos, y48).astype(np.float32)

    speech_rms = max(float(np.sqrt(np.mean(y48 * y48))), 1e-6)
    if add_click_tail:
        # Match exact v24 speech-to-click RMS ratio (~0.0245 / 0.112 = 0.2188)
        tail_scaled = (click_template_unit_rms * (speech_rms * 0.218)).astype(np.float32)
        gap = np.zeros(int(0.010 * 48000), dtype=np.float32)
        y48 = np.concatenate([y48, gap, tail_scaled])

    # Loudness normalization (handle sub-400ms short callouts where BS.1770 gating returns -inf)
    if len(y48) >= int(0.42 * 48000):
        cur_lufs = float(meter48.integrated_loudness(y48))
    else:
        padded = np.tile(y48, int(math.ceil(0.45 * 48000 / max(len(y48), 1))))
        cur_lufs = float(meter48.integrated_loudness(padded))

    if math.isfinite(cur_lufs) and cur_lufs > -70.0:
        gain = 10.0 ** ((target_lufs - cur_lufs) / 20.0)
        y48 = y48 * gain
    else:
        target_rms = 10.0 ** (target_lufs / 20.0)
        y48 = y48 * (target_rms / speech_rms)

    # True-peak ceiling at -1.5 dBFS (0.8414)
    ceiling = 10.0 ** (-1.5 / 20.0)
    pk = float(np.max(np.abs(y48)))
    if pk > ceiling:
        # Smooth soft-clip above -3.0 dBFS into -1.5 dBFS ceiling
        knee = 10.0 ** (-3.0 / 20.0)
        mag = np.abs(y48)
        over = mag > knee
        y48[over] = np.sign(y48[over]) * (
            knee + (ceiling - knee) * np.tanh((mag[over] - knee) / max(ceiling - knee, 1e-6))
        )
        pk2 = float(np.max(np.abs(y48)))
        if pk2 > ceiling:
            y48 *= ceiling / pk2

    final_peak_db = 20.0 * math.log10(max(float(np.max(np.abs(y48))), 1e-9))
    if len(y48) >= int(0.42 * 48000):
        final_lufs = float(meter48.integrated_loudness(y48))
    else:
        padded = np.tile(y48, int(math.ceil(0.45 * 48000 / max(len(y48), 1))))
        final_lufs = float(meter48.integrated_loudness(padded))

    pcm16 = np.clip(np.round(y48 * 32767.0), -32768, 32767).astype("<i2").tobytes()
    wem = (
        b"RIFF"
        + struct.pack("<I", 56 + len(pcm16))
        + b"WAVEfmt \x18\x00\x00\x00"
        + WEM_FMT_CHUNK
        + b"JUNK\x04\x00\x00\x00\x00\x00\x00\x00data"
        + struct.pack("<I", len(pcm16))
        + pcm16
    )
    return wem, len(y48) / 48000.0, final_lufs, final_peak_db


def resolve_archive_pair(
    exp_main: Path | None,
    blob_dirs: list[Path],
    main_sha: str,
    stream_sha: str,
) -> tuple[Path, Path]:
    if exp_main is not None and exp_main.is_file():
        st = exp_main.with_name(exp_main.name + ".stream")
        if st.is_file() and file_sha256(exp_main) == main_sha and file_sha256(st) == stream_sha:
            return exp_main, st
    mp = next((d / main_sha for d in blob_dirs if (d / main_sha).is_file()), None)
    sp = next((d / stream_sha for d in blob_dirs if (d / stream_sha).is_file()), None)
    if mp is None or sp is None:
        raise FileNotFoundError(f"Missing cached archive pair main={main_sha} stream={stream_sha}")
    return mp, sp


def rebuild_voice_archive(
    in_main: Path,
    in_stream: Path,
    out_main: Path,
    out_stream: Path,
    replacements: dict[tuple[int, int], bytes],
    skip_keys: set[tuple[int, int]] | None = None,
) -> dict:
    """Rebuild a voice archive (main + .stream + 0 B .gpu_resources), updating only replaced STREAM keys."""
    skip_keys = skip_keys or set()
    header = in_main.read_bytes()[:72]
    rows = P.directory(in_main)
    companions = (in_main, in_stream, None)
    resources: dict[tuple[int, int], object] = {}
    replaced_count = 0
    preserved_banks = 0
    for row in rows:
        key = (row[0], row[1])
        res = P.Resource(in_main, row, companions)
        if key[1] == BANK:
            preserved_banks += 1
        if key in replacements and key not in skip_keys:
            orig_m0 = res.parts()[0]
            if len(orig_m0) != 12:
                raise ValueError(f"Unexpected stream main wrapper size {len(orig_m0)} for {key[0]:016x}")
            new_wem = replacements[key]
            if P.fmt(new_wem) != 0xFFFE:
                raise ValueError(f"Invalid replacement WEM format for {key[0]:016x}")
            new_m0 = orig_m0[:8] + struct.pack("<I", len(new_wem))
            resources[key] = P.Generated((new_m0, new_wem, b""))
            replaced_count += 1
        else:
            resources[key] = res

    out_main.parent.mkdir(parents=True, exist_ok=True)
    P.write_archive(out_main, header, resources)
    # write_archive writes out_main, out_main.stream, out_main.gpu_resources
    actual_stream = out_main.with_name(out_main.name + ".stream")
    actual_gpu = out_main.with_name(out_main.name + ".gpu_resources")
    if actual_stream != out_stream:
        actual_stream.replace(out_stream)
    if actual_gpu.stat().st_size != 0:
        raise ValueError(f"Expected 0 B .gpu_resources for {out_main}, got {actual_gpu.stat().st_size}")

    # Verify roundtrip directory & preflight
    import importlib.util
    spec = importlib.util.spec_from_file_location(
        "slim_preflight", str(HERE.parent / "slim-archive-preflight.py")
    )
    preflight = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(preflight)
    pf = preflight.inspect_path(out_main, (out_stream.stat().st_size, 0))
    if not pf["passed"]:
        raise RuntimeError(f"Preflight failed on {out_main}: {pf['issues']}")

    return {
        "main_path": str(out_main),
        "main_size": out_main.stat().st_size,
        "main_sha256": file_sha256(out_main),
        "stream_path": str(out_stream),
        "stream_size": out_stream.stat().st_size,
        "stream_sha256": file_sha256(out_stream),
        "total_resources": len(resources),
        "replaced_streams": replaced_count,
        "preserved_banks": preserved_banks,
        "preflight_passed": pf["passed"],
    }


def build_delta_voice_archive(
    in_main: Path,
    out_main: Path,
    out_stream: Path,
    replacements: dict[tuple[int, int], bytes],
) -> dict:
    """Build a compact delta voice archive (main + .stream + 0 B .gpu_resources) containing only replaced STREAMs."""
    header = in_main.read_bytes()[:72]
    rows = { (r[0], r[1]): r for r in P.directory(in_main) }
    resources: dict[tuple[int, int], object] = {}
    for key, new_wem in sorted(replacements.items()):
        if key not in rows:
            raise KeyError(f"Stream key {key[0]:016x} absent from {in_main}")
        row = rows[key]
        with in_main.open("rb") as fm:
            fm.seek(row[2])
            orig_m0 = fm.read(row[7])
        if len(orig_m0) != 12:
            raise ValueError(f"Unexpected stream main wrapper size {len(orig_m0)} for {key[0]:016x}")
        if P.fmt(new_wem) != 0xFFFE:
            raise ValueError(f"Invalid replacement WEM format for {key[0]:016x}")
        new_m0 = orig_m0[:8] + struct.pack("<I", len(new_wem))
        resources[key] = P.Generated((new_m0, new_wem, b""))

    out_main.parent.mkdir(parents=True, exist_ok=True)
    P.write_archive(out_main, header, resources)
    actual_stream = out_main.with_name(out_main.name + ".stream")
    actual_gpu = out_main.with_name(out_main.name + ".gpu_resources")
    if actual_stream != out_stream:
        actual_stream.replace(out_stream)
    if actual_gpu.stat().st_size != 0:
        raise ValueError(f"Expected 0 B .gpu_resources for {out_main}, got {actual_gpu.stat().st_size}")

    import importlib.util
    spec = importlib.util.spec_from_file_location(
        "slim_preflight", str(HERE.parent / "slim-archive-preflight.py")
    )
    preflight = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(preflight)
    pf = preflight.inspect_path(out_main, (out_stream.stat().st_size, 0))
    if not pf["passed"]:
        raise RuntimeError(f"Preflight failed on {out_main}: {pf['issues']}")

    return {
        "mode": "delta_overlay",
        "main_path": str(out_main),
        "main_size": out_main.stat().st_size,
        "main_sha256": file_sha256(out_main),
        "stream_path": str(out_stream),
        "stream_size": out_stream.stat().st_size,
        "stream_sha256": file_sha256(out_stream),
        "total_resources": len(resources),
        "replaced_streams": len(resources),
        "preflight_passed": pf["passed"],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--v24-dir", type=Path, default=DEFAULT_V24_DIR)
    ap.add_argument("--v22-dir", type=Path, default=DEFAULT_V22_DIR)
    ap.add_argument("--model-snap", type=Path, default=DEFAULT_MODEL_SNAP)
    ap.add_argument("--r54-exp", type=Path, default=DEFAULT_R54_EXP)
    ap.add_argument("--blob-cache", type=Path, default=DEFAULT_BLOB_CACHE)
    ap.add_argument("--clones-r26", type=Path, default=DEFAULT_CLONES_R26)
    ap.add_argument("--out-root", type=Path, default=DEFAULT_OUT_ROOT)
    ap.add_argument("--batch-size", type=int, default=8)
    args = ap.parse_args()

    t_start = time.time()
    m24 = json.loads((args.v24_dir / "manifest_v24.json").read_text(encoding="utf-8"))
    personas = m24["personas"]

    blob_dirs = [
        args.blob_cache,
        args.clones_r26 / "blobs",
        args.clones_r26 / "_blob_cache",
    ]
    p1032_in_main, p1032_in_stream = resolve_archive_pair(
        args.r54_exp / "9ba626afa44a3aa3.patch_1032",
        blob_dirs,
        "83fe47a73bc55d76ee36d0223c1f95832fd07b1440f5a9e98a032f118c338a44",
        "af0f339efe13342796e33e2f27e099f63e6af848ffe170f408cb7bec27a5b6fa",
    )
    p1033_in_main, p1033_in_stream = resolve_archive_pair(
        None,
        blob_dirs,
        "a486629c685219fc7b893a976759aaa5234aa3963f4437d8864e85b6a7a59ac5",
        "f0adfe7b77ee693d18c16bccdee9acd94d8d99c7bb78450e85f5e5a97e618612",
    )
    p33_main_sha = "42ab1486b5c7a04905dd7824cdc116415bb87568c9cd6f8ed9c969f18ad47667"
    p33_stream_sha = "c6dbe15829a03f30396973c862dacc3875f59bc6ed42d9e301756db6af5bc9f0"
    p33_in_main = next((d / p33_main_sha for d in blob_dirs if (d / p33_main_sha).is_file()), None)
    if p33_in_main is None:
        raise FileNotFoundError(f"Missing Clones patch_33 main header {p33_main_sha}")
    p33_in_stream = next((d / p33_stream_sha for d in blob_dirs if (d / p33_stream_sha).is_file()), None)

    p1032_keys = {(r[0], r[1]) for r in P.directory(p1032_in_main)}
    p1033_keys = {(r[0], r[1]) for r in P.directory(p1033_in_main)}
    p33_keys = {(r[0], r[1]) for r in P.directory(p33_in_main)}

    click_template = extract_click_tail_template(p1032_in_main, p1032_in_stream, m24)
    print(f"Extracted 90ms click-tail template from patch_1032 ({len(click_template)} samples)", flush=True)

    # Collect all unique active jobs and map to stream destinations
    jobs: dict[str, dict] = {}
    for skey_str, info in m24["empire_tts_streams"].items():
        jid = info["job_id"]
        k_tuple = (int(skey_str.split(".")[0], 16), int(skey_str.split(".")[1], 16))
        if k_tuple == UNTOUCHED_P1033_KEY:
            continue
        p = info["persona"]
        txt = info.get("imperial_text") or info.get("text") or ""
        new_txt, reasons = apply_phonetic_rules(jid, txt)
        if new_txt is None:
            continue
        rec = jobs.setdefault(
            jid,
            {
                "job_id": jid,
                "persona": p,
                "old_text": txt,
                "new_text": new_txt,
                "reasons": reasons,
                "p1032_targets": [],
                "p1033_targets": [],
                "p33_targets": [],
            },
        )
        p_cfg = personas[p]
        target_spec = (
            k_tuple,
            info.get("dsp_mode") or p_cfg["dsp_mode"],
            bool(info.get("add_click_tail", p_cfg["add_click_tail"])),
            float(info.get("target_lufs", -19.0 if p_cfg["add_click_tail"] else -20.5)),
        )
        if k_tuple in p1032_keys:
            rec["p1032_targets"].append(target_spec)
        if k_tuple in p1033_keys:
            rec["p1033_targets"].append(target_spec)

    # Also check slot4_clean_sources in case any Slot 4 stream maps by raw wav stem
    for skey_str, s4 in m24.get("slot4_clean_sources", {}).items():
        k_tuple = (int(skey_str.split(".")[0], 16), int(skey_str.split(".")[1], 16))
        if k_tuple == UNTOUCHED_P1033_KEY or k_tuple not in p1033_keys:
            continue
        jid = Path(s4["wav"]).stem
        if jid in jobs:
            existing_keys = {t[0] for t in jobs[jid]["p1033_targets"]}
            if k_tuple not in existing_keys:
                p = jobs[jid]["persona"]
                p_cfg = personas[p]
                jobs[jid]["p1033_targets"].append(
                    (k_tuple, p_cfg["dsp_mode"], bool(p_cfg["add_click_tail"]), -19.0)
                )

    for skey_str, info in m24["clones_tts_streams"].items():
        jid = info["job_id"]
        k_tuple = (int(skey_str.split(".")[0], 16), int(skey_str.split(".")[1], 16))
        p = info["persona"]
        txt = info.get("republic_text") or info.get("text") or ""
        new_txt, reasons = apply_phonetic_rules(jid, txt)
        if new_txt is None:
            continue
        rec = jobs.setdefault(
            jid,
            {
                "job_id": jid,
                "persona": p,
                "old_text": txt,
                "new_text": new_txt,
                "reasons": reasons,
                "p1032_targets": [],
                "p1033_targets": [],
                "p33_targets": [],
            },
        )
        p_cfg = personas[p]
        target_spec = (
            k_tuple,
            info.get("dsp_mode") or p_cfg["dsp_mode"],
            bool(info.get("add_click_tail", p_cfg["add_click_tail"])),
            float(info.get("target_lufs", -18.0)),
        )
        if k_tuple in p33_keys:
            rec["p33_targets"].append(target_spec)

    total_p1032 = sum(len(j["p1032_targets"]) for j in jobs.values())
    total_p1033 = sum(len(j["p1033_targets"]) for j in jobs.values())
    total_p33 = sum(len(j["p33_targets"]) for j in jobs.values())
    print(
        f"Flagged {len(jobs)} unique TTS jobs -> "
        f"patch_1032={total_p1032} streams, patch_1033={total_p1033} streams, clones_patch_33={total_p33} streams",
        flush=True,
    )

    raw_out_dir = args.out_root / "voices_raw_r55"
    raw_out_dir.mkdir(parents=True, exist_ok=True)

    # Group jobs by persona for fast batched Qwen3-TTS synthesis
    by_persona: dict[str, list[dict]] = {}
    for j in jobs.values():
        by_persona.setdefault(j["persona"], []).append(j)

    needed_personas = sorted(by_persona.keys())
    print(f"Loading Qwen3-TTS-12Hz-1.7B-Base on CUDA for {len(needed_personas)} personas...", flush=True)
    from qwen_tts import Qwen3TTSModel

    tts = Qwen3TTSModel.from_pretrained(
        str(args.model_snap),
        device_map="cuda",
        torch_dtype=torch.bfloat16,
    )

    # Precompute voice clone prompts from cached 24k prompt wavs on D:\ClaudeWork\cache (never touching C:\Users\goode\OneDrive!)
    prompts_cache: dict[str, list] = {}
    for p in needed_personas:
        p24 = args.v24_dir / "prompts_24k" / f"{p}.wav"
        if not p24.is_file():
            p24 = args.v22_dir / "prompts_24k" / f"{p}.wav"
        if not p24.is_file():
            raise FileNotFoundError(f"Missing 24 kHz reference prompt WAV for persona {p}")
        ref_text = " ".join(personas[p]["texts"])
        prompts_cache[p] = tts.create_voice_clone_prompt(
            ref_audio=str(p24),
            ref_text=ref_text,
            x_vector_only_mode=False,
        )
        print(f"  Built prompt for {p:30s} from {p24}", flush=True)

    synthesized_wavs: dict[str, tuple[np.ndarray, int, int]] = {}
    for p in needed_personas:
        p_jobs = by_persona[p]
        # Check if any raw wav already generated in raw_out_dir with matching metadata
        pending = []
        for j in p_jobs:
            cached_wav = raw_out_dir / f"{j['job_id']}.wav"
            cached_meta = raw_out_dir / f"{j['job_id']}.json"
            if cached_wav.is_file() and cached_meta.is_file():
                meta = json.loads(cached_meta.read_text(encoding="utf-8"))
                if meta.get("new_text") == j["new_text"]:
                    y_c, sr_c = sf.read(str(cached_wav), dtype="float32")
                    synthesized_wavs[j["job_id"]] = (y_c, sr_c, int(meta.get("attempts", 1)))
                    continue
            pending.append(j)

        if not pending:
            print(f"  [{p}] All {len(p_jobs)} jobs cached in {raw_out_dir}", flush=True)
            continue

        print(f"  [{p}] Synthesizing {len(pending)}/{len(p_jobs)} jobs (batch_size={args.batch_size})...", flush=True)
        for b0 in range(0, len(pending), args.batch_size):
            batch = pending[b0 : b0 + args.batch_size]
            texts = [x["new_text"] for x in batch]
            torch.manual_seed(20261128 + b0)
            wavs, sr = tts.generate_voice_clone(
                text=texts,
                language="English",
                voice_clone_prompt=prompts_cache[p],
                do_sample=True,
                temperature=0.68,
                top_p=0.88,
                top_k=40,
                repetition_penalty=1.05,
                max_new_tokens=512,
            )
            for j, w in zip(batch, wavs, strict=True):
                trimmed = trim_silence_24k(w, sr=sr)
                dur = len(trimmed) / float(sr)
                wc = max(1, len(j["new_text"].split()))
                min_dur = 0.36 if wc == 1 else max(0.48, 0.16 * wc)
                max_dur = max(3.0, 0.85 * wc + 2.2)
                rms = float(np.sqrt(np.mean(trimmed * trimmed))) if len(trimmed) else 0.0
                attempt = 1
                best_w = w
                best_dur = dur
                while (dur < min_dur or dur > max_dur or rms < 0.008) and attempt < 4:
                    attempt += 1
                    torch.manual_seed(20261128 + attempt * 9973 + hash(j["job_id"]) % 10000)
                    retry_txt = j["new_text"]
                    if dur < min_dur and not retry_txt.endswith("!"):
                        retry_txt = retry_txt.rstrip(".") + "!"
                    rw_list, sr = tts.generate_voice_clone(
                        text=[retry_txt],
                        language="English",
                        voice_clone_prompt=prompts_cache[p],
                        do_sample=True,
                        temperature=0.62,
                        top_p=0.85,
                        top_k=35,
                        repetition_penalty=1.06,
                        max_new_tokens=384,
                    )
                    rw = rw_list[0]
                    t_rw = trim_silence_24k(rw, sr=sr)
                    d_rw = len(t_rw) / float(sr)
                    r_rw = float(np.sqrt(np.mean(t_rw * t_rw))) if len(t_rw) else 0.0
                    if min_dur <= d_rw <= max_dur and r_rw >= 0.008:
                        best_w = rw
                        dur = d_rw
                        rms = r_rw
                        break
                    if abs(d_rw - 0.5 * (min_dur + max_dur)) < abs(best_dur - 0.5 * (min_dur + max_dur)):
                        best_w = rw
                        best_dur = d_rw
                        dur = d_rw
                        rms = r_rw

                out_wav = raw_out_dir / f"{j['job_id']}.wav"
                sf.write(str(out_wav), best_w, sr, subtype="PCM_16")
                (raw_out_dir / f"{j['job_id']}.json").write_text(
                    json.dumps(
                        {
                            "job_id": j["job_id"],
                            "persona": p,
                            "old_text": j["old_text"],
                            "new_text": j["new_text"],
                            "reasons": j["reasons"],
                            "attempts": attempt,
                            "trimmed_dur_sec": round(len(trim_silence_24k(best_w, sr=sr)) / float(sr), 4),
                        },
                        indent=2,
                    ),
                    encoding="utf-8",
                )
                synthesized_wavs[j["job_id"]] = (best_w, sr, attempt)

    # Free GPU VRAM immediately after synthesis
    del tts
    del prompts_cache
    torch.cuda.empty_cache()

    print("Applying v24 DSP, LUFS normalization, and encoding 48 kHz 16-bit PCM .wem streams...", flush=True)
    meter48 = pyln.Meter(48000)
    p1032_replacements: dict[tuple[int, int], bytes] = {}
    p1033_replacements: dict[tuple[int, int], bytes] = {}
    p33_replacements: dict[tuple[int, int], bytes] = {}
    job_reports: list[dict] = []

    for jid, j in sorted(jobs.items()):
        y_raw, sr_raw, attempts = synthesized_wavs[jid]
        dsp_cache: dict[tuple[str, bool, float], tuple[bytes, float, float, float]] = {}
        for bucket_name, target_list, dest_dict in (
            ("p1032", j["p1032_targets"], p1032_replacements),
            ("p1033", j["p1033_targets"], p1033_replacements),
            ("clones_p33", j["p33_targets"], p33_replacements),
        ):
            for k_tuple, dsp_mode, click, tlufs in target_list:
                cfg = (dsp_mode, click, tlufs)
                if cfg not in dsp_cache:
                    dsp_cache[cfg] = apply_v24_dsp_48k(
                        y_raw, sr_raw, dsp_mode, click, tlufs, click_template, meter48
                    )
                dest_dict[k_tuple] = dsp_cache[cfg][0]

        sample_metrics = next(iter(dsp_cache.values())) if dsp_cache else (b"", 0.0, 0.0, 0.0)
        job_reports.append(
            {
                "job_id": jid,
                "persona": j["persona"],
                "reasons": j["reasons"],
                "old_text": j["old_text"],
                "new_text": j["new_text"],
                "attempts": attempts,
                "wem_dur_sec": round(sample_metrics[1], 3),
                "lufs": round(sample_metrics[2], 2),
                "peak_dbfs": round(sample_metrics[3], 2),
                "p1032_streams": [f"{t[0][0]:016x}" for t in j["p1032_targets"]],
                "p1033_streams": [f"{t[0][0]:016x}" for t in j["p1033_targets"]],
                "clones_p33_streams": [f"{t[0][0]:016x}" for t in j["p33_targets"]],
            }
        )

    shared_dir = args.out_root / "patches" / "shared"
    clones_overlay_dir = args.out_root / "clones-voices-r26-overlay"

    print("Rebuilding Empire patch_1032 + .stream...", flush=True)
    p1032_res = rebuild_voice_archive(
        p1032_in_main,
        p1032_in_stream,
        shared_dir / "9ba626afa44a3aa3.patch_1032",
        shared_dir / "9ba626afa44a3aa3.patch_1032.stream",
        p1032_replacements,
    )

    print("Rebuilding Empire patch_1033 + .stream (standard British Imperial, b7cc016f2537e3d3 untouched)...", flush=True)
    p1033_res = rebuild_voice_archive(
        p1033_in_main,
        p1033_in_stream,
        shared_dir / "9ba626afa44a3aa3.patch_1033",
        shared_dir / "9ba626afa44a3aa3.patch_1033.stream",
        p1033_replacements,
        skip_keys={UNTOUCHED_P1033_KEY},
    )

    if p33_in_stream is not None:
        print("Rebuilding full Clones r26 overlay patch_33 + .stream...", flush=True)
        p33_res = rebuild_voice_archive(
            p33_in_main,
            p33_in_stream,
            clones_overlay_dir / "9ba626afa44a3aa3.patch_33",
            clones_overlay_dir / "9ba626afa44a3aa3.patch_33.stream",
            p33_replacements,
        )
    else:
        print("Building compact Clones r26 voice delta overlay (9ba626afa44a3aa3.patch_47 + .stream)...", flush=True)
        p33_res = build_delta_voice_archive(
            p33_in_main,
            clones_overlay_dir / "9ba626afa44a3aa3.patch_47",
            clones_overlay_dir / "9ba626afa44a3aa3.patch_47.stream",
            p33_replacements,
        )

    # Verify b7cc016f2537e3d3 in patch_1033 is 100% bit-identical to r54 input
    r_in_map = {(r[0], r[1]): r for r in P.directory(p1033_in_main)}
    r_out_map = {(r[0], r[1]): r for r in P.directory(shared_dir / "9ba626afa44a3aa3.patch_1033")}
    if UNTOUCHED_P1033_KEY in r_in_map:
        p_in = P.Resource(p1033_in_main, r_in_map[UNTOUCHED_P1033_KEY], (p1033_in_main, p1033_in_stream, None)).parts()
        p_out = P.Resource(
            shared_dir / "9ba626afa44a3aa3.patch_1033",
            r_out_map[UNTOUCHED_P1033_KEY],
            (
                shared_dir / "9ba626afa44a3aa3.patch_1033",
                shared_dir / "9ba626afa44a3aa3.patch_1033.stream",
                None,
            ),
        ).parts()
        if p_in != p_out:
            raise AssertionError("b7cc016f2537e3d3 in patch_1033 was modified!")

    summary = {
        "elapsed_sec": round(time.time() - t_start, 2),
        "unique_jobs_regenerated": len(jobs),
        "personas_used": needed_personas,
        "patch_1032": p1032_res,
        "patch_1033": p1033_res,
        "clones_patch_33_overlay": p33_res,
        "jobs": job_reports,
    }
    summary_path = args.out_root / "voices_r55_summary.json"
    summary_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"DONE in {summary['elapsed_sec']}s -> {summary_path}", flush=True)
    print(json.dumps({k: v for k, v in summary.items() if k != "jobs"}, indent=2), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
