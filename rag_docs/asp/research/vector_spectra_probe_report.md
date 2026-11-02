# Benchmark Report — vector_spectra_probe

**Date:** 2026-08-11
**Branch:** experiment/unstructured-rag
**Run tag:** vector_spectra_probe
**Models:** Embedding = BAAI/bge-base-en-v1.5 (768-dim, local, $0/query). Facets = Family A/B Nemotron (1 LLM call/query, disk-cached → $0 reruns).
**Datasets:** ArguAna (k=10), NFCorpus (k=100), FiQA (k=10) — all measured on bge-base, n=50.
**Backends:** SPECTRA + flat_dense floor (same-condition, same process).
**n per dataset:** 50
**Experiment:** SPECTRA — raise recall by CHANGING CANDIDATE GENERATION (LLM-facet subspace + off-axis
full-corpus scan), not reranking a fixed dense pool; scored by a fused scale-invariant additive form.
**Outcome: FALSIFIED.** SPECTRA loses to flat_dense at FiQA's k=10 primary operating point;
the HIGH-baseline wins are carried by known techniques (hub removal + bigger pool), not the
novel facet machinery. See §6.

---

## 0. Method (SPECTRA v2)

One fixed parameter set, all datasets: **r=8 facets, N=100, M=100, λ=0.5, μ=0.5.**

1. **Hub removal** ĥ = corpus-mean direction; Π(x)=x−(x·ĥ)ĥ (anisotropy correction), cached per corpus.
2. **Facet subspace** — one LLM call writes r=8 short hypothetical passages (disk-cached by sha1(query) →
   $0 on reruns; degrades to $0 clause-split proxy on any failure). A=[Π(q);Π(f₁..f_r)], Gram–Schmidt →
   V=[v₀..v_ρ]: v₀∝Π(q) anchor axis, v₁..v_ρ off-axis facet directions (QR collapses near-duplicate facets).
