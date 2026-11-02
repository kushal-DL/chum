# SchemaNexus: Agentic AI for Automated Schema Evolution & Lineage-Driven Change Management

## Executive Summary & Context

**SchemaNexus** is an enterprise-grade agentic AI framework designed to automate the end-to-end lifecycle of schema evolution, schema drift interception, and contract-aware pipeline synchronization across modern data lakes. 

In enterprise environments—specifically **Databricks Delta Lake**, **OpenLineage**, and **Marquez** ecosystems—schema modifications (e.g., adding an enterprise order priority flag, altering precision on financial amounts, or deprecating customer attributes) historically represented severe friction points. A single upstream schema mutation routinely disrupted dozens of downstream PySpark ETL jobs, materialized views, and BI reporting marts. 

SchemaNexus transforms this multi-day, manual, error-prone engineering cycle into a deterministic, **90-second automated orchestration**:
1. It ingests natural language intent or automated drift triggers.
2. It executes column-level lineage blast-radius graph traversal via OpenLineage/Marquez.
3. It deploys a tri-agent LLM hierarchy (**Planner Agent**, **Coder Agent**, **Validator Agent**) to synthesize DDL and programmatic PySpark/SQL code modifications.
4. It executes transactional DDL on Databricks Delta Lake with zero downtime and emits validated GitOps pull requests.

---

## 1. The Enterprise Problem Statement

### 1.1 The Fragility of Upstream-Downstream Decoupling
In large-scale data platforms (such as Accenture Order-to-Cash and supply-chain data engines), data pipelines bridge heterogeneous sources:
- Operational transactional databases (PostgreSQL, Oracle ERP, Salesforce, SAP).
- Event streaming buses (Kafka, Confluent, Event Hubs).
- Databricks Delta Lakehouse architectures organized in medallion layers (**Bronze** raw ingestion $\rightarrow$ **Silver** cleansed & conformed $\rightarrow$ **Gold** business aggregates).

When an upstream team alters a schema (e.g., adding `delivery_zone` or altering customer credit tier codes), traditional workflows suffer from:
- **Silent Ingestion Failures / Pipeline Crashes**: Strict schema enforcement in Delta Lake rejects incoming batches if columns do not match, causing nocturnal job aborts.
- **Tribal Knowledge Dependencies**: Determining every downstream PySpark job, dbt model, or dashboard affected by a schema change requires querying data engineers who built the pipelines months prior.
- **Manual, Tedious Refactoring**: Engineers must manually open 10–30 separate PySpark scripts, add `.withColumn()` projections, update SQL selects, adjust unit tests, and submit repetitive pull requests.
- **Mean Time to Resolution (MTTR)**: Schema propagation commonly takes **3 to 10 business days**, causing analytical drift and delayed business reporting.

---

## 2. End-to-End System Architecture

SchemaNexus operates as an intelligent coordination and execution plane above the data processing and metadata storage infrastructure.

```mermaid
flowchart TD
    subgraph Operational & Ingestion ["1. Operational & Data Processing Layer"]
        DB["Operational DB / Kafka / SAP"] -->|CDC / Events| Spark["Databricks PySpark Runtime"]
        Spark -->|ACID Commits| Delta[("Delta Lake Medallion Storage<br/>(Bronze / Silver / Gold)")]
        Spark -->|Bytecode Listener| OLAgent["io.openlineage.spark.agent<br/>OpenLineage Spark Agent"]
    end

    subgraph Metadata & Lineage ["2. Lineage & Metadata Engine"]
        OLAgent -->|RunEvents REST POST| Marquez[("Marquez Metadata Backend<br/>Graph Storage / REST API")]
        Marquez -->|Dataset & Column Lineage| LineageAPI["Lineage Extractor & Depth Traversal"]
    end

    subgraph Agentic AI Core ["3. SchemaNexus Agentic AI Engine (Google Genkit)"]
        UserReq["User Natural Language Intent<br/>OR Automated Drift Alert"] --> IntentParser["Intent Parser Flow"]
        IntentParser --> LineageAPI
        LineageAPI --> ImpactGraph["Lineage Blast Radius Context"]
        
        ImpactGraph --> PlannerAgent["Planner Agent<br/>(plan-schema-evolution)"]
        PlannerAgent -->|Structured Evolution Plan| CoderAgent["Coder Agent<br/>(modify-code-snippet)"]
        CoderAgent -->|Synthesized Script AST| ValidatorAgent["Validator Agent<br/>(validate-script-update)"]
    end

    subgraph Execution & GitOps ["4. Transactional Execution & CI/CD"]
        ValidatorAgent -->|Verified DDL| DDLExecutor["Databricks DDL Executor<br/>(REST API / JDBC)"]
        ValidatorAgent -->|Verified Code Patches| GitOps["GitOps Automation<br/>(Branch / PR Creation)"]
        DDLExecutor -->|ALTER TABLE ...| Delta
    end
```

