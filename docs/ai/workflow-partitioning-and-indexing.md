# Playbook: workflow partitioning & indexing

Every line-of-business workflow system faces a predictable database scaling bottleneck: workflow state tables grow unbounded, and instance payload data (`jsonb`) degrades index maintenance and cache hit rates when mixed into a single monolithic table.

Middt Framework solves this by implementing **declarative list partitioning by `WorkflowKey`** across all 5 engine tables, combined with **automated schema-driven B-Tree and GIN indexing**.

---

## 1. Partitioning architecture

All 5 core workflow tables in PostgreSQL are partitioned by `LIST ("WorkflowKey")`:

| Root Table | Partition Key | PostgreSQL PK | SQLite PK |
|---|---|---|---|
| `WorkflowInstances` | `"WorkflowKey"` | `("Id", "WorkflowKey")` | `("Id", "WorkflowKey")` |
| `WorkflowInstanceData` | `"WorkflowKey"` | `("InstanceId", "WorkflowKey")` | `("InstanceId", "WorkflowKey")` |
| `WorkflowJobs` | `"WorkflowKey"` | `("Id", "WorkflowKey")` | `("Id", "WorkflowKey")` |
| `WorkflowHistory` | `"WorkflowKey"` | `("Id", "WorkflowKey")` | `"Id"` (rowid autoincrement) |
| `WorkflowIncidents` | `"WorkflowKey"` | `("Id", "WorkflowKey")` | `"Id"` (rowid autoincrement) |

### Partition-wise joins
Because `WorkflowInstances` and `WorkflowInstanceData` share the exact same partition key (`WorkflowKey`) and composite key `(Id, WorkflowKey)`, PostgreSQL automatically enables **Partition-Wise Joins**:
```sql
SET enable_partitionwise_join = on;
```
When querying an instance with its data:
```sql
SELECT i."Id", i."State", d."Data"
FROM wf."WorkflowInstances" i
JOIN wf."WorkflowInstanceData" d ON i."Id" = d."InstanceId" AND i."WorkflowKey" = d."WorkflowKey"
WHERE i."WorkflowKey" = 'order-approval';
```
The query planner completely bypasses cross-table shuffling and joins only `WorkflowInstances_OrderApproval` with `WorkflowInstanceData_OrderApproval`.

---

## 2. Automatic partition and index lifecycle

Developers and AI assistants do **not** need to manually write raw partition DDL migrations for each workflow. 

When the API starts up:
1. `WorkflowStartup` invokes `WorkflowPartitionManager.EnsurePartitionsAndIndexesAsync(...)`.
2. The manager inspects all definitions loaded in `WorkflowRegistry` (from `demo/Middt.Demo.Api/Workflows/*.json`).
3. For each workflow:
   - Computes PascalCase suffix via `WorkflowPartitionManager.PartitionSuffix(workflowKey)`.
     *(e.g., `order-approval` $\to$ `OrderApproval`, `bike-repair_v2` $\to$ `BikeRepairV2`)*
   - Automatically executes `CREATE TABLE IF NOT EXISTS {schema}."{Table}_{Suffix}" PARTITION OF {schema}."{Table}" FOR VALUES IN ('{workflowKey}');` for all 5 tables.
   - Creates a catch-all fallback `{Table}_Default` for untracked or dynamic definitions.
4. **Inspects `dataSchema.properties` and `start.input.schema.properties`** from the JSON definition:
   - For every declared property, creates a dedicated, typed B-Tree functional index on that workflow's partition:
     - `integer` / `number`: `((((data->>'field')::numeric)))`
     - `boolean`: `((((data->>'field')::boolean)))`
     - `string`: `(((data->>'field')::text))`
5. Creates global `jsonb_path_ops` GIN indexes on `Data` for arbitrary JSON containment queries (`@>`).
6. On **SQLite** (used in unit tests and local dev without Docker), creates partial expression indexes:
   `CREATE INDEX IF NOT EXISTS "IX_Part_{Suffix}_{prop}" ON "WorkflowInstanceData" (json_extract("Data", '$.{prop}')) WHERE "WorkflowKey" = '{key}';`

---

## 3. How to configure a future workflow for indexing

When creating a new workflow JSON file (e.g. `invoice-processing.v1.json`):

### Step 1: Provide explicit property types in `dataSchema`
Always define the properties you anticipate querying, filtering, sorting, or joining on in `dataSchema.properties`.

