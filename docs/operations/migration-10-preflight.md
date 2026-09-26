# Migration 10 preflight and rehearsal

Migration 10 (`repair_crafting_entry_snapshot_foreign_keys`) replaces the four
crafting-entry tables so their `account_profile_id` foreign keys reference
`account_crafting_snapshots(account_profile_id)`. It must not be first run on
the owner's original database. This runbook is the required preflight before
that change.

## Stop gate

Do not start the application against the original database, run a migration
command, overwrite a database file, or remove a backup until the owner has
reviewed the rehearsal evidence below and explicitly approves changing the
original database.

## 1. Identify and quiesce the original

1. In the running application's `Réglages` backup/recovery area, record the
   exact `databasePath` shown by **Backup and recovery**. This is the only
   authoritative target path; do not infer it from a filename or a temporary
   test database.
2. Record the application version/commit, current `schema_migrations` maximum
   version, file size, and timestamp. Keep the original path out of tickets,
   commits, logs, and screenshots if it could reveal personal filesystem data.
3. Stop the host cleanly and wait for it to exit. Do not copy a live SQLite
   file with Finder or a plain filesystem copy because WAL state may be
   omitted.

## 2. Create a consistent immutable backup

Prefer **Create local backup** in the application before stopping it: the
application uses SQLite's `BackupDatabase` API and places the backup in its
managed backup directory. Copy that completed backup to a separately retained
location and calculate a checksum.

If the application cannot start, use SQLite's backup mechanism while no other
writer is active, for example:

```bash
sqlite3 /absolute/original/tyrian-ledger.db ".backup '/absolute/retained/tyrian-ledger-pre-migration-10.db'"
shasum -a 256 /absolute/retained/tyrian-ledger-pre-migration-10.db
```

Keep the original untouched. The retained file is the rollback artifact; never
use `cp` as the only backup of a live database.

## 3. Capture pre-migration evidence

Run the following against the retained backup, redirecting output to a local
owner-only evidence file. The counts are intentionally aggregate-only.

```sql
PRAGMA integrity_check;
PRAGMA foreign_key_check;
SELECT max(version) AS schema_version FROM schema_migrations;
SELECT 'account_profiles', count(*) FROM account_profiles
UNION ALL SELECT 'account_crafting_snapshots', count(*) FROM account_crafting_snapshots
UNION ALL SELECT 'account_crafting_bank_entries', count(*) FROM account_crafting_bank_entries
UNION ALL SELECT 'account_crafting_material_entries', count(*) FROM account_crafting_material_entries
UNION ALL SELECT 'account_crafting_recipe_unlocks', count(*) FROM account_crafting_recipe_unlocks
UNION ALL SELECT 'account_crafting_disciplines', count(*) FROM account_crafting_disciplines
UNION ALL SELECT 'execution_plans', count(*) FROM execution_plans;
```

Both pragma checks must report no errors. Migration 10 should preserve every
listed row count; it changes only the crafting-entry table definitions and
their foreign-key targets.

## 4. Rehearse only in an isolated location

1. Create a new empty, access-restricted directory outside the original
   database directory.
2. Copy the retained backup into it using SQLite backup/restore semantics, not
   by replacing the original. Point a separate local host at that absolute
   copied path with `TyrianLedger__Database__Path`.
3. Start that isolated host once and confirm `schema_migrations` reports
   version `10` named `repair_crafting_entry_snapshot_foreign_keys`.
4. Repeat the integrity, foreign-key, and row-count queries from step 3. The
   integrity and foreign-key checks must remain clean and every listed count
   must match the pre-migration evidence exactly.
5. Exercise the crafting snapshot read and a Plan read against the isolated
   database. Confirm no startup repair failure and no unexpected execution-plan
   or crafting-entry count changes.
6. Shut down the isolated host. Keep its evidence, the pre-migration checksum,
   and the retained backup for owner review.

## 5. Approval checkpoint and original-database execution

Present the original path, backup checksum/location, pre/post rehearsal
results, row-count comparison, and any discrepancy to the owner. Only after
explicit approval may an operator start the approved application version once
against the original path. Immediately repeat the step-3 checks and compare
them with the retained evidence. If any integrity, foreign-key, migration-name,
or row-count check differs, stop; preserve the original and backup for
investigation rather than retrying or replacing files.
