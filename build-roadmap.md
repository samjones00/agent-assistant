# Investor Assistant — 6-Month Build Roadmap

## 1. Scope & Capabilities

### Phase 0 — Q&A (months 1–2)
Accurate, grounded Q&A over the full data model (the prototype, hardened). The assistant answers every question the prototype handles, with production reliability.

### Phase 1 — Proactive Nudges (months 2–4)
The bot initiates, not just responds:
- **Capital-call reminders**: "Your Q3 capital call for Forgecraft Series B is due in 14 days."
- **Fee alerts**: "Your Q4 management fee of $2,400 will be deducted on Dec 1."
- **Distribution notifications**: "Helianthe Energy exit proceeds ($18,400 net) are being processed."
- **KYC/document reminders**: "Your KYC documents expire in 30 days — please re-submit."
- **Portfolio milestones**: "Your MOIC on Pulsegrid Health just crossed 2.0x."

### Phase 2 — Reporting & Drafting (months 3–5)
- **Quarterly portfolio summary** — auto-generated PDF with performance, contributions, fees, and distributions.
- **Investor letter drafting** — drafts personalised quarterly letters for RM review.
- **Custom report builder** — investor asks for any slice of their data and gets a formatted report.

### Phase 3 — Concierge Actions (months 4–6)
Limited, high-confidence outbound actions with human-in-the-loop:
- **Capital-call payment initiation** — bot prepares the instruction, RM approves.
- **Fee waiver requests** — investor asks, bot drafts the request for internal approval.
- **Document collection** — bot requests documents, tracks receipt, flags overdue items.

### Stays with a human
- Investment advice or recommendations ("should I invest in X?")
- Negotiating fee discounts or side letters
- Handling disputes or complaints
- Approving any financial transaction above $50k
- Onboarding new investors (KYC/AML approval)

---

## 2. Architecture & Tech Stack

```
┌─────────────────────────────────────────────────────────────┐
│                    iOS Investor App                         │
│  (SwiftUI + MessageChannel SDK)                             │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTPS / WSS
┌────────────────────────▼────────────────────────────────────┐
│              API Gateway (Azure API Management)              │
│  Auth (Entra ID) · Rate limit · Audit log                   │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                 Orchestration Layer                          │
│  Supervisor Agent (GPT-4o) → Specialist Agents:             │
│    PortfolioAgent · FeesAgent · DocumentsAgent · CommsAgent │
│  LangGraph for stateful multi-turn flows                    │
│  Guardrails: content filter + input/output validation       │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                 Data / Retrieval Layer                       │
│  PostgreSQL (canonical) + TimescaleDB (time-series marks)    │
│  Azure AI Search (hybrid vector + keyword RAG for docs)     │
│  Redis (session cache, rate-limit counters)                 │
└────────────────────────┬────────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────────┐
│                 Integrations Layer                           │
│  Portfolio Ledger · Fund Admin · CRM (HubSpot) ·            │
│  KYC/AML (Persona) · e-Sign (DocuSign) · Comms (SendGrid)   │
│  Valuation data (PitchBook/Crunchbase) · Market data        │
└─────────────────────────────────────────────────────────────┘
```

### Stack choices

| Layer | Choice | Rationale |
|---|---|---|
| Client | SwiftUI (iOS native) | Target is iOS investor app; native > web view for push + offline |
| API | ASP.NET Core 10 + YARP | Same stack as MAF prototype; proven at scale |
| Orchestration | LangGraph (Python) | Best-in-class for stateful agent DAGs; supervisor + specialist agents |
| Models | GPT-4o (primary) + GPT-4o-mini (triage/simple) | 4o for complex reasoning; mini for classification/guards |
| Vector store | Azure AI Search | Managed, hybrid, role-based doc-level security |
| OLTP | PostgreSQL + TimescaleDB | Relational data + time-series valuations in one engine |
| Cache | Redis | Session state, rate-limit counters, agent context |
| Streaming | Azure SignalR | Real-time chat + push notifications |
| Compute | Azure Container Apps | Serverless containers, auto-scale, low cold start |
| Auth | Entra ID (formerly Azure AD) | Investor's corporate identity; supports B2B guest |
| Observability | Azure Monitor + LangSmith | Traces, LLM token audit, cost attribution |

