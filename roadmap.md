# EquiTie Relationship Manager Bot — Production Roadmap

## Scope and Capabilities

### Beyond Q&A
- **Proactive nudges**: Push notifications for commitment levels, milestones, funding events.
- **Capital-call & fee reminders**: Timed alerts with in-app payment initiation.
- **Document & KYC requests**: Request outstanding documents, track status, nudge overdue items. Upload via in-app camera/file picker.
- **Onboarding**: Guided LP sign-up — identity verification, accreditation, fund selection, e-signature.
- **Reporting**: Scheduled portfolio reports pushed monthly/quarterly. Ad-hoc on request.
- **Drafting comms**: Draft quarterly letters, capital-call notices, tax statements for manager review before send.
- **Meeting prep**: Briefing pack before investor calls — positions, activity, talking points, outstanding items.

### Stays with human
Investment advice, discretionary decisions, dispute resolution, final sign-off on comms, escalation.

## Architecture and Tech Stack

- **Client**: SwiftUI iOS app — streaming chat, push notifications (APNs), document capture.
- **Backend**: Go or .NET 10 — agent runtime, session management, tool executor, guardrail engine, audit trail. API Gateway for auth (OAuth2/JWT), rate limiting.
- **LLM**: Azure OpenAI GPT-4o (EU region) — GDPR, enterprise agreement. GPT-4o-mini for eval judge.
- **Vector DB**: pgvector on Postgres — RAG over fund docs (PPMs, side letters, quarterly reports).
- **Session cache**: Redis — conversation state, rate limits.
- **Observability**: OpenTelemetry + Grafana for traces, metrics, logs per agent decision.

## Data and Integrations

| System | Integration | Direction |
|--------|------------|-----------|
| Portfolio ledger | REST API | Read-only (source of truth) |
| Fund administration | Batch / SFTP | Daily NAV, fees, waterfall |
| CRM | REST API | Read profile + history, write interaction log |
| KYC/AML provider | Webhook + API | Trigger checks on onboarding |
| E-signature (DocuSign) | REST API | Send docs, check status |
| Comms platform | SendGrid / Iterable | Send templated emails (bot drafts, human approves) |
| Market data | Bloomberg / PitchBook | Read-only, cached |

### Principles
- Read from sources of truth; no write-back to ledger.
- Write actions require human confirmation.
- All cross-system data flow logged to audit trail.

## AI Approach and Safety

### Models
- **GPT-4o** — primary assistant (reasoning, tool selection, response).
- **text-embedding-3-large** — RAG embeddings for document retrieval.
- **GPT-4o-mini** — LLM-as-judge for evals.
- **Custom fine-tune** — intent classification, entity extraction (distilled from GPT-4o logs).

### Grounding and Tool Use
- **RAG** over fund documents (chunked, embedded, stored in pgvector).
- **Structured data via deterministic tools** — same pattern as POC. The LLM never writes SQL or queries the ledger directly.
- **Deterministic code replaces the model** for arithmetic (MOIC, IRR, currency conversion), capital-call calculations, document status, KYC validation regex rules, and notification scheduling.

### Evaluation
- **Unit tests** per PR for tool logic.
- **LLM-as-judge evals** per PR for semantic correctness.
- **Adversarial evals** per PR (prompt injection, jailbreak, out-of-scope).
- **Nightly regression suite** — 500+ curated Q&A pairs scored by GPT-4o-mini.
- **Production monitoring** — thumbs up/down, escalation rate, conversation length dashboards.
- Eval pipeline gates all deploys; regression beyond threshold blocks PR.

### Guardrails and Compliance
- **No investment advice**: System prompt and tool responses include disclaimer. Model refuses recommendation questions.
- **Audit trail**: Every message, tool call, and result logged immutably with tamper-evident hashes.
- **Data protection**: PII redacted from LLM-bound logs. Data stays in EU (Azure EU region).
- **Human-in-the-loop**: Capital calls, fee reminders, and drafted comms require manager approval.
- **Session isolation**: Each investor sees only their own data (same POC pattern via session investor ID).