```jsonc
{
  "key": "invoice-processing",
  "version": 1,
  "title": "Fatura Onay Süreci",
  "dataSchema": {
    "type": "object",
    "properties": {
      "invoiceNumber": { "type": "string" },
      "vendorId": { "type": "integer" },
      "totalAmount": { "type": "number" },
      "isPriority": { "type": "boolean" },
      "dueDate": { "type": "string", "format": "date-time" }
    }
  },
  "start": { ... },
  "states": [ ... ]
}
```

### What happens automatically:
At next boot, the framework will create:
- `wf."WorkflowInstances_InvoiceProcessing"`
- `wf."WorkflowInstanceData_InvoiceProcessing"`
- `wf."WorkflowJobs_InvoiceProcessing"`
- `wf."WorkflowHistory_InvoiceProcessing"`
- `wf."WorkflowIncidents_InvoiceProcessing"`
- Index `IX_Part_InvoiceProcessing_invoiceNumber` on `((data->>'invoiceNumber')::text)`
- Index `IX_Part_InvoiceProcessing_vendorId` on `(((data->>'vendorId')::numeric))`
- Index `IX_Part_InvoiceProcessing_totalAmount` on `(((data->>'totalAmount')::numeric))`
- Index `IX_Part_InvoiceProcessing_isPriority` on `(((data->>'isPriority')::boolean))`

### Step 2: Querying indexed workflow fields
When querying workflow instance data in custom endpoints, repositories, or `WorkflowView`:
- **Always include `WorkflowKey` in the query** to trigger partition pruning:
  ```csharp
  var results = await db.InstanceData
      .Where(d => d.WorkflowKey == "invoice-processing" && EF.Functions.JsonContains(d.Data, ...))
      .ToListAsync(cancellationToken);
  ```
- With typed B-Tree functional index in place, PostgreSQL can perform index range scans on numbers:
  ```sql
  SELECT * FROM wf."WorkflowInstanceData_InvoiceProcessing"
  WHERE ((data->>'totalAmount')::numeric) > 50000;
  ```

---

## 4. Advanced: custom composite indexes or special requirements

If a workflow requires compound indexes (e.g. `(vendorId, dueDate)` together):
1. In development, you can add an idempotent SQL statement in a custom hosted service or EF Core migration targeting the specific partition:
   ```sql
   CREATE INDEX IF NOT EXISTS "IX_InvoiceProcessing_Vendor_DueDate"
   ON wf."WorkflowInstanceData_InvoiceProcessing" (
       (((data->>'vendorId')::numeric)),
       (((data->>'dueDate')::text))
   );
   ```
2. Or declare an SQL view using `Middt.Framework.Workflow.View`:
   ```sql
   CREATE OR REPLACE VIEW wf."v_InvoiceProcessing" AS
   SELECT 
       i."Id",
       i."BusinessKey",
       i."State",
       i."Status",
       i."CreatedAt",
       (d."Data"->>'invoiceNumber') AS "InvoiceNumber",
       (d."Data"->>'vendorId')::numeric AS "VendorId",
       (d."Data"->>'totalAmount')::numeric AS "TotalAmount"
   FROM wf."WorkflowInstances_InvoiceProcessing" i
   JOIN wf."WorkflowInstanceData_InvoiceProcessing" d 
     ON i."Id" = d."InstanceId" AND i."WorkflowKey" = d."WorkflowKey";
   ```

---

## 5. Lifecycle and maintenance operations

### Detaching or Archiving Old Workflow Partitions
When a workflow version is retired or instances must be purged after legal retention limits:
```sql
-- 1. Detach partition from live traffic (instantaneous, zero table lock on active flows)
ALTER TABLE wf."WorkflowInstances" DETACH PARTITION wf."WorkflowInstances_OldFlow";
ALTER TABLE wf."WorkflowInstanceData" DETACH PARTITION wf."WorkflowInstanceData_OldFlow";

-- 2. Export, archive to cold storage, or drop
DROP TABLE wf."WorkflowInstances_OldFlow";
DROP TABLE wf."WorkflowInstanceData_OldFlow";
```
No WAL bloat, no table rewrite, zero impact on other workflows.

---

## 6. Verification checklist for AI assistants

When adding or altering a workflow:
1. Ensure the JSON file has `"key"` matching lowercase kebab-case (e.g. `"crm-lead"`).
2. Ensure properties intended for filtering or sorting are declared with `"type"` in `dataSchema.properties`.
3. Run tests:
   ```bash
   dotnet test --project tests/Middt.Framework.Tests --filter-class "*Workflow*"
   ```
4. Verify that CI build passes with 0 warnings:
   ```bash
   dotnet build Middt.Framework.slnx -p:ContinuousIntegrationBuild=true
   ```
