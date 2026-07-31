# RagSystem

RagSystem is a .NET 10 hybrid retrieval-augmented generation (RAG) API. It
answers either document questions or SQL Server questions by routing a natural
language prompt to the appropriate retrieval path.

## Current implementation

```
                    ┌────────────────────┐
                    │ POST /query         │
                    └─────────┬──────────┘
                              │
                       LLM intent router
                         ┌────┴────┐
                         │         │
                 document path  database path
                         │         │
                Qdrant `docs`  Qdrant `schema_catalog`
                         │         │
                    answer LLM  NL2SQL generator
                         │         │
                         └────┬────┘
                              │
                       JSON response
```

The vector store is used for document chunks and SQL schema metadata only.
Business rows are never embedded: database questions execute a live, validated,
read-only SQL Server query.

| Area | Current behavior |
| --- | --- |
| Runtime | .NET 10 / ASP.NET Core minimal API |
| LLM and embeddings | OpenRouter through `Microsoft.Extensions.AI`; configured models are `openai/gpt-4o-mini` and `text-embedding-3-small` |
| Vector database | Qdrant via gRPC on `localhost:6334`, using cosine vectors of size 1536 |
| Document format | Word (`.docx`; `.doc` is accepted by the extension check) through Open XML; PDF ingestion is not implemented |
| SQL source | SQL Server through Dapper and `Microsoft.Data.SqlClient` |
| Current SQL allow-list | `Customers`, `Orders` |

## API

The development launch profile serves HTTP at `http://localhost:5098`.
Swagger is enabled in the Development environment.

| Endpoint | Purpose |
| --- | --- |
| `POST /ingest/documents` | Upload a Word document as `multipart/form-data` field `file`, chunk it, embed it, and upsert it to Qdrant's `docs` collection. Re-ingesting the same chunk ID replaces it. |
| `POST /ingest/schema` | Read the allow-listed SQL Server tables, build one catalog entry per table, embed it, and upsert it to `schema_catalog`. Run this after changing the database schema. |
| `POST /query` | Classify the supplied `question` as a document or database question and execute the matching retrieval flow. `topK` is optional and applies to document retrieval. |

### Query responses

Document responses contain `intent`, `answer`, and `sources`. Database
responses contain `intent`, `answer`, and the generated `sql`.

If SQL generation or execution cannot proceed, `/query` returns HTTP 400 with
`intent: "database"`, `error`, `detail`, and `sql`. This makes generated-SQL
failures diagnosable without exposing an ASP.NET developer exception page.

## Database-query flow and safeguards

1. The router classifies sales, orders, customers, counts, totals, and records
   as database questions; policy/manual/text questions go to document search.
2. The API embeds the database question and retrieves the three most relevant
   schema-catalog entries.
3. The NL2SQL generator receives that schema context plus T-SQL rules and
   few-shot examples, including a per-region ranking query.
4. `SqlSafetyValidator` parses the result with ScriptDom. It permits exactly
   one `SELECT` statement, allows CTE names, and rejects physical tables outside
   the allow-list.
5. `SqlQueryExecutor` runs the validated SQL on the read-only connection with a
   10-second command timeout and a maximum of 200 returned rows. The row cap is
   connection-scoped (`SET ROWCOUNT`) rather than a string rewrite, so valid
   queries using `DISTINCT`, CTEs, and subqueries are preserved unchanged.
6. The answer service summarizes the returned rows in natural language.

The database login named by `ConnectionStrings:SqlServerReadOnly` must have
only the permissions appropriate for read access. The validator is a second
line of defense, not a substitute for database permissions.

## Local setup

Prerequisites:

- .NET 10 SDK
- SQL Server with the `Customers` and `Orders` tables and separate admin and
  read-only connection strings
- Qdrant listening on gRPC port 6334
- An OpenRouter API key

Configure secrets outside source control, for example with user secrets:

```bash
dotnet user-secrets set "ConnectionStrings:SqlServerAdmin" "<admin connection string>" --project src/RagSystem.Api
dotnet user-secrets set "ConnectionStrings:SqlServerReadOnly" "<read-only connection string>" --project src/RagSystem.Api
dotnet user-secrets set "OpenRouter:ApiKey" "<api key>" --project src/RagSystem.Api
```

Start the API and then build the schema catalog before database queries:

```bash
dotnet run --project src/RagSystem.Api

curl -X POST http://localhost:5098/ingest/schema
```

To ingest a document:

```bash
curl -X POST http://localhost:5098/ingest/documents \
  -F "file=@/absolute/path/to/document.docx"
```

To query it:

