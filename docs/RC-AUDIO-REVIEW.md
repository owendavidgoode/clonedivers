# Local RC audio review — September 22, 2026

The user approved an automated review without asking them to listen to clips.
The first pass processed all **3,988 distinct Delta recordings**, **94.14 minutes**
of audio, locally on the RTX 5090. Paid API cost is **$0**; no audio was uploaded.
This is model-assisted audio review, not a claim of human listening or infallible
transcription. No game files, release assets, voice mappings or volume levels changed.

## Method and evidence

1. Read the 4,035-entry RC candidate catalog, deduplicate by verified file hash,
   and retain aliases and original subtitle references. The prior PCM audit also
   found 3,988 distinct payloads.
2. Run [faster-whisper](https://github.com/SYSTRAN/faster-whisper) large-v3 in FP16
   on the GPU. Each WAV is an independently bounded batch input, with its own
   transcript. No subtitle or expected text is supplied to recognition.
3. Compare recognized words to the original subtitle alternatives. Record raw
   recognizer scores, word similarity, duration, RMS, peak level, source hashes,
   and every current Helldivers mapping that uses the clip.
4. Independently describe all shipped clips and clips with recognition/reference
   issues using [Qwen2-Audio](https://huggingface.co/Qwen/Qwen2-Audio-7B-Instruct).
   This model receives the audio without the first transcript, character identity,
   or subtitles. Its captions are advisory: the pilot misidentified some distorted
   speech and grunts, including calling a vocal grunt a dog. Never promote a clip
   or override source subtitles solely because this model sounds confident.

First-pass results: `dist/rc-audio-review-2026-09-22/` contains `clips.jsonl`,
`review.csv`, `summary.json`, `run.json` and an environment inventory.
Whisper snapshot: `edaa852ec7e145841d8ffdb056a99866b5f0a478`.
The run records the model, input hashes, core package versions and decoding settings.
The first pass took about five minutes after setup (32-clip pilot plus resumed run).

Second-pass results: `dist/rc-audio-second-pass-2026-09-22/`.
Its `summary.json` confirms all **1,249** selected distinct clips completed,
including all 322 shipped recordings, in about 5.5 minutes after loading. Qwen snapshot:
`0a095220c30b7b31434169c3086508ef3ea5bf0a`.
**Reject Qwen captions as an acceptance gate on these assets.** A separate single-clip
calibration reproduced its false description of Boss's "Cancel that, commando"
as dog barking, ruling out the batch grouping as the sole cause. It also corrupted
"bacta" in a known Sev line. Captions remain experimental evidence, not approved
transcripts, character verification, or reliable delivery/loudness judgments.
Calibration is saved in `dist/rc-audio-second-calibration-2026-09-22/`.

The separate `second-pilot` directory is an abandoned structured-output experiment,
not the accepted caption pass. Both models and their dependencies are local caches
under `dist/`; players do not download them.

## Findings

| First-pass result | Count |
| --- | ---: |
| All unique recordings processed | 3,988 |
| Exact normalized subtitle matches | 2,665 |
| Close matches, including exact matches | 3,003 |
| Subtitle disagreement flags | 640 |
| No subtitle reference | 345 |
| Low-confidence/repetition/no-speech flags | 200 |

These categories overlap. Word similarity >=0.8 is a triage heuristic, not a
calibrated correctness probability. Recognizer scores do not prove intelligibility.
Signal RMS is not perceptual loudness; the separate September 21 LUFS audit remains
the reference for source-level loudness.

Of the 322 shipped recordings, 250 closely match subtitles (247 exactly), 25
have subtitle disagreements, and 47 have no subtitle reference. All **47** of
those unsubtitled shipped clips trace to the original `HurtSmallArms`,
`HurtLargeArms` or `HurtMelee` selectors and are mapped to injury events. They are
intentional vocalizations, not 47 missing dialogue transcriptions. Neither ASR's
invented words nor an audio caption should turn them into dialogue candidates.

Many discrepancies are expected ASR limitations: bacta becomes "back to" or
"better", short shouted words are misheard, and numeric names differ in spelling.
Preserve both the source subtitle and recognizer result; do not rewrite the source
text to match an uncertain model output.

One concrete content issue merits a replacement in the next voice candidate:
Scorch `D62MK605.wav` says **"You got it, three-eight."** Both subtitle and
recognition agree that it addresses Boss specifically, so it is not an ideal
generic acknowledgment in mixed squads. The shipped asset remains unchanged.

Examples of unused Boss clips with matching source subtitles and first-pass
recognition, suitable for subsequent event-mapping review:

| Source | Words | Duration |
| --- | --- | ---: |
| `mp_voice/D38ZZ036.wav` | Taking fire! | 0.98 s |
| `character_voice/D38X5032.wav` | Get clear! | 0.71 s |
| `character_voice/D38X5014.wav` | Deltas, take cover! | 1.16 s |
| `character_voice/D38XX145.wav` | Cover fire! | 0.81 s |
| `character_voice/D38MK024.wav` | Let's move on. | 0.80 s |
| `yyy_voice/D38Y6500.wav` | Tank destroyed. | 0.76 s |

The six candidates, 47 injury classifications and the Scorch issue are saved in
`dist/rc-audio-review-2026-09-22/editorial-decisions.json` with source identities
and affected game entries. A completion receipt checks both passes cover their
exact intended sets and include every shipped recording.

These are candidates, not approved event replacements. For example, "Tank destroyed"
requires a confirmed tank-kill event; it must not become a generic kill acknowledgment.
Retain fallback audio where the event or source meaning is uncertain. Do not require
the user to review the catalog manually to make progress on well-supported matches.

## Expansion follow-up — September 23

The six examples above have been followed by a full-catalog mapping pass and a
built staged patch. The [expansion report](RC-VOICE-EXPANSION.md) records 228 newly
used recordings, 54 retired choices and 496 total distinct recordings across all
four commandos. It also corrects the named-Boss response in the staged map. The
shipped pack remains unchanged pending in-game validation and the broader update.

## Re-running

Tools: `tools/review-rc-audio.py` and `tools/review-rc-audio-second-pass.py`.
Both accept `--model`, `--out`, optional `--limit`, and `--batch-size`; the second
also takes `--review` pointing to first-pass `clips.jsonl`. They resume completed
rows only when their input/run configuration matches, and reject changed audio.

Use `dist/rc-upgrade/uv/uv.exe run --offline --no-project --python
dist/audio-review-venv/Scripts/python.exe python <tool> ...` with `UV_CACHE_DIR`
pointing to `dist/rc-upgrade/uv-cache`. For Whisper on Windows, prepend the existing
Python 3.13 `torch/lib` and CUDA 12.8 `bin` directories to the process PATH so
CTranslate2 can load CUDA/cuDNN. No machine-wide PATH or package changes are needed.

The isolated environment uses the existing CUDA-enabled system PyTorch
`2.8.0.dev20250406+cu128`. Do not install a CPU-only torch wheel over that environment.
Transformer setup changed only the isolated environment; the first Whisper process
retained its already-loaded tokenizer. Core ASR versions are in `run.json`; the
setup used tokenizers 0.23.2, while the finished shared environment uses 0.22.2 for
Transformers 4.57.6. Use a separate output for strict reproduction after such changes.
