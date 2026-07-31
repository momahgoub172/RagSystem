# RagSystem — Hybrid RAG Project Plan

> A .NET-based Retrieval-Augmented Generation system combining unstructured document search (PDF/Word) with structured business data (SQL Server: Sales, Inventory, and future sources), unified through Qdrant vector search (for documents) and NL2SQL (for structured data), with a cloud LLM synthesizing final answers.

---

## 1. Project Overview

### 1.1 Goal

Build a system that lets users ask natural-language questions and get accurate answers drawn from two very different kinds of sources:

1. **Unstructured documents** — PDFs, Word docs (policies, manuals, reports, etc.)
2. **Structured business databases** — existing SQL Server DBs for internal apps (Sales, Inventory, and more later)

The system should be smart enough to know *which kind of source* a question needs, and route accordingly.

### 1.2 Why this is hard (and interesting)

A normal RAG system just embeds documents and does similarity search. This project is harder because:

- Structured data (sales figures, inventory counts) doesn't answer well from embeddings alone — "total sales last quarter" needs a real query, not vector similarity.
- Documents need semantic search since there's no fixed schema to query against.
- The system needs a **router** that decides which path a question needs: document search or a live database query.

### 1.3 Scope boundaries (what this project is NOT, for now)

- Not a general "connect any database" product — starts with SQL Server only
- Not using a local/self-hosted LLM yet — cloud LLM (OpenAI/Azure OpenAI) for now
- Not handling write/update operations — read-only, question-answering only
- Not building fine-grained user auth/permissions in the MVP
- **Not embedding structured DB rows into the vector store** — structured data is answered exclusively through NL2SQL (live queries), not through semantic/vector search. This keeps structured answers always accurate and current, and keeps the system simpler to build and reason about.

---

## 2. High-Level Architecture

```
┌──────────────┐     ┌─────────────────────┐
│ PDF / Word   │     │ SQL Server DBs       │
│ Documents    │     │ (Sales, Inventory..) │
└──────┬───────┘     └──────────┬──────────┘
       │                        │
       v                        v
  Doc Loader              Schema Introspector
  + Chunker               (table/column catalog)
       │                        │
       v                        v
   Embed (text-embedding)  Embed (schema descriptions only)
       │                        │
       └────────────┬───────────┘
                     v
              ┌─────────────┐
              │   Qdrant     │  (Docker)
              │ collections: │
              │  - docs      │
              │  - schema_catalog │
              └──────┬──────┘
                     │
                     v
         ┌───────────────────────┐
         │   Query Router (LLM)   │
         │  classifies intent:    │
         │  document vs database  │
         └─────┬─────────────┬───┘
               │             │
       Document path   Database path
       (vector search   (NL2SQL → run
        in "docs")       query on SQL
               │          Server directly)
               └──────┬──────┘
                      v
              Cloud LLM (IChatClient)
              synthesizes final answer
                      │
                      v
              Answer + source citations
              (doc/page or SQL query used)
```

**Key change from earlier drafts:** structured data (Sales, Inventory) is *never* embedded into Qdrant as row/aggregate summaries. The only thing from the SQL side that touches Qdrant is the **schema catalog** (table/column descriptions), which helps the NL2SQL step pick the right tables — it does not represent actual business data.

---

## 3. Core Components Explained

### 3.1 Document Ingestion (`RagSystem.Ingestion.Docs`)

Handles PDF and Word files.

- **PDF parsing**: `PdfPig` (pure .NET, no native dependencies) — extracts text per page
- **Word parsing**: `DocumentFormat.OpenXml` — extracts paragraphs/headings/tables
- **Interface**: `IDocumentLoader` so new formats (e.g. `.txt`, `.md`, `.pptx`) can be added later without touching the pipeline
- **Chunking**: split extracted text into overlapping chunks (~300–500 tokens, ~10–15% overlap) to preserve context across chunk boundaries. Chunk size is a tuning knob — too small loses context, too large dilutes relevance.
- **Metadata per chunk**: source file name, page number, chunk index — needed later for citations
- **Storage**: embedded and stored in Qdrant's `docs` collection

### 3.2 Structured Data — NL2SQL Only (`RagSystem.Ingestion.Sql`)

This is the sole path for structured business data. No row embedding, no separate semantic search over DB content — every question about Sales/Inventory/etc. is answered by generating and running a real SQL query.

**a) Schema Catalog (supports NL2SQL routing)**

- A small metadata store describing each table/column in plain language (name, type, short description, sample values)
- This catalog is embedded into Qdrant (`schema_catalog` collection) — **this is metadata about the schema, not the actual data**
- When a question comes in, vector search over the catalog finds the 3–5 most relevant tables — these get included in the NL2SQL prompt instead of dumping the entire schema (saves tokens, improves accuracy)

**b) NL2SQL Generation**

- User question → LLM generates a real SQL query (using schema context + few-shot examples) → query executes against the actual DB (read-only)
- Handles both simple lookups ("show me Acme Corp's overdue invoices") and aggregations ("total sales last quarter by region") — since it's always a live query, there's no distinction needed between "semantic" and "analytic" structured questions anymore. NL2SQL handles both.
- Results come back as rows, which the LLM then formats into a natural-language answer

**c) Why no row embedding**