3. **Three scale-invariant fields** (hub-removed unit docs d'=Π(d)/‖Π(d)‖):
   raw = q·d (plain cosine = flat floor) · hub anchor n = v₀·d' · facet coverage c = maxⱼ|vⱼ·d'| (MaxSim-style).
4. **Candidate generation (the recall lever):** Pool_raw = top max(N,k) of raw ∪ Pool_excess = top max(M,k)
   of Σⱼ(vⱼ·d')². Pools scale with k — you cannot rank a doc you never generated.
5. **Fused additive score:** f = raw + λ·c + μ·n. Anchoring on raw cosine floors it at flat_dense; c and n
   only add signal (no per-dataset crutch).

---

## 1. Headline scorecard

| Dataset | Method | ASP R@k | Flat R@k | Δ R | ASP nDCG@10 | Flat nDCG@10 | Δ nDCG | Verdict |
|---------|--------|---------|----------|-----|-------------|--------------|--------|---------|
| ArguAna (k=10) | SPECTRA | **88.00** | 80.00 | **+8.00** | 42.28 | 39.50 | +2.78 | ✅ |
| NFCorpus (k=100) | SPECTRA | **32.94** | 31.33 | **+1.61** | 41.16 | 38.22 | +2.94 | ✅ |
| FiQA (k=10) | SPECTRA | 48.13 | 48.70 | **−0.57** | 39.07 | 40.59 | −1.52 | ❌ |

Verdict key: ✅ ASP beats flat | ⚠️ within 1 pp | ❌ ASP trails flat by >1 pp.
FiQA (R@1 26.37→21.70, MRR 46.65→43.92) — SPECTRA loses on every metric at the primary
operating point.

⚠️ **The headline claim is FALSIFIED — see §6/§8.** With FiQA now measured (2026-08-11), the
verdict is settled: **SPECTRA is NOT the E16 champion.** (1) FiQA — the chartered LOW-baseline
PRIMARY that gates the whole win-on-both bar — SPECTRA loses at the standard k=10 operating
point (R@10 −0.57, nDCG@10 −1.52); the loss is a statistical wash (McNemar p=1.0000, bootstrap
95% CI [−6.13, +3.83] crosses 0). The E16-S11 falsification condition (FiQA < floor +1.5pt)
fires outright. (2) The two HIGH-baseline wins (ArguAna, NFCorpus) are carried by hub removal
(a KNOWN technique) + a bigger rerank pool, not the novel facet machinery. (3) NFCorpus R@100
is launch-top_k-dependent; 32.94 is the apples-to-apples value.

---

## 2. k-sweep (recall@k, one fixed param set)

**ArguAna** (1 relevant/query — recall@k = is-the-answer-in-top-k):

| k | flat | SPECTRA | Δ |
|---|------|---------|---|
| 5 | 64.00 | 64.00 | 0 |
| 10 | 80.00 | **88.00** | +8.00 |
| 20 | 94.00 | 94.00 | 0 (★ 90%+ reached) |
| 50 | 98.00 | 98.00 | 0 |
| 100 | 100.00 | 100.00 | 0 |

**NFCorpus** (50–100 relevant/query — recall structurally capped at low k):

| k | flat | SPECTRA | Δ |
|---|------|---------|---|
| 50 | 26.12 | **28.46** | +2.34 |
| 100 | 31.33 | **32.94** | +1.61 |
| 200 | 37.47 | **38.20** | +0.73 |
| 500 | 50.72 | 48.87 | −1.85 |
| 1000 | 68.35 | 68.30 | −0.05 |

Each row is launched at that k (launch top_k = report k), so SPECTRA's pool = max(N, k) and
the numbers are the apples-to-apples operating points. SPECTRA wins through k=200, **regresses
−1.85pp at k=500**, ties at k=1000. The k=500 regression is a direct counterexample to any
"floored by construction" claim (see §3). NFCorpus's standard reported metric is R@100 +
nDCG@10; k=500 sits outside the standard report but is disclosed here in full, not hidden.

**FiQA** (1–3 relevant/query — the chartered LOW-baseline PRIMARY; standard metric = R@10/nDCG@10):

| k | flat | SPECTRA | Δ |
|---|------|---------|---|
| 10 | 48.70 | 48.13 | **−0.57** (primary operating point — SPECTRA LOSES) |
| 20 | 51.90 | 57.90 | +6.00 |
| 50 | 64.27 | 67.00 | +2.73 |
| 100 | 72.60 | 77.27 | +4.67 |

**This is the decisive, falsifying measurement.** At FiQA's chartered k=10 operating point
SPECTRA loses (R@10 −0.57, nDCG@10 40.59→39.07, R@1 26.37→21.70, MRR 46.65→43.92). The loss is
not statistically distinguishable from zero (McNemar p=1.0000 — only 5 discordant queries,
flat won 3 / SPECTRA won 2; bootstrap 95% CI on the R@10 delta = [−6.13, +3.83]), i.e. a wash
trending negative. SPECTRA *does* win at k≥20 (+6.0/+2.7/+4.7) — but FiQA's standard operating
point is k=10, and leaning on the k≥20 win is exactly the "k as a disguised per-dataset knob"
crutch flagged as a blocker in §8. The non-monotone sign (loses shallow, wins deep) is the
signature of a pool-size effect, not a robust method — mirroring NFCorpus (wins shallow,
regresses at k=500), inverted.

---

## 3. No-regression check — RETRACTED CLAIM

**The earlier "floored at flat_dense by construction" guarantee was FALSE and is retracted.**
The reasoning was: SPECTRA's candidate pool ⊇ flat's top-k, so it cannot drop a doc flat
returned. This is wrong. The pool being a superset does not make the *scoring* rank-safe: the
fused additive score `f = raw + λ·c + μ·n` reorders the union pool, and the off-anchor terms
can demote a gold doc that flat would have ranked in its top-k. **The NFCorpus k=500 row
(−1.85pp, §2) is the direct counterexample** — SPECTRA lands *below* flat there.

What actually holds, empirically (not by construction): at the standard shallow operating
points (ArguAna k=10, NFCorpus k=100) the union-pool recall lift dominates the rescoring
demotions, so SPECTRA ≥ flat there. At deep k the rescoring is not rank-safe and SPECTRA can
regress. No-regression is therefore an *observed, operating-point-dependent* property, not a
mathematical guarantee — and must be re-verified per dataset/k, never assumed.

---

## 4. Falsification record (honest negatives — first-class)

| Method | Result | Root cause |
|--------|--------|-----------|
| COVERT v1/v2 | R@10 = floor (v1); −1.05pp (v2) | OT-plan boost 100× too small; token-cloud OT anti-correlated on medical queries |
| APEX | NFCorpus +0.86, ArguAna 0 | Fixed-pool reranker — cannot recall a doc dense never surfaced (recall-ceiling bound) |
| SPECTRA v1 (a+λ√E) | NFCorpus 30.59 (−0.74 vs flat) | Unnormalized √E rewarded doc magnitude, not facet coverage; hub-removal alone hurts NFCorpus |
| **SPECTRA v2 (fused)** | **wins both** | Scale-invariant additive terms; raw-cosine anchor floors regression |

The v1→v2 fix is the scientific core: decomposing the score into three additive, individually-signed terms
showed hub-removal helps ArguAna (+8) but hurts NFCorpus (−2) when it *replaces* the raw anchor, while the
fused additive form (raw anchor + hub + coverage) wins both. See §4b for the honest term attribution — which
corrects an earlier overclaim about the mechanism.

---

## 4b. Term-attribution ablation (E16-S12) — CORRECTS the mechanism claim

Nested-arm ablation on the cached indices + cached facets ($0), same fixed param set. **This falsifies the
earlier "candidate generation / off-axis scan is the recall lever" narrative** — recorded honestly per the
no-fudge mandate.

| Arm | ArguAna R@10 (Δ vs raw) | NFCorpus R@100 (Δ vs raw) |
|-----|------------------------|---------------------------|
| flat_raw (plain cosine) | 80.00 | 31.33 |
| raw + cover (facets only) | 78.00 (**−2.00**) | 32.02 (+0.69) |
| raw + hub | 82.00 (+2.00) | 32.08 (+0.75) |
| **raw + cover + hub (FULL)** | **88.00 (+8.00)** | **32.94 (+1.61)** |
| hub_only (no raw, no cover) | **90.00 (+10.00)** | 32.00 (+0.67) |
| cover_only | 10.00 (−70) | 24.34 (−7) |
| FULL, raw-pool only (no off-axis expansion) | 88.00 (+8.00) | 31.33 (+0.00) |

**Honest reading:**
1. **ArguAna's +8 is carried by HUB REMOVAL (anisotropy correction), not facets.** `hub_only`=90 actually
   *beats* the full method; the facet-coverage term is a −2pp DRAG here. Off-axis expansion surfaced **0**
   relevant docs not already in the raw pool.
2. **The off-axis candidate expansion contributes almost nothing end-to-end.** A matched-pool-budget test
   (rerank pool = raw top-N only) shows NFCorpus: raw-N100→31.33, raw-N200→32.66, union(N100∪M100)→32.94 —
   so enlarging the pool gives +1.33 and the off-axis *direction* adds only **+0.28** on top. On ArguAna the
   expansion is fully inert (88=88).
3. So the real drivers are **(a) hub removal** (≈ Mu & Viswanath "all-but-the-top" — a KNOWN technique, not
   novel) and **(b) fused scoring over a modestly larger rerank pool.** The LLM-facet subspace + off-axis
   scan — the novel, LLM-cost-incurring, inventive-claim part — is NOT yet earning its keep on these two
   datasets, and hurts one of them.

**Why keep the facet machinery anyway (open, testable question) — NOW ANSWERED on FiQA:**
ArguAna (argument↔counter-argument) and NFCorpus (medical lay↔technical) are not the severe
query↔document *vocabulary-mismatch* regime that hypothetical-passage facets are designed for.
**FiQA (finance forum QA) is** — the canonical low-baseline vocab-mismatch dataset and the
primary beat-target. FiQA is now measured (bge-base, n=50):

| Arm | FiQA R@10 (Δ vs raw) |
|-----|----------------------|
| flat_raw (plain cosine) | 48.70 |
| raw + cover (facets only) | 46.27 (**−2.43**) |
| raw + hub | 48.87 (+0.17) |
| **raw + cover + hub (FULL)** | 48.13 (**−0.57**) |
| hub_only | **49.70 (+1.00)** |
| cover_only | 2.67 (−46) |
| FULL, raw-pool only (no off-axis expansion) | 47.13 (−1.57) |

**The facet machinery does NOT earn its keep even on FiQA at k=10 — the answer is: cut it.**
The cover (facet) term is again the worst contributor (−2.43); `hub_only` is again the best
single arm (+1.00); FULL loses to flat (−0.57). The one new signal: the off-axis excess pool
now surfaces **5.33% of relevant docs unreachable by raw cosine** (vs 0 on ArguAna), and
FULL_no_excess_pool (47.13) < FULL (48.13) shows the pool adds +1.0 — but those off-axis-only
docs do NOT rank into the top-10, so the lift only materialises at k≥20 (+6.0pp), which is not
FiQA's operating point. **Honest conclusion (now settled, not open): SPECTRA reduces to
"anisotropy-corrected fused reranking"; the facet/off-axis apparatus is a net drag at every
dataset's standard operating point and should be cut.**

---

## 5. Key findings

- On the two measured (both HIGH-baseline) datasets, SPECTRA beats the same-condition
  flat_dense floor at the standard operating point: ArguAna +8.00 R@10 (80→88), NFCorpus
  +1.61 R@100 (31.33→32.94), and on nDCG@10 (+2.78, +2.94; NFCorpus MRR +5.64).
- **The win is not from the novel mechanism (from §4b + §8).** It is driven by hub removal (a
  KNOWN anisotropy technique, ≈ Mu & Viswanath all-but-the-top) + a larger rerank pool. The
  LLM-facet off-axis candidate generation is inert on ArguAna (0 new relevant docs) and
  near-inert on NFCorpus (+0.28pp of +1.61). The facet-coverage term is a −2pp DRAG on
  ArguAna. The "candidate generation is the recall lever" claim is withdrawn on these datasets.
- **The charter "wins on both regimes" bar is NOT met — FiQA falsifies it.** FiQA is now
  measured (bge-base, n=50) and at its chartered k=10 operating point SPECTRA LOSES: R@10
  48.70→48.13 (−0.57), nDCG@10 40.59→39.07, R@1 26.37→21.70, MRR 46.65→43.92. The loss is a
  statistical wash (McNemar p=1.0000, bootstrap 95% CI [−6.13, +3.83] crosses 0), but the
  charter requires floor **+1.5pt** — SPECTRA is negative, so the E16-S11 falsification
  condition fires. (The prior APEX FiQA bge-small artifact showed +0.10; the same family does
  not lift the vocab-mismatch primary.)
- **On FiQA the off-axis pool finally shows life but does not convert.** 5.33% of relevant docs
  are reachable only via the excess pool (vs 0 on ArguAna) — the first non-zero evidence the
  facet apparatus recovers docs dense misses. But they don't rank into the top-10; the lift
  only appears at k≥20 (+6.0pp), which is not FiQA's operating point, and relying on it is the
  "k as a per-dataset knob" crutch (§8). The facet-coverage term is again a −2.43 DRAG; hub_only
  (+1.00) is again the best single arm.
- The ArguAna +8.0pp is **4 flipped queries** and the subspace is stochastic (temperature=0.7
  facets → three runs gave 88/84/82). McNemar p≈0.29 — not significant.
- **Settled conclusion:** SPECTRA reduces to "anisotropy-corrected fused reranking" — a claim
  that must clear PCA/whitening + all-but-the-top + HyDE + ColBERT prior art, and that still
  does not beat flat at FiQA's operating point. The facet/off-axis apparatus should be cut.

---

## 6. Decision

**NOT PROMOTED — SPECTRA is FALSIFIED as the E16 vector champion.** With FiQA (the chartered
LOW-baseline PRIMARY) now measured, the win-on-both gate is decisively unmet: SPECTRA loses to
the same-condition flat_dense floor at FiQA's k=10 operating point on every metric (R@10 −0.57,
nDCG@10 −1.52, R@1 −4.67, MRR −2.73), and the E16-S11 falsification condition
("FiQA < floor +1.5pt ⇒ method not promoted") fires outright. This is not a "pending" downgrade
— it is the falsifying measurement the earlier draft was waiting on.

Two things are true and both recorded honestly:
- **The novel mechanism is dead at every standard operating point.** The facet-coverage term is
  a net drag on all three datasets; the off-axis pool contributes 0 (ArguAna), +0.28pp
  (NFCorpus), and +1.0pp-but-only-at-k≥20 (FiQA). What lift SPECTRA has on the two HIGH-baseline
  datasets is carried by hub removal (a KNOWN technique) + a bigger rerank pool.
- **The k≥20 FiQA win is a crutch, not a result.** FiQA's chartered metric is R@10/nDCG@10;
  claiming the k≥20 win is the exact "k as a disguised per-dataset knob" pattern the audit
  flagged as a blocker (§8). The non-monotone sign (loses shallow, wins deep) is the signature
  of a pool-size effect, not a robust method.

**Next experiment (what this falsification points to):** abandon the facet/off-axis apparatus.
The only component that repeatedly helps is hub removal (all-but-the-top). Either (a) ship a
minimal "anisotropy-corrected dense" arm and honestly benchmark it as a known-technique baseline
(no novelty claim), or (b) return to invention for a candidate-generation mechanism that beats
flat at the *primary* operating point on a *low-baseline* dataset — the bar SPECTRA could not
clear. No promotion, paper claim, or invention disclosure stands. Remaining hygiene if any
anisotropy-corrected arm is pursued: held-out validation slice for λ/μ/N/M (currently tuned on
test queries — epic rule 5); multi-seed facet band (temperature=0.7 → 88/84/82 on ArguAna).

No result has been cherry-picked; the k=500 NFCorpus regression and the launch-top_k
dependence of R@100 are disclosed in full.

**Reproduce** (launch top_k = report k so SPECTRA's pool size matches the reported metric):
```
export ASP_VECTOR_EMBED_MODEL="BAAI/bge-base-en-v1.5"
python -m asp.benchmarks.vector_retrieval_ablation --dataset arguana  --limit 50 --top-k 10  --arms flat_dense spectra --model BAAI/bge-base-en-v1.5
python -m asp.benchmarks.vector_retrieval_ablation --dataset nfcorpus --limit 50 --top-k 100 --arms flat_dense spectra --model BAAI/bge-base-en-v1.5
python -m asp.benchmarks.vector_retrieval_ablation --dataset fiqa     --limit 50 --top-k 10  --arms flat_dense spectra --model BAAI/bge-base-en-v1.5
```
Note: facets are temperature=0.7 (stochastic) and cached by sha1(query); the cache pins the
reported run, but a cold cache will draw new facets and R@10 will vary (88/84/82 observed).

---

## 7. Efficiency note

SPECTRA scans the full corpus on ρ off-axis directions (a dense (Nc×d)·(d×(1+ρ)) matmul per
query) plus one cached LLM call. On the measured corpora this is sub-second and $0 on rerun
(facet cache), but it is strictly more compute than flat_dense for a lift that, on these two
datasets, hub-removal + a bigger pool already deliver without the LLM.

---

## 8. Adversarial audit findings (2026-08-11) — accepted in full

An adversarial audit (4 lenses: novelty-vs-prior-art, statistical robustness, comparison
fairness, hidden-crutch) returned **CLAIM_BROKEN** on two lenses and **CLAIM_WEAKENED** on two.
Every load-bearing fact was verified against source. Accepted findings:

**Blockers**
1. **Novelty falsified.** The win is carried by hub removal (all-but-the-top, KNOWN) + a
   larger pool, not the facet/off-axis mechanism. The facet generator IS a HyDE variant
   (r=8 hypothetical passages); the cover term IS ColBERT-style MaxSim over query-side
   vectors. The docstring's HyDE/ColBERT "distinct from" claims were wrong and are corrected.
2. **k is a disguised operating-point knob.** The ArguAna win exists at k=10; NFCorpus
   regresses at k=500. "One fixed parameter set" is honest for {r,N,M,λ,μ} but the reported
   win still lives at a per-dataset k.
3. **"Floored by construction" is mathematically false** (retracted in §3; k=500 counterexample).

**Majors**
4. **Charter win-on-both bar not met — NOW CONFIRMED BY FiQA (resolved 2026-08-11).** When
   this finding was written FiQA was unmeasured; it has since been run (bge-base, n=50) and
   SPECTRA LOSES at the k=10 primary operating point (R@10 −0.57, statistically a wash). The
   falsification condition fires. See §2 (FiQA k-sweep), §4b (FiQA ablation), §6 (decision).
5. **Embedding-swap floor regression** — flat_dense dropped 84→80 (ArguAna R@10) on the
   bge-small→bge-base swap with no ADR, inflating the delta. ADR now filed (docs/adr/0002).
6. **Test-split tuning** — no held-out validation slice; λ=μ=0.5 and the FULL-vs-hub_only
   choice were selected on the same 50 test queries reported (epic rule 5 violation).
7. **Statistical fragility** — ArguAna +8.0pp = 4 flipped queries, McNemar p≈0.29 (not
   significant); subspace stochastic (88/84/82 across seeds). ~~R@100 not persisted~~ →
   **RESOLVED:** the harness now persists recall@{top_k} when top_k>10, and the committed
   NFCorpus artifact stores recall@100 (flat 31.33 / SPECTRA 32.94) — headline reproducible
   from disk. (This §8 sub-point was true when written; fixed same day.)

**Minors** — number inconsistency 32.94 vs 33.07 reconciled (§1, §2: launch-top_k dependence;
32.94 is the apples-to-apples value); λ=μ=0.5 asserted "justified" but unsupported (now
labeled a default).

**Standing to the report's credit** (also verified): the flat_dense floor is genuinely
same-embedding/same-process (no `if dataset ==` branch in the module); §4b openly withdraws the
mechanism claim; the k=500 regression is disclosed, not hidden. The problem was never fraud —
it was headline/status language over-claiming what the honest body supports. This section
brings the headline back in line with the evidence.