```bash
curl -X POST http://localhost:5098/query \
  -H "Content-Type: application/json" \
  -d '{"question":"Show the top 3 highest-value orders per region"}'
```

## Verification

Run the unit-test project and build the solution:

```bash
dotnet test tests/RagSystem.Tests/RagSystem.Tests.csproj
dotnet build RagSystem.sln
```

The executable smoke test sends six increasingly complex database questions to
a running API and checks that each produces a successful database response with
both SQL and an answer. It requires `curl` and `jq`.

```bash
./scripts/nl2sql-smoke-test.sh

# Use a different API host if needed.
API_BASE_URL=http://localhost:8080 ./scripts/nl2sql-smoke-test.sh
```

The covered query patterns are above-average values, per-group ranking,
month-over-month revenue, aggregate `HAVING`, percentages by region, and a
relative date range with a numeric filter. The script prints the generated SQL
and answer for each case, and exits nonzero on any failed case.

### Validated chatbot examples

The following user/chatbot exchanges were returned during a successful
six-query smoke-test run against the current sample database.

> **User**
>
> Which customers have placed orders above the average order value?
>
> **Chatbot**
>
> The customers who have placed orders above the average order value are as follows:
>
> 1. Acme Corp (Customer ID: 1)
> 2. Globex Inc (Customer ID: 2)
> 3. Initech (Customer ID: 3)
>
> Additionally, there is a large number of customers from "Soylent Corp" with IDs ranging from 4 to 200 who have also placed orders above the average order value. This includes clients like Soylent Corp #1 (Customer ID: 4), Soylent Corp #2 (Customer ID: 5), and so on, all the way through to Soylent Corp #197 (Customer ID: 200).
>
> In total, there are 200 customers listed who have all placed orders above the average order value.

---

> **User**
>
> Show the top 3 highest-value orders per region
>
> **Chatbot**
>
> The top three highest-value orders per region are as follows:
>
> **East Region:**
> 1. Order ID 1960 with a total amount of $47,316.62, placed on May 26, 2025.
> 2. Order ID 1096 with a total amount of $44,296.43, placed on August 27, 2025.
> 3. Order ID 797 with a total amount of $43,887.78, placed on May 13, 2026.
>
> **West Region:**
> 1. Order ID 1029 with a total amount of $49,977.58, placed on April 30, 2026.
> 2. Order ID 2223 with a total amount of $49,972.30, placed on December 10, 2024.
> 3. Order ID 535 with a total amount of $49,950.55, placed on December 25, 2025.
>
> These orders represent the highest values in their respective regions.

---

> **User**
>
> Compare total revenue this month versus last month
>
> **Chatbot**
>
> This month, the total revenue is $3,187,087.31, while last month it was $3,205,515.05. This shows a decrease in revenue of $18,427.74 compared to last month.

---

> **User**
>
> Which regions have more than 50 overdue orders?
>
> **Chatbot**
>
> Based on the query results, there are no regions that have more than 50 overdue orders.

---

> **User**
>
> What percentage of orders are overdue, broken down by region?
>
> **Chatbot**
>
> Based on the provided data, there are no overdue orders in the East region, while the West region has an overdue percentage of approximately 6.7%.

---

> **User**
>
> How many orders were placed in the last 90 days with a value over $10,000?
>
> **Chatbot**
>
> In the last 90 days, there were 302 orders placed with a value over $10,000.

---

## Solution layout

```
src/
  RagSystem.Api             Minimal API, endpoint orchestration, intent router
  RagSystem.AI              OpenRouter client, embeddings, answer generation
  RagSystem.Core            Shared models and interfaces
  RagSystem.Ingestion.Docs  Word loader and fixed-size chunker
  RagSystem.Ingestion.Sql   Schema catalog, NL2SQL, validation, execution
  RagSystem.VectorStore     Qdrant-backed vector-store implementation
tests/
  RagSystem.Tests           Unit-test project
scripts/
  nl2sql-smoke-test.sh      Live API smoke test for NL2SQL
```

## Known gaps and next steps

- PDF and additional document loaders are not implemented.
- Schema descriptions are currently emitted from SQL metadata; the
  `SchemaDescriptionProvider` and its JSON enrichment are not wired into the
  API.
- The SQL allow-list and Qdrant connection are currently configured in code.
  Multi-source SQL configuration is not yet active.
- There is no authentication, authorization, query audit log, retry/repair
  loop for invalid generated SQL, or combined document-and-database answering.
- Generated SQL is checked for safety and executability, but semantic accuracy
  still depends on the model and should be evaluated against representative
  business data before production use.
