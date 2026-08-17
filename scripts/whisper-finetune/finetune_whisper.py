"""
Fine-tune openai/whisper-large-v3-turbo with LoRA on synthetic technical-domain audio.

Strategy
--------
- LoRA adapters on the DECODER (4 layers in turbo) — vocabulary / language modelling
- Encoder kept FROZEN. Pre-compute all encoder outputs ONCE and cache to disk.
  This eliminates the 32-layer encoder (5.2s/sample on CPU) from every training step.
  With cached outputs, the decoder-only training loop is ~3s per batch (10× faster).
- Subset sampling: greedily select SUBSET records that maximise vocabulary coverage,
  then fill with random records. This allows good coverage with fewer samples.
- CPU training throughout (DirectML causes TDR for Whisper encoder inference).

Timings (SUBSET=800, CPU)
--------------------------
  Phase 1 — encoder precompute: ~70 min (5.2s × 800 samples, one-time)
  Phase 2 — decoder LoRA training: ~14 min (3 epochs × 90 batches × 3s)
  Merge + spot-check: ~5 min
  Total: ~90 min

Output
------
Merged HF model at  models/whisper-large-v3-turbo-tech/
Drop-in replacement for whisper_api_server.py (set MODEL_ID to the path).
"""

import gc
import json
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import soundfile as sf
import torch
from torch.utils.data import Dataset, DataLoader
from transformers.modeling_outputs import BaseModelOutput

SCRIPT_DIR  = Path(__file__).parent
REPO_ROOT   = SCRIPT_DIR.parent.parent
METADATA    = SCRIPT_DIR / "training_data" / "metadata.jsonl"
ENC_CACHE   = SCRIPT_DIR / "training_data" / "encoder_cache"
MODEL_OUT   = REPO_ROOT / "models" / "whisper-large-v3-turbo-tech"
BASE_MODEL  = "openai/whisper-large-v3-turbo"
VOCAB_FILE  = SCRIPT_DIR / "vocab.json"

# ── Tuning knobs ──────────────────────────────────────────────────────────────
SUBSET      = 800   # records to train on (greedily covers vocabulary terms first)
LORA_R      = 8
LORA_ALPHA  = 16
LR          = 1e-4
EPOCHS      = 3
BATCH       = 8     # larger batch safe — encoder outputs are pre-cached
GRAD_ACCUM  = 4
VAL_SPLIT   = 0.1
MAX_AUDIO_S = 29.0
SEED        = 42


# ── Smart subset selection ────────────────────────────────────────────────────

def select_subset(records: list[dict], vocab_file: Path, n: int, seed=SEED) -> list[dict]:
    """
    Greedy set-cover: iteratively pick records that contain the most uncovered
    vocabulary terms, then fill remaining slots with random records.
    """
    # Load vocabulary terms
    try:
        vocab_data = json.loads(vocab_file.read_text(encoding="utf-8"))
        all_terms  = [t.lower() for terms in vocab_data.values() for t in terms]
    except Exception:
        all_terms = []

    if not all_terms or n >= len(records):
        rng = np.random.default_rng(seed)
        idx = rng.permutation(len(records))[:n]
        return [records[i] for i in idx]

    # Build term → record index mapping
    term_to_recs: dict[str, list[int]] = {t: [] for t in all_terms}
    for i, rec in enumerate(records):
        txt = rec["transcript"].lower()
        for t in all_terms:
            if t in txt:
                term_to_recs[t].append(i)

    # Greedy set cover
    covered   = set()
    selected  = []
    remaining = set(range(len(records)))
    uncovered = set(all_terms)

    while len(selected) < n and uncovered:
        best_idx, best_gain = -1, -1
        # Score each remaining record by uncovered terms it mentions
        scores = {}
        for t in uncovered:
            for i in term_to_recs[t]:
                if i in remaining:
                    scores[i] = scores.get(i, 0) + 1
        if not scores:
            break
        best_idx = max(scores, key=lambda i: scores[i])
        selected.append(records[best_idx])
        remaining.discard(best_idx)
        txt = records[best_idx]["transcript"].lower()
        for t in list(uncovered):
            if t in txt:
                uncovered.discard(t)

    # Fill remaining slots randomly
    if len(selected) < n:
        rng  = np.random.default_rng(seed)
        rest = [records[i] for i in sorted(remaining)]
        rng.shuffle(rest)
        selected.extend(rest[:n - len(selected)])

    return selected[:n]


# ── Encoder precomputation ────────────────────────────────────────────────────