- Simpler system: one path for structured data, not two
- Always accurate: live query means no stale summaries to keep in sync
- Avoids the "embeddings can't do math" failure mode entirely, since nothing structured is ever approximated via similarity search

### 3.3 Vector Store — Qdrant (`RagSystem.VectorStore`)

- Self-hosted via Docker (see section 6)
- Now holds only two collections:
  - `docs` — document chunks (PDF/Word)
  - `schema_catalog` — table/column descriptions used to select relevant tables for NL2SQL prompts
- Accessed via the official `Qdrant.Client` .NET SDK
- Each point stores: vector, payload (metadata: source doc name/page, or table/column reference), and a `source_type` field used for filtering

### 3.4 Query Router

- A lightweight LLM call (or simple rule-based classifier for MVP) that decides, per incoming question:
  - **Document path** → vector search in Qdrant's `docs` collection
  - **Database path** → NL2SQL → execute against SQL Server
- MVP can start simple: a single LLM call with a system prompt like *"classify this question as DOCUMENT or DATABASE"* — no need for a trained classifier yet
- (Future) could support questions that need both — e.g. "does the sales figure match what's in the contract PDF" — but that's out of scope for now

### 3.5 NL2SQL Safety Layer (critical, not optional)

Because this path executes real generated SQL against real business databases:

1. **Read-only DB user** — connection string uses a SQL login with SELECT-only permissions, enforced at the DB level (not just "trust the prompt")
2. **Table allow-list** — only pre-approved tables/views can be referenced; reject anything outside it
3. **SQL structure validation** — parse the generated SQL using `Microsoft.SqlServer.TransactSql.ScriptDom` (native .NET T-SQL parser) to confirm it's a single, well-formed `SELECT` statement before execution
4. **Row limits & timeouts** — cap result set size and query execution time to protect the production DB
5. **Logging** — log every generated SQL statement, for debugging accuracy and for audit trail

### 3.6 LLM Integration (`RagSystem.AI`)

- Uses `Microsoft.Extensions.AI` abstractions:
  - `IChatClient` — for the router, NL2SQL generation, and final answer synthesis
  - `IEmbeddingGenerator<string, Embedding<float>>` — for embedding doc chunks and schema descriptions
- Backed by OpenAI or Azure OpenAI for now
- Abstraction means swapping to a local model later (e.g. via Ollama) is a config change, not a rewrite

### 3.7 API Layer (`RagSystem.Api`)

- ASP.NET Core minimal API
- Key endpoints (MVP):
  - `POST /ingest/documents` — upload/trigger doc ingestion
  - `POST /ingest/schema/{source}` — (re)build schema catalog for a given DB source
  - `POST /query` — main Q&A endpoint (runs router → doc search or NL2SQL → synthesis)
- Swagger/OpenAPI enabled for easy manual testing without building a UI first

**Manual test ([http://localhost:5098](http://localhost:5098)):**

```bash
# Ingest a Word document
curl -X POST http://localhost:5098/ingest/documents \
  -F "file=@/Users/goba/Programming/Projects/RagSystem/BCT_Orange Intranet Portal Migration & Revamp -RSD.docx"

# Ask a question
curl -X POST http://localhost:5098/query \
  -H "Content-Type: application/json" \
  -d '{"question": "what is orange project?"}'
```

---

## 4. Suggested Solution Structure

```
RagSystem.sln
 ├─ RagSystem.Api                 (endpoints, query router entrypoint)
 ├─ RagSystem.Core                (shared interfaces: IDocumentLoader, ISqlSource, IVectorStore)
 ├─ RagSystem.Ingestion.Docs      (PDF/Word loaders + chunker)
 ├─ RagSystem.Ingestion.Sql       (schema introspection, NL2SQL generation, safety validation)
 ├─ RagSystem.VectorStore         (Qdrant client wrapper, collection management)
 ├─ RagSystem.AI                  (IChatClient / IEmbeddingGenerator wiring, prompt templates)
 └─ RagSystem.Tests               (unit/integration tests)
```

---

## 5. Technology Stack Summary


| Layer                       | Technology                                                     |
| --------------------------- | -------------------------------------------------------------- |
| Language/Runtime            | .NET 8/9                                                       |
| API                         | ASP.NET Core Minimal API                                       |
| Doc parsing                 | PdfPig, DocumentFormat.OpenXml                                 |
| Vector DB                   | Qdrant (self-hosted, Docker)                                   |
| Vector DB client            | Qdrant.Client (.NET SDK)                                       |
| Structured DB               | SQL Server (Sales, Inventory, extensible)                      |
| DB access (dynamic queries) | Dapper                                                         |
| SQL validation              | Microsoft.SqlServer.TransactSql.ScriptDom                      |
| LLM abstraction             | Microsoft.Extensions.AI (`IChatClient`, `IEmbeddingGenerator`) |
| LLM provider                | OpenAI / Azure OpenAI (cloud, for now)                         |


---

## 6. Docker Compose (Qdrant) — Reference

```yaml
version: "3.8"
services:
  qdrant:
    image: qdrant/qdrant:latest
    ports:
      - "6333:6333"   # REST API
      - "6334:6334"   # gRPC
    volumes:
      - ./qdrant_storage:/qdrant/storage
```

