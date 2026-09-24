#!/usr/bin/env -S uv run
"""Independently describe flagged RC audio and shipped clips using a local audio LLM."""

import argparse
import hashlib
import importlib.metadata
import json
import logging
import os
from pathlib import Path
import sys
import time

LOG = logging.getLogger(__name__)
PROMPT = (
    'What can you hear in this audio? If there is intelligible speech, quote the words, '
    'then briefly describe the vocal delivery and any background noise. If it is only '
    'a grunt, breath or other vocalization, describe that sound without guessing words. '
    'Answer in English in at most two sentences. Do not infer the speaker identity or a story.'
)


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--review", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--limit", type=int)
    parser.add_argument("--batch-size", type=int, default=8)
    args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format="%(message)s")
    try:
        if args.batch_size < 1 or (args.limit is not None and args.limit < 1):
            raise ValueError("Positive batch size and limit required")
        os.environ["HF_HUB_OFFLINE"] = "1"
        os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
        import torch
        from faster_whisper.audio import decode_audio
        from transformers import AutoProcessor, Qwen2AudioForConditionalGeneration

        if not torch.cuda.is_available():
            raise RuntimeError("Expected working CUDA runtime")
        all_rows = [json.loads(line) for line in args.review.read_text(encoding="utf-8").splitlines()]
        wanted_flags = {"subtitle_disagreement", "no_subtitle_reference", "uncertain_recognition", "no_speech_recognized"}
        selected = [r for r in all_rows if r["shipped_targets"] or wanted_flags.intersection(r["flags"])]
        selected.sort(key=lambda r: (not bool(r["shipped_targets"]), not bool(wanted_flags.intersection(r["flags"])), r["sha256"]))
        args.out.mkdir(parents=True, exist_ok=True)
        config = {"review_sha256": digest(args.review), "model_snapshot": str(args.model.resolve()),
                  "prompt": PROMPT, "max_new_tokens": 128, "do_sample": False,
                  "selected_count": len(selected), "batch_size": args.batch_size,
                  "packages": {p: importlib.metadata.version(p) for p in ["torch", "transformers", "accelerate"]}}
        config_path = args.out / "run.json"
        if config_path.exists() and json.loads(config_path.read_text()) != config:
            raise ValueError("Resume inputs differ; use a fresh directory")
        config_path.write_text(json.dumps(config, indent=2) + "\n", encoding="utf-8")
        journal = args.out / "clips.jsonl"
        reviewed = [json.loads(line) for line in journal.read_text(encoding="utf-8").splitlines()] if journal.exists() else []
        done = {r["sha256"] for r in reviewed}
        if len(done) != len(reviewed) or not done <= {r["sha256"] for r in selected}:
            raise ValueError("Invalid resume journal")
        pending = [r for r in selected if r["sha256"] not in done]
        if args.limit:
            pending = pending[:args.limit]
        processor = AutoProcessor.from_pretrained(args.model, local_files_only=True, trust_remote_code=False)
        processor.tokenizer.padding_side = "left"
        model = Qwen2AudioForConditionalGeneration.from_pretrained(
            args.model, torch_dtype=torch.bfloat16, device_map="cuda", attn_implementation="sdpa",
            local_files_only=True, trust_remote_code=False,
        ).eval()
        started = time.monotonic()
        with journal.open("a", encoding="utf-8") as handle:
            for offset in range(0, len(pending), args.batch_size):
                batch = pending[offset:offset + args.batch_size]
                texts, waves = [], []
                for row in batch:
                    path = Path(row["file"])
                    if digest(path) != row["sha256"]:
                        raise ValueError(f"Changed source: {path}")
                    conversation = [{"role": "user", "content": [
                        {"type": "audio", "audio_url": "local-audio.wav"},
                        {"type": "text", "text": PROMPT},
                    ]}]
                    texts.append(processor.apply_chat_template(conversation, add_generation_prompt=True, tokenize=False))
                    waves.append(decode_audio(str(path), sampling_rate=processor.feature_extractor.sampling_rate))
                inputs = processor(text=texts, audio=waves, sampling_rate=processor.feature_extractor.sampling_rate,
                                   return_tensors="pt", padding=True)
                inputs = inputs.to("cuda")
                if "input_features" in inputs:
                    inputs["input_features"] = inputs["input_features"].to(torch.bfloat16)
                with torch.inference_mode():
                    tokens = model.generate(**inputs, max_new_tokens=128, do_sample=False)
                outputs = processor.batch_decode(tokens[:, inputs.input_ids.shape[1]:], skip_special_tokens=True)
                for row, response in zip(batch, outputs, strict=True):
                    result = {"sha256": row["sha256"], "character": row["character"],
                              "raw_response": response,
                              "shipped": bool(row["shipped_targets"]),
                              "status": "Independent model opinion; neither subtitles nor first-pass text were provided."}
                    handle.write(json.dumps(result, ensure_ascii=False) + "\n")
                    reviewed.append(result)
                handle.flush()
                summary = {"reviewed": len(reviewed), "selected": len(selected),
                           "complete": len(reviewed) == len(selected),
                           "nonempty_responses": sum(bool(r["raw_response"].strip()) for r in reviewed),
                           "paid_api_cost_usd": 0, "audio_uploaded": False}
                (args.out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
                LOG.info("Second pass %d/%d clips (%.1f seconds)", len(reviewed), len(selected), time.monotonic() - started)
        return 0
    except KeyboardInterrupt:
        return 130
    except Exception:
        LOG.exception("Audio second pass failed; completed journal rows retained")
        return 1


if __name__ == "__main__":
    sys.exit(main())