def precompute_encoder_outputs(records: list[dict], processor, cache_dir: Path):
    """
    Run each audio sample through the frozen encoder ONCE and save to disk.
    Uses CPU only — DirectML causes silent TDR on Whisper's 32-layer encoder.
    Cache is keyed by audio filename stem so it survives re-runs.
    """
    from transformers import WhisperForConditionalGeneration

    cache_dir.mkdir(parents=True, exist_ok=True)
    to_do = [r for r in records
             if not (cache_dir / f"{Path(r['audio_path']).stem}.pt").exists()]

    if not to_do:
        print(f"  All {len(records)} encoder outputs already cached.")
        return

    n_cached = len(records) - len(to_do)
    print(f"  Encoding {len(to_do)} samples ({n_cached} already cached)...")
    eta_min  = len(to_do) * 5.2 / 60
    print(f"  Estimated time: {eta_min:.0f} min  ({len(to_do)} × ~5.2s on CPU)", flush=True)

    t0   = time.time()
    base = WhisperForConditionalGeneration.from_pretrained(BASE_MODEL, dtype=torch.float32)
    encoder = base.model.encoder
    base = None; gc.collect()
    encoder.eval()
    print(f"  Encoder loaded ({time.time()-t0:.1f}s)")

    with torch.inference_mode():
        for i, rec in enumerate(to_do):
            stem       = Path(rec["audio_path"]).stem
            cache_file = cache_dir / f"{stem}.pt"

            audio, sr = sf.read(rec["audio_path"], dtype="float32")
            if audio.ndim > 1:
                audio = audio[:, 0]
            audio = audio[:int(MAX_AUDIO_S * sr)]

            feats = processor(audio, sampling_rate=sr, return_tensors="pt").input_features
            out   = encoder(feats)
            # Save fp16 to halve disk usage: (1, 1500, 1280) = 3.8 MB per file
            torch.save(out.last_hidden_state.half().cpu(), cache_file)

            if (i + 1) % 50 == 0 or (i + 1) == len(to_do):
                elapsed  = time.time() - t0
                rate     = (i + 1) / elapsed
                remaining = (len(to_do) - i - 1) / max(rate, 1e-6)
                print(f"  {i+1}/{len(to_do)}  {rate:.2f}/s  ETA {remaining/60:.1f}min",
                      flush=True)

    del encoder; gc.collect()
    print(f"  Precomputation done in {(time.time()-t0)/60:.1f}min", flush=True)


# ── Dataset ───────────────────────────────────────────────────────────────────

class CachedEncoderDataset(Dataset):
    def __init__(self, records: list[dict], cache_dir: Path, processor):
        self.records   = records
        self.cache_dir = cache_dir
        prompt_pairs   = processor.get_decoder_prompt_ids(
            language="english", task="transcribe"
        )
        self._prefix_ids = [tok_id for _, tok_id in prompt_pairs]
        self._eot_id     = processor.tokenizer.eos_token_id
        self._tokenizer  = processor.tokenizer

    def __len__(self):
        return len(self.records)

    def __getitem__(self, idx):
        rec    = self.records[idx]
        stem   = Path(rec["audio_path"]).stem
        enc_hs = torch.load(
            self.cache_dir / f"{stem}.pt", weights_only=False
        ).float()  # fp16 on disk → fp32 for training; (1, 1500, 1280)

        text_ids  = self._tokenizer(rec["transcript"], add_special_tokens=False).input_ids
        label_ids = self._prefix_ids + text_ids + [self._eot_id]
        return {
            "encoder_hidden_states": enc_hs,
            "labels": torch.tensor(label_ids, dtype=torch.long),
        }


@dataclass
class DataCollator:
    pad_token_id: int = -100

    def __call__(self, features):
        enc_hs     = torch.cat([f["encoder_hidden_states"] for f in features], dim=0)
        label_list = [f["labels"] for f in features]
        max_len    = max(l.shape[0] for l in label_list)
        padded     = []
        for lab in label_list:
            pad = torch.full((max_len - lab.shape[0],), self.pad_token_id, dtype=torch.long)
            padded.append(torch.cat([lab, pad]))
        return {
            "encoder_hidden_states": enc_hs,
            "labels": torch.stack(padded),
        }


# ── Helpers ───────────────────────────────────────────────────────────────────

def load_records(metadata_path: Path) -> list[dict]:
    records = []
    for line in metadata_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line:
            rec = json.loads(line)
            if Path(rec["audio_path"]).exists():
                records.append(rec)
    return records


def split_records(records, val_frac=VAL_SPLIT, seed=SEED):
    rng       = np.random.default_rng(seed)
    idx       = rng.permutation(len(records))
    n_val     = max(1, int(len(records) * val_frac))
    return [records[i] for i in idx[n_val:]], [records[i] for i in idx[:n_val]]


