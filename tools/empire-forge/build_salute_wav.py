#!/usr/bin/env python3
"""Synthesize original deadpan Stormtrooper Salute WAV ("Stormtrooper.") via Qwen3-TTS.

Generates a crisp ~0.65-0.88s 24 kHz 16-bit mono RIFF/WAVE PCM clip (<= 80 KB, 100%
original synthesized voice, zero copyrighted TV audio) with Stormtrooper helmet comm
EQ and a light helmet click, verified with faster-whisper ASR.
"""
from __future__ import annotations

import json
import math
from pathlib import Path
import struct
import sys

import numpy as np
import pyloudnorm as pyln
from scipy import signal
import torch

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_voices_r55 as BV

OUT_WAV = HERE.parents[1] / "forge-mods" / "empire-lego-yoda" / "audio" / "stormtrooper-salute.wav"


def write_pcm16_mono_wav(path: Path, y: np.ndarray, sr: int = 24000) -> dict:
    y = np.asarray(y, dtype=np.float32).reshape(-1)
    pcm = np.clip(np.round(y * 32767.0), -32768, 32767).astype("<i2").tobytes()
    byte_rate = sr * 2
    fmt_chunk = struct.pack("<HHIIHH", 1, 1, sr, byte_rate, 2, 16)
    riff_payload = b"WAVEfmt " + struct.pack("<I", 16) + fmt_chunk + b"data" + struct.pack("<I", len(pcm)) + pcm
    wav_bytes = b"RIFF" + struct.pack("<I", len(riff_payload)) + riff_payload
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(wav_bytes)
    return {"path": str(path), "bytes": len(wav_bytes), "duration_sec": round(len(y) / sr, 3), "sr": sr}


def apply_light_helmet_dsp_24k(y24_raw: np.ndarray, click_24k_unit: np.ndarray) -> np.ndarray:
    trimmed = BV.trim_silence_24k(y24_raw, sr=24000)
    nyq = 12000.0
    # Crisp Stormtrooper helmet mic bandpass (290..3650 Hz) + subtle 1.85 kHz presence + gentle comm warmth
    sos = signal.butter(4, [290.0 / nyq, 3650.0 / nyq], btype="bandpass", output="sos")
    y = signal.sosfiltfilt(sos, trimmed).astype(np.float32)
    b_pk, a_pk = signal.iirpeak(1850.0 / nyq, Q=1.5)
    y = (y + 0.28 * signal.lfilter(b_pk, a_pk, y)).astype(np.float32)
    rms = max(float(np.sqrt(np.mean(y * y))), 1e-6)
    y = (np.tanh(1.35 * (y / (3.1 * rms))) / math.tanh(1.35) * (3.1 * rms)).astype(np.float32)

    speech_rms = max(float(np.sqrt(np.mean(y * y))), 1e-6)
    # Light helmet click tail (~55ms, gentler 0.14x speech_rms for casual deadpan nod)
    click_short = click_24k_unit[: int(0.055 * 24000)].copy()
    fo = int(0.012 * 24000)
    click_short[-fo:] *= 0.5 * (1.0 + np.cos(np.linspace(0.0, math.pi, fo, dtype=np.float32)))
    gap = np.zeros(int(0.012 * 24000), dtype=np.float32)
    y = np.concatenate([y, gap, (click_short * (speech_rms * 0.14)).astype(np.float32)])

    # Normalize to -17.5 LUFS with -1.5 dBFS ceiling so PlaySoundW sits naturally with in-game VO
    meter = pyln.Meter(24000)
    padded = np.tile(y, int(math.ceil(0.50 * 24000 / max(len(y), 1))))
    cur_lufs = float(meter.integrated_loudness(padded))
    if math.isfinite(cur_lufs) and cur_lufs > -70.0:
        y = y * (10.0 ** ((-17.5 - cur_lufs) / 20.0))
    ceiling = 10.0 ** (-1.5 / 20.0)
    pk = float(np.max(np.abs(y)))
    if pk > ceiling:
        y = y * (ceiling / pk)
    return y.astype(np.float32)