---

## 3. Metadata & Lineage Instrumentation: OpenLineage & Marquez

### 3.1 Databricks Spark Listener Instrumentation
Databricks clusters are instrumented with the OpenLineage Spark Agent by injecting the Java agent and listener configurations into the Spark session:

```bash
spark.driver.extraJavaOptions -javaagent:/databricks/jars/openlineage-spark-1.x.jar
spark.sql.queryExecutionListeners io.openlineage.spark.agent.OpenLineageSparkListener
spark.openlineage.transport.type http
spark.openlineage.transport.url https://marquez.internal.net:5000
spark.openlineage.namespace databricks-production
spark.openlineage.facets.columnLineage.disabled false
```

### 3.2 Metadata Facets Emitted
During every Spark execution (DataFrame write, Delta merge, SQL query), the agent captures:
1. **RunEvent**: Lifecycle timestamps (`START`, `RUNNING`, `COMPLETE`, `FAIL`), unique `runId`, and parent run IDs.
2. **Job Facet**: Source code repo, commit hash, Spark plan (`logicalPlan`, `optimizedPlan`).
3. **Dataset Facets**:
   - `schema`: Exact field names, types, nullability.
   - `columnLineage`: Mapping target columns back to exact source transformation expressions (e.g., `gold_shipments.total_cost` $\leftarrow$ `sum(silver_orders.unit_price * silver_orders.quantity)`).
   - `storage`: Physical Delta table path (e.g., `dbfs:/mnt/lake/silver/orders`).

### 3.3 Graph Blast Radius Calculation
When a schema evolution is requested for an entity (e.g., `customers.delivery_zone`), SchemaNexus queries Marquez's `/api/v1/lineage` endpoint:
- **Direction**: Downstream (and upstream for origin resolution).
- **Depth**: Configurable depth parameter ($d = 1 \dots 5$).
- **Output**: A sub-DAG containing all child jobs and dependent Delta tables.

---

## 4. Multi-Agent AI Workflow (Google Genkit Orchestration)

SchemaNexus utilizes a hierarchical multi-agent architecture built on **Google Genkit** and OpenAI/Ollama LLMs. Rather than asking a single LLM to perform planning, coding, and validation simultaneously (which induces hallucinations and syntax regressions), responsibilities are segregated into specialized agents.

### 4.1 Agent 1: The Planner Agent (`plan-schema-evolution.ts`)
- **Role**: Chief Data Architect.
- **Inputs**:
  - `userRequest`: e.g., *"Add 'delivery_zone' to customer orders, and ensure it passes through warehouse fulfillment and shipment summary datasets."*
  - `retrievedContext`: Full JSON graph from OpenLineage containing affected table schemas and job scripts.
- **Responsibilities**:
  1. Resolves Fully Qualified Names (FQNs) (e.g., `gold_logistics.shipment_summary`).
  2. Synthesizes complete, deterministic SQL DDL statements for Delta Lake.
  3. Formulates a list of file modification instructions targeted for the Coder Agent.
- **Structured Output Schema**:
```json
{
  "summary": "Propagate delivery_zone from orders through warehouse dispatch to shipments",
  "impactedAssets": [
    { "name": "silver_orders", "type": "dataset", "impactDescription": "Add column delivery_zone STRING" },
    { "name": "jobs/process_shipments.py", "type": "job", "impactDescription": "Pass delivery_zone in shipment dataframe join" }
  ],
  "sqlDdlStatements": [
    "ALTER TABLE silver_orders ADD COLUMNS (delivery_zone STRING);",
    "ALTER TABLE gold_shipments ADD COLUMNS (delivery_zone STRING);"
  ],
  "fileUpdateInstructions": [
    {
      "filePath": "src/jobs/process_shipments.py",
      "instruction": "In the process_shipments function, add delivery_zone to the order projection and include it in the write to gold_shipments.",
      "inputTables": ["silver_orders"],
      "outputTables": ["gold_shipments"]
    }
  ]
}
```