def _forward(model, enc_hs, labels):
    # PEFT's PeftModelForSeq2SeqLM.forward hardcodes 'input_ids' — call the
    # LoRA-patched Whisper model directly with encoder_outputs to bypass it.
    return model.base_model.model(
        encoder_outputs=BaseModelOutput(last_hidden_state=enc_hs),
        labels=labels,
    )


def compute_val_loss(model, val_loader, device):
    model.eval()
    total_loss, steps = 0.0, 0
    with torch.no_grad():
        for batch in val_loader:
            enc_hs = batch["encoder_hidden_states"].to(device)
            labels = batch["labels"].to(device)
            total_loss += _forward(model, enc_hs, labels).loss.item()
            steps += 1
    model.train()
    return total_loss / max(steps, 1)


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    print("=" * 60)
    print("  Whisper large-v3-turbo  ×  LoRA fine-tune")
    print("  Domain: cloud / Azure / AWS / Databricks / LLMOps")
    print("          ML / Python / Kubernetes / Kafka")
    print(f"  Subset: {SUBSET} records  |  {EPOCHS} epochs  |  batch={BATCH}")
    print("=" * 60)

    try:
        from peft import get_peft_model, LoraConfig, TaskType
        from transformers import WhisperForConditionalGeneration, WhisperProcessor
    except ImportError as e:
        print(f"Missing dependency: {e}")
        sys.exit(1)

    if not METADATA.exists():
        print(f"Training data not found at {METADATA}"); sys.exit(1)

    # ── Load + sample records ─────────────────────────────────────────────────
    all_records = load_records(METADATA)
    print(f"\nPool: {len(all_records)} total records in metadata")

    subset = select_subset(all_records, VOCAB_FILE, SUBSET, seed=SEED)
    print(f"Selected {len(subset)} records via greedy vocabulary coverage")

    train_recs, val_recs = split_records(subset)
    print(f"Split: {len(train_recs)} train / {len(val_recs)} val")

    print(f"\nLoading processor...")
    processor = WhisperProcessor.from_pretrained(BASE_MODEL)

    # ── Phase 1: Precompute encoder outputs (one-time, CPU) ───────────────────
    print(f"\n[Phase 1] Encoder precomputation → {ENC_CACHE}")
    precompute_encoder_outputs(subset, processor, ENC_CACHE)

    # ── Phase 2: Load PEFT model for decoder-only training ────────────────────
    print(f"\n[Phase 2] Loading PEFT model for decoder training (CPU)...")
    t0    = time.time()
    model = WhisperForConditionalGeneration.from_pretrained(BASE_MODEL, dtype=torch.float32)
    model.config.forced_decoder_ids            = None
    model.config.suppress_tokens               = []
    model.generation_config.forced_decoder_ids = None
    print(f"  Base model: {sum(p.numel() for p in model.parameters())/1e6:.0f}M params "
          f"({time.time()-t0:.1f}s)")

    lora_cfg = LoraConfig(
        task_type=TaskType.SEQ_2_SEQ_LM,
        r=LORA_R, lora_alpha=LORA_ALPHA,
        target_modules=["q_proj", "v_proj"],
        lora_dropout=0.05, bias="none", modules_to_save=[],
    )
    model = get_peft_model(model, lora_cfg)
    trainable, total = model.get_nb_trainable_parameters()
    print(f"  LoRA: {trainable/1e6:.2f}M trainable / {total/1e6:.0f}M total "
          f"({100*trainable/total:.2f}%)")

    for name, param in model.named_parameters():
        if ".encoder." in name:
            param.requires_grad_(False)

    device = torch.device("cpu")
    model  = model.to(device)
    model.train()

    # ── DataLoaders ───────────────────────────────────────────────────────────
    collator = DataCollator(pad_token_id=processor.tokenizer.pad_token_id or -100)
    train_ds = CachedEncoderDataset(train_recs, ENC_CACHE, processor)
    val_ds   = CachedEncoderDataset(val_recs,   ENC_CACHE, processor)
    train_dl = DataLoader(train_ds, batch_size=BATCH, shuffle=True,
                          collate_fn=collator, num_workers=0)
    val_dl   = DataLoader(val_ds,   batch_size=BATCH, shuffle=False,
                          collate_fn=collator, num_workers=0)

    # ── Optimizer + LR schedule ───────────────────────────────────────────────
    optimizer    = torch.optim.AdamW(
        [p for p in model.parameters() if p.requires_grad], lr=LR, weight_decay=0.01
    )
    total_steps  = (len(train_dl) * EPOCHS) // GRAD_ACCUM
    warmup_steps = max(1, total_steps // 10)

    def lr_lambda(step):
        if step < warmup_steps:
            return step / warmup_steps
        prog = (step - warmup_steps) / max(total_steps - warmup_steps, 1)
        return max(0.1, 1.0 - prog)

    scheduler = torch.optim.lr_scheduler.LambdaLR(optimizer, lr_lambda)

    print(f"\nDecoder training config:")
    print(f"  {len(train_dl)} batches/epoch × {EPOCHS} epochs = {total_steps} optimizer steps")
    est_sec = len(train_dl) * EPOCHS * 3   # ~3s per batch for decoder-only
    print(f"  Estimated training time: {est_sec/60:.0f} min", flush=True)

    # ── Training loop ─────────────────────────────────────────────────────────
    best_val_loss = float("inf")
    best_ckpt     = SCRIPT_DIR / "training_data" / "best_checkpoint"
    global_step   = 0
    t_start       = time.time()

    for epoch in range(1, EPOCHS + 1):
        ep_loss, ep_steps = 0.0, 0
        t_ep = time.time()
        optimizer.zero_grad()

        for step, batch in enumerate(train_dl, 1):
            enc_hs = batch["encoder_hidden_states"].to(device)
            labels = batch["labels"].to(device)

            out  = _forward(model, enc_hs, labels)
            loss = out.loss / GRAD_ACCUM
            loss.backward()

            ep_loss  += out.loss.item()
            ep_steps += 1

            if step % GRAD_ACCUM == 0 or step == len(train_dl):
                torch.nn.utils.clip_grad_norm_(
                    [p for p in model.parameters() if p.requires_grad], 1.0
                )
                optimizer.step()
                scheduler.step()
                optimizer.zero_grad()
                global_step += 1

                if global_step % 10 == 0:
                    elapsed = time.time() - t_ep
                    rate    = step / max(elapsed, 1)
                    eta     = (len(train_dl) - step) / max(rate, 1e-6)
                    print(
                        f"  E{epoch} step {step}/{len(train_dl)}"
                        f"  loss={ep_loss/ep_steps:.4f}"
                        f"  lr={scheduler.get_last_lr()[0]:.2e}"
                        f"  ETA {eta/60:.1f}min",
                        flush=True,
                    )

        avg_train  = ep_loss / max(ep_steps, 1)
        val_loss   = compute_val_loss(model, val_dl, device)
        epoch_time = time.time() - t_ep
        print(f"\nEpoch {epoch}/{EPOCHS}  train={avg_train:.4f}  val={val_loss:.4f}  "
              f"time={epoch_time/60:.1f}min", flush=True)

        if val_loss < best_val_loss:
            best_val_loss = val_loss
            print(f"  → New best — saving checkpoint...", flush=True)
            model.save_pretrained(str(best_ckpt))
            processor.save_pretrained(str(best_ckpt))

    print(f"\nTraining complete in {(time.time()-t_start)/60:.1f}min  "
          f"best_val={best_val_loss:.4f}", flush=True)

    # ── Merge LoRA + save ─────────────────────────────────────────────────────
    print("\nMerging LoRA adapters...")
    from peft import PeftModel
    base2  = WhisperForConditionalGeneration.from_pretrained(BASE_MODEL, dtype=torch.float32)
    merged = PeftModel.from_pretrained(base2, str(best_ckpt)).merge_and_unload()
    merged.config.forced_decoder_ids            = None
    merged.config.suppress_tokens               = []
    merged.generation_config.forced_decoder_ids = None
    MODEL_OUT.mkdir(parents=True, exist_ok=True)
    merged.save_pretrained(str(MODEL_OUT))
    processor.save_pretrained(str(MODEL_OUT))
    print(f"Saved → {MODEL_OUT}")

    # ── Spot-check ────────────────────────────────────────────────────────────
    print("\n--- Vocabulary spot-check ---")
    merged.eval()
    for term in ["Kubernetes", "Apache Kafka", "LangChain", "Delta Lake", "FastAPI"]:
        sample = next((r for r in val_recs if term.lower() in r["transcript"].lower()), None)
        if sample is None:
            continue
        audio, sr = sf.read(sample["audio_path"], dtype="float32")
        if audio.ndim > 1:
            audio = audio[:, 0]
        feats = processor(audio, sampling_rate=sr, return_tensors="pt").input_features
        with torch.no_grad():
            ids = merged.generate(feats)
        pred  = processor.batch_decode(ids, skip_special_tokens=True)[0].strip()
        match = "✓" if term.lower() in pred.lower() else "✗"
        print(f"  {match} [{term}]  '{pred[:100]}'")

    print(f"\nDone. Model at: {MODEL_OUT}")
    print(f'In whisper_api_server.py:  MODEL_ID = r"{MODEL_OUT}"')


if __name__ == "__main__":
    main()