def main() -> int:
    v24_dir = BV.DEFAULT_V24_DIR
    m24 = json.loads((v24_dir / "manifest_v24.json").read_text(encoding="utf-8"))
    p1032_m, p1032_s = BV.resolve_archive_pair(
        BV.DEFAULT_R54_EXP / "9ba626afa44a3aa3.patch_1032",
        [BV.DEFAULT_BLOB_CACHE],
        "83fe47a73bc55d76ee36d0223c1f95832fd07b1440f5a9e98a032f118c338a44",
        "af0f339efe13342796e33e2f27e099f63e6af848ffe170f408cb7bec27a5b6fa",
    )
    click48 = BV.extract_click_tail_template(p1032_m, p1032_s, m24)
    click24 = signal.resample_poly(click48, 1, 2).astype(np.float32)
    click24 /= max(float(np.sqrt(np.mean(click24 * click24))), 1e-6)

    from faster_whisper import WhisperModel
    from qwen_tts import Qwen3TTSModel

    tts = Qwen3TTSModel.from_pretrained(
        str(BV.DEFAULT_MODEL_SNAP),
        device_map="cuda",
        torch_dtype=torch.bfloat16,
    )
    asr = WhisperModel("base.en", device="cuda", compute_type="float16")

    candidates = []
    for persona in ("stormtrooper_male1", "stormtrooper_male2"):
        p_wav = v24_dir / "prompts_24k" / f"{persona}.wav"
        ref_text = " ".join(m24["personas"][persona]["texts"])
        prompt = tts.create_voice_clone_prompt(ref_audio=str(p_wav), ref_text=ref_text, x_vector_only_mode=False)
        for seed in (101, 202, 303, 404, 505):
            torch.manual_seed(seed)
            torch.cuda.manual_seed_all(seed)
            wavs, sr = tts.generate_voice_clone(
                text=["Stormtrooper."],
                language=["English"],
                voice_clone_prompt=prompt,
                temperature=0.55,
                top_p=0.85,
            )
            y_raw = np.asarray(wavs[0], dtype=np.float32)
            if sr != 24000:
                g = math.gcd(24000, sr)
                y_raw = signal.resample_poly(y_raw, 24000 // g, sr // g).astype(np.float32)
            y_dsp = apply_light_helmet_dsp_24k(y_raw, click24)
            dur = len(y_dsp) / 24000.0
            tmp_path = BV.DEFAULT_OUT_ROOT / f"_salute_cand_{persona}_{seed}.wav"
            write_pcm16_mono_wav(tmp_path, y_dsp, sr=24000)
            segs, _ = asr.transcribe(str(tmp_path), language="en")
            tr = " ".join(s.text.strip() for s in segs).strip()
            clean_tr = tr.lower().strip(" .,!?-")
            tmp_path.unlink(missing_ok=True)
            print(f"  [{persona} seed={seed}] dur={dur:.3f}s asr={tr!r}", flush=True)
            if clean_tr in ("stormtrooper", "storm trooper") and 0.58 <= dur <= 0.92:
                # Prefer crisp ~0.74s deadpan cadence
                score = abs(dur - 0.74) + (0.0 if clean_tr == "stormtrooper" else 0.03)
                candidates.append((score, persona, seed, dur, tr, y_dsp))

    if not candidates:
        raise RuntimeError("No candidate matched 'Stormtrooper.' within 0.58..0.92s!")
    candidates.sort(key=lambda c: c[0])
    best_score, best_p, best_seed, best_dur, best_tr, best_y = candidates[0]
    info = write_pcm16_mono_wav(OUT_WAV, best_y, sr=24000)
    if info["bytes"] > 80 * 1024:
        raise RuntimeError(f"WAV size {info['bytes']} exceeds 80 KB ceiling!")

    # Final faster-whisper verification on written file
    segs_final, _ = asr.transcribe(str(OUT_WAV), language="en")
    tr_final = " ".join(s.text.strip() for s in segs_final).strip()
    import hashlib
    sha = hashlib.sha256(OUT_WAV.read_bytes()).hexdigest()
    result = {
        **info,
        "sha256": sha,
        "persona": best_p,
        "seed": best_seed,
        "asr_transcript": tr_final,
    }
    (BV.DEFAULT_OUT_ROOT / "salute_wav_r55_summary.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print("SELECTED SALUTE WAV:", json.dumps(result, indent=2), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