---

## 3. Data & Integrations

### Core systems (must-connect)

| System | Data | Direction | Sync cadence |
|---|---|---|---|
| Portfolio Ledger | Holdings, capital calls, contributions, NAV | Bidirectional | Real-time (event) |
| Fund Admin | Fee calculations, investor statements | Read | Daily batch |
| CRM (HubSpot/Salesforce) | Investor profile, comms history, tickets | Bidirectional | Real-time |
| KYC/AML (Persona) | KYC status, document expiry | Read | Daily |
| e-Signature (DocuSign) | Side letters, subscription docs | Read | Event-triggered |
| Comms (SendGrid/Twilio) | Email, SMS notifications | Write | On-demand |
| Valuation (PitchBook/Mark-to-Market) | Comparable valuations, FMV marks | Read | Weekly |
| Document store (SharePoint/S3) | PPM, legal docs, financial statements | Read | On-demand (RAG) |

### Data flow architecture
- **Event bus** (Azure Event Grid) for real-time integration: capital call created → nudge agent triggers → investor notified.
- **Daily reconciliation** (Azure Data Factory) syncs fund admin → PostgreSQL for consistent reporting.
- **Docs** are ingested into Azure AI Search with per-document ACLs scoped to investor/entity.

---

## 4. AI Approach & Safety

### Model strategy
- **GPT-4o** for the reasoning-heavy supervisor and specialist agents (portfolio, fees, docs).
- **GPT-4o-mini** for classification/guardrail checks (is this a portfolio question? does it ask for advice?).
- **Deterministic code** for all arithmetic (MOIC, FX, sums) — the model never computes numbers directly. It calls tool functions that return exact results.
- **No local or fine-tuned models** for prototype/phase 0; may fine-tune a smaller model for classification in Phase 2 if cost requires.

### Grounding & retrieval
- Structured data queries go through the CsvQueryServer (→ PostgreSQL in production), never through the model's training data.
- Document retrieval uses Azure AI Search with hybrid (vector + keyword) search over ingested PDFs/docs.
- Every answer cites source rows or document references; the model is system-prompted to omit any claim it cannot ground.

### Tool use
- Model selects tools, never computes. Tool definitions are strict (required params, type constraints, enum validation).
- Calculator tool is sandboxed (no file/network access). CsvQuery tool is read-only.
- Agents are single-purpose — a fees agent can only call fee-related tools, reducing attack surface.

### Evaluation
- **Offline eval suite**: ~200 test cases covering every edge case in the dataset, plus adversarial inputs (prompt injection, asking about other investors, asking for advice).
- **Automated assertion**: for each test case, assert answer correctness (numerical), citation presence, and refusal for out-of-bounds questions.
- **Human eval**: weekly review of 50 random conversations by the RM team — rate accuracy, tone, personalisation, and safety.
- **Metrics**: precision @k for numbers (within 0.5% = correct), citation rate (%), unsafe refusal rate (%), RM satisfaction score.

### Guardrails (production)
- **Input guard**: classify each user message as permitted / not-permitted (investment advice, competitor query, personal attack) — reject with a polite refusal.
- **Output guard**: post-process every LLM response to strip any hallucinated numbers, remove PII leaks, check for disallowed topics.
- **Audit trail**: every question, tool call, tool result, and final answer is logged to an append-only store (Azure Cosmos DB + immutable blob storage) with investor_id, timestamp, model, tokens, latency.
- **Data isolation**: all queries are parameterised with the authenticated investor_id. RLS in PostgreSQL enforces the same constraint at the database level. No LLM prompt ever contains another investor's data.
- **No advice**: system prompt explicitly instructs the model to refuse any question that could be construed as investment advice ("should I invest", "what do you think about", "is this a good deal"). RM escalation path offered instead.

---

## 5. Team & Hiring