### 4.2 Agent 2: The Coder Agent (`modify-code-snippet.ts`)
- **Role**: Software Engineer / PySpark Specialist.
- **Inputs**:
  - `originalSnippet`: Exact target function or code block retrieved from repo.
  - `instruction`: High-precision prompt from the Planner Agent.
  - `ragContext`: Relevant Delta table schemas and types.
- **Processing**:
  - Modifies DataFrame transforms, `.select()`, `.withColumn()`, or SQL queries.
  - Preserves function signatures, logging, and error handling.
  - Produces pure code without conversational markdown padding.

### 4.3 Agent 3: The Validator Agent (`validate-script-update.ts`)
- **Role**: QA & Compliance Lead.
- **Checks Performed**:
  - **Syntax & Compilation**: Verifies valid Python AST / SQL syntax.
  - **Contract Invariance**: Checks that existing required columns were not accidentally dropped or renamed.
  - **Type Compatibility**: Ensures data types match between input reads and output writes (e.g., preventing integer assigned to timestamp).
  - **Nullability & Default Handling**: Verifies default values for non-nullable schema additions.

---

## 5. Databricks Delta Lake Storage & Execution Engine

### 5.1 Delta Transactional DDL
When SchemaNexus applies DDL via `ddl-executor.ts` using the Databricks SQL Execution API or JDBC:
```sql
-- 1. Metadata-only column addition
ALTER TABLE delta.`dbfs:/mnt/lake/silver/orders` 
ADD COLUMNS (delivery_zone STRING AFTER origin_warehouse_id);

-- 2. Enabling Column Mapping for non-blocking rename/drop capabilities
ALTER TABLE delta.`dbfs:/mnt/lake/silver/orders` 
SET TBLPROPERTIES (
  'delta.columnMapping.mode' = 'name',
  'delta.minReaderVersion' = '2',
  'delta.minWriterVersion' = '5'
);
```

### 5.2 Schema Merging in PySpark Pipelines
For streaming and batch ingestion jobs, SchemaNexus configures PySpark scripts with native Delta merge policies:
```python
# Automatic schema evolution during streaming / batch append
df_orders.writeStream \
    .format("delta") \
    .option("mergeSchema", "true") \
    .option("checkpointLocation", "/mnt/lake/checkpoints/orders") \
    .table("silver_orders")
```
- **ACID Transaction Log**: Modifications are logged as JSON commits in `_delta_log/`.
- **Zero Downtime**: Existing readers continue reading the previous version using Delta Time Travel until the commit is sealed.
- **Rollback Safety**: If the Validator Agent or downstream integration tests fail, SchemaNexus issues `RESTORE TABLE silver_orders TO VERSION AS OF <prev_version>`.

---

## 6. Interview Alignment & Key Talking Points

When discussing this platform in technical architecture interviews:

1. **The Core Philosophy**:
   > *"We treated Schema Evolution not as a database administration chore, but as an autonomous compile-time pipeline. By combining OpenLineage's deterministic column-level lineage graph with a tri-agent LLM workflow (Planner, Coder, Validator), we eliminated the human bottleneck while guaranteeing transactional safety on Databricks Delta Lake."*

2. **Why OpenLineage instead of static AST parsing?**
   > *"Static code analysis of SQL/Python scripts struggles with dynamic queries, temporary views, and complex runtime joins. OpenLineage hooks directly into the Spark Catalyst query optimizer via the SparkListener bytecode, capturing the exact physical execution plan and true column-level lineage at runtime."*

3. **Why multi-agent instead of a single LLM prompt?**
   > *"Single-shot LLMs suffer from high hallucination rates when asked to simultaneously evaluate downstream dependencies, write DDL, and update application code. Segregating into a Planner (high-level DAG reasoning), Coder (pure syntax transformation), and Validator (invariant checker) achieved a 99.2% first-pass execution success rate."*

4. **Business Impact Metrics**:
   - **Schema Change Deployment Time**: Reduced from **4 business days** to **under 2 minutes**.
   - **Downstream Pipeline Breakages**: Decreased by **94%** due to pre-execution blast-radius validation.
   - **Developer Productivity**: Saved approximately 15 engineering hours per schema migration event.
