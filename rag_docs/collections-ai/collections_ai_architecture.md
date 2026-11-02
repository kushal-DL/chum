# Multi-Model Machine Learning Architecture for Enterprise Collections in Order-to-Cash (O2C)

## Executive Summary & Context

Within enterprise **Order-to-Cash (O2C)** platforms (such as Accenture's in-house financial transformation systems), managing Accounts Receivable (AR) and overdue collections across multinational enterprise portfolios entails billions of dollars in working capital. 

A frequent pitfall in non-production or theoretical ML designs is modeling collections as a single, vanilla binary classification problem (*"Will customer default? Yes/No"*). In B2B enterprise commerce:
1. Customers rarely disappear or outright default like retail credit card borrowers; rather, payments stall due to disputed rate cards, complex multi-subsidiary billing, missing Purchase Orders (POs), or strategic cash-flow management by corporate treasurers.
2. Collections teams operate under severe capacity constraints (e.g., 20 collectors managing 150,000 open invoices totaling \$2.5B).
3. Success is measured not by raw accuracy or F1-score, but by **working capital optimization**: reducing **Days Sales Outstanding (DSO)**, minimizing **Aging > 90 Days**, and maximizing cash recovery per collector labor hour.

To achieve this in production, we architected a **multi-model ensemble and decision-scoring pipeline** combining calibrated gradient boosting, survival analysis, NLP dispute classification, constrained priority optimization, and a Next-Best-Action (NBA) policy engine.

---

## 1. System Architecture Overview

```mermaid
flowchart TD
    subgraph Data Layer ["1. Ingestion & Feature Store (Databricks Delta & Feast)"]
        ERP["SAP / Oracle ERP Ledger"] --> Bronze[("Bronze Delta Tables")]
        CRM["Collector CRM & Emails"] --> Bronze
        Bronze --> Silver[("Silver Feature Store (Feast)")]
    end

    subgraph Scoring Pipeline ["2. Multi-Tiered ML Inference Engine"]
        Silver --> M1["Model 1: Calibrated Propensity-to-Pay<br/>(LightGBM + Isotonic Calibration)"]
        Silver --> M2["Model 2: Expected Days-to-Pay Survival Engine<br/>(Cox Proportional Hazards & AFT)"]
        Silver --> M3["Model 3: Dispute Likelihood & Root Cause<br/>(DistilRoBERTa + Tabular Ensemble)"]
        
        M1 -->|P(Paid On-Time)| Priority["Tier 4: Composite Value-at-Risk (VaR)<br/>Dynamic Priority Ranking Engine"]
        M2 -->|Survival Curve S(t)| Priority
        M3 -->|Dispute Prob & Reason| Priority
    end

    subgraph Action & Execution ["3. Decision Optimization & Action Layer"]
        Priority --> NBA["Tier 5: Next-Best-Action (NBA)<br/>Contextual Policy Engine"]
        NBA --> AutoDunning["Automated Low-Touch Dunning Email"]
        NBA --> CollectorQueue["Dynamic Daily Collector Worklist (WPF / Web)"]
        NBA --> DisputeWorkflow["Billing / Logistics Dispute Triage"]
        NBA --> CreditHold["Credit Hold / Risk Escalation"]
    end

    subgraph Feedback ["4. Closed-Loop Feedback & MLOps"]
        CollectorQueue -->|Call Outcomes & Notes| Silver
        AutoDunning -->|Email Opens & Payment Dates| Silver
        Silver --> MLflow["Databricks MLflow Registry & PSI Drift Monitor"]
    end
```

---

## 2. Detailed Multi-Model Component Specifications

### 2.1 Model 1: Calibrated Propensity-to-Pay Classifier
- **Objective**: Estimate the true posterior probability $P(\text{Paid within terms} \mid X)$ for every open invoice.
- **Algorithm**: LightGBM Classifier with Gradient-based One-Side Sampling (GOSS).
- **Core Feature Groups (120+ features)**:
  - **Payment Velocity**: Rolling 30/60/90-day customer DSO, standard deviation of payment lag, customer historical delinquency rate.
  - **Invoice Characteristics**: Log-transformed invoice amount, currency volatility, billing payment terms (Net 30/60/90), line item count.
  - **Credit Profile**: Credit limit utilization ($U = \frac{\text{Open AR} + \text{Unbilled}}{\text{Credit Limit}}$), Dun & Bradstreet risk tier, days since last credit review.
  - **Seasonality & Fiscal Milestones**: Days to customer fiscal quarter/year-end, month-end billing indicator.
- **Probability Calibration**:
  Raw tree ensemble probabilities are notoriously distorted toward extreme values. In financial operations, an uncalibrated 0.80 probability could correspond to an empirical recovery rate of only 62%.
  - We applied **Isotonic Regression** (for high-volume segments) and **Platt Scaling** (for low-volume corporate segments).
  - Evaluated using **Brier Score** ($< 0.08$) and **Expected Calibration Error (ECE < 0.025)**.

### 2.2 Model 2: Expected Days-to-Pay (DTP) Survival Engine
- **Objective**: Predict the full settlement survival distribution $S(t) = P(T > t)$, estimating the exact day cash will be collected.
- **Why Standard Regression Fails**: Open invoices that have not yet been paid are **right-censored**. Omitting them introduces massive survivorship bias (underestimating collection lag), while imputing today's date introduces negative bias.
- **Formulation**:
  - **Cox Proportional Hazards**: 
    $$\lambda(t \mid X) = \lambda_0(t) \exp(X \beta)$$
  - **Accelerated Failure Time (AFT)** with Log-Logistic baseline distribution:
    $$\log(T) = \mu + X \beta + \sigma W$$
- **Operational Value**: Enables Corporate Treasury to simulate aggregate daily cash inflows across rolling 30/60/90-day liquidity horizons:
  $$\mathbb{E}[\text{Cash Inflow}(t)] = \sum_{i \in \text{OpenInvoices}} \text{Amount}_i \cdot \left[ S_i(t - 1) - S_i(t) \right]$$

### 2.3 Model 3: Dispute Likelihood & Root Cause Classifier
- **Objective**: Up to **42% of enterprise overdue invoices are unpaid due to commercial disputes**, not insolvency. Calling a client to demand payment when the invoice has a pricing error damages client relationships and wastes collector hours.
- **Architecture**: Dual-branch multi-modal classifier.
  - **Branch A (Tabular)**: Price variance against contract rate card, freight charge mismatch against Bill of Lading, damaged goods return merchandise authorization (RMA) flags.
  - **Branch B (NLP)**: Fine-tuned **DistilRoBERTa** encoder ingesting unstructured customer emails, EDI 820 memo notes, and prior collector CRM free-text comments.
  - **Classification Heads**:
    1. Binary Head: $P(\text{Dispute})$.
    2. Multi-Class Head: Predicted dispute category:
       - `PRICING_DISCREPANCY` (mismatched tier discounts)
       - `DAMAGED_SHORTAGE` (warehouse/logistics discrepancies)
       - `MISSING_PO` (ERP workflow approval stall)
       - `TAX_EXEMPTION_MISSING` (invalid state/country tax certificate)

### 2.4 Tier 4: Dynamic Value-at-Risk (VaR) Priority Scoring
Rather than sorting collector queues by invoice age or invoice balance alone, the system computes a daily dynamic **Priority Score** for each open account $a$ across its set of open invoices $I_a$:

$$\text{Priority Score}(a) = \sum_{i \in I_a} \Big[ \text{Amount}_i \times \big(1 - P(\text{Propensity}_i)\big) \times \phi(\text{OverdueDays}_i) \times \psi(\text{Dispute}_i) \Big]$$

Where:
- $\phi(d) = 1 + \ln(1 + \max(0, d))$ is a sub-linear penalty scaling urgency without letting small old debts overshadow large borderline invoices.
- $\psi(\text{Dispute}) = \begin{cases} 0.2 & \text{if } P(\text{Dispute}) > 0.70 \text{ (Routed to Dispute Desk, removed from AR collector)} \\ 1.0 & \text{otherwise} \end{cases}$

Every morning at 06:00 UTC, a Databricks PySpark job evaluates all open invoices and compiles optimal, capacity-constrained worklists for each collector.

### 2.5 Tier 5: Next-Best-Action (NBA) Policy Engine
Once an account is prioritized, what should be done? A rule-constrained **Contextual Bandit** assigns the action:
1. **Action 1: Smart Automated Dunning**: Low risk, high propensity ($P > 0.85$), minor days overdue. Dispatches automated branded email with invoice PDF and payment link.
2. **Action 2: Dispute Desk Handshake**: High dispute risk ($P(\text{Dispute}) > 0.70$). Automatically opens a ticket in SAP/Salesforce for billing ops with the predicted root cause pre-selected.
3. **Action 3: High-Touch Collector Telephone Call**: High balance ($> \$100\text{k}$), low propensity, aging 15–30 days. Surfaces complete dossier to collector with talking points and historical dispute notes.
4. **Action 4: Credit Hold / Risk Escalation**: Persistent non-payment, credit utilization $> 95\%$, survival probability $P(T \le 90\text{d}) < 0.20$. Automated notification to Credit Committee to suspend fulfillment of pending shipments.

---

## 3. Production MLOps & Continuous Learning

- **Training Pipeline**: Bi-weekly automated retraining on Databricks clusters using MLflow tracking.
- **Model Drift Detection**:
  - **Population Stability Index (PSI)** monitored on continuous features (payment lags, invoice balances):
    $$\text{PSI} = \sum \left( \text{Actual}\% - \text{Expected}\% \right) \times \ln\left(\frac{\text{Actual}\%}{\text{Expected}\%}\right)$$
    Alert triggered if $\text{PSI} > 0.15$.
  - **Concept Drift**: Rolling Brier score tracking on calibrated propensity probabilities.
- **Inference Latency & Scale**: Daily batch inference across **1.8 million active invoices** executed in **14 minutes** on an 8-node Databricks cluster (Delta Lake I/O). Real-time single-invoice ad-hoc scoring via FastAPI containerized in Kubernetes ($< 45\text{ms}$).

---

## 4. Key Interview Talking Points & Production Insights

1. **Why multi-model instead of a single classification model?**
   > *"In enterprise B2B finance, predicting whether an invoice will default is useless because 98% of corporate clients eventually pay. The real business questions are: When will they pay? Why are they delaying? And where should 20 collectors focus their 8-hour workday to unlock the most cash? That requires survival analysis for settlement timing, NLP for dispute root-cause identification, and a calibrated priority engine."*

2. **How did you handle censored data in payment duration?**
   > *"If an invoice is 45 days old and still unpaid, you cannot treat 45 as the payment time, nor can you drop it. Using Cox Proportional Hazards and Accelerated Failure Time (AFT) survival models, we treated these as right-censored observations, yielding unbiased cash flow forecasting curves for Treasury."*

3. **What business ROI was achieved?**
   - **DSO Reduction**: Decreased portfolio Days Sales Outstanding by **4.2 days** within 6 months.
   - **Collector Efficiency**: Cash collected per collector hour increased by **28%**.
   - **Dispute Resolution Cycle**: Reduced average dispute resolution time from **19 days to 5 days** by proactively routing billing errors before collector outreach.