| Role | Seniority | Headcount | When |
|---|---|---|---|
| Tech Lead / Architect | Staff+ | 1 | Month 0 |
| Backend Engineer (C# / .NET) | Senior | 2 | Month 0 |
| Backend Engineer (Python — AI) | Senior | 1 | Month 0 |
| Frontend Engineer (iOS / SwiftUI) | Senior | 1 | Month 0 |
| ML Engineer (evaluation + guardrails) | Senior | 1 | Month 1 |
| Data Engineer (integrations + pipelines) | Senior | 1 | Month 2 |
| Product Manager | Senior | 1 | Month 0 |
| Designer (product + conversation design) | Mid | 1 | Month 1 |
| QA / Test Engineer | Mid | 1 | Month 2 |
| SRE / Platform Engineer | Senior | 1 | Month 3 |

Total: 11 people by month 3, stabilising at 11–12.

---

## 6. Timeline

| Phase | Months | What ships |
|---|---|---|
| **P0: Core Q&A** | 0–2 | Production API, PostgreSQL, hosted GPT-4o, eval suite, auth (Entra ID), prototype hardened → internal beta with 10 friendly investors |
| **P1: Proactive** | 2–3 | Notification engine, capital-call/fee nudges, KYC reminders, supervisor agent + specialist agents, Azure AI Search for docs |
| **P2: Reporting** | 3–5 | Quarterly PDF generator, letter drafts, custom report builder, multi-agent orchestration with LangGraph, early iOS integration |
| **P3: Actions** | 4–6 | Human-in-the-loop for payment initiation / fee waiver requests, full iOS native chat UI, push notifications, audit trail complete, compliance sign-off, public launch |
| **P4: Polish** | 5–6 | Conversation eval dashboard, RM satisfaction survey, performance tuning, cost optimisation (model caching, mini-model routing) |

---

## 7. Risks & Cost

### Risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **Hallucination on numeric data** | Medium | Critical | All arithmetic via deterministic tool calls; eval suite catches regressions |
| **Prompt injection / jailbreak** | Medium | High | Input guard model + output guard; regular red-teaming |
| **Investor data leak across sessions** | Low | Critical | RLS at DB level, parameterised queries, no cross-investor data in prompts |
| **LLM latency under load** | Medium | Medium | GPT-4o-mini for triage, Redis caching, async streaming responses |
| **Integration brittleness (fund admin)** | High | Medium | Bulkhead pattern, stale-data warning in UI, daily reconciliation |
| **Investor ignores / disables the bot** | Medium | Low | RM override, graceful fallback to email, human hand-off |

### Build-vs-buy

| Decision | Choice | Rationale |
|---|---|---|
| LLM | Build (Azure OpenAI) | Cheaper at scale, data stays in Azure, fine-tuning path |
| Agent orchestration | Build (LangGraph) | Too early for off-the-shelf; need custom agent routing |
| RAG pipeline | Build (Azure AI Search) | Custom chunking + ACLs required |
| Notification engine | Build | Simple: SendGrid + Twilio APIs. No SaaS fits the data model |
| Eval framework | Build | Need investor-specific numeric assertions; off-the-shelf doesn't cover |
| Auth/identity | Buy (Entra ID) | Standard; no reason to build |
| KYC/AML | Buy (Persona) | Regulated; off-the-shelf compliance |
| e-Signature | Buy (DocuSign) | Market standard, API-first |
| CRM | Buy (HubSpot) | Already in use at EquiTie |

### Cost shape (monthly run-rate)

| Category | Month 1–2 | Month 3–4 | Month 5–6 |
|---|---|---|---|
| LLM (Azure OpenAI) | $2k | $5k | $8k |
| Compute (ACA) | $1k | $3k | $5k |
| Data (PostgreSQL + AI Search) | $1k | $2k | $3k |
| SaaS integrations | $1k | $2k | $3k |
| Observability | $500 | $1k | $1k |
| **Total** | **$5.5k** | **$13k** | **$20k** |

Excluding team salaries. Scales with investor count: ~$0.20/investor/month at 10k investors, dominated by LLM tokens.
