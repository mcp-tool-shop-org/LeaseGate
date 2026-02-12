using Microsoft.Data.Sqlite;

namespace LeaseGate.Storage;

public sealed class SqliteLeaseGateStateStore : ILeaseGateStateStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteLeaseGateStateStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public DurableStateSnapshot Load()
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            var snapshot = new DurableStateSnapshot();

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT lease_id, idempotency_key, acquired_at_utc, expires_at_utc, reserved_compute_units, request_json, constraints_json FROM leases";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    snapshot.ActiveLeases.Add(new StoredLease
                    {
                        LeaseId = reader.GetString(0),
                        IdempotencyKey = reader.GetString(1),
                        AcquiredAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                        ExpiresAtUtc = DateTimeOffset.Parse(reader.GetString(3)),
                        ReservedComputeUnits = reader.GetInt32(4),
                        RequestJson = reader.GetString(5),
                        ConstraintsJson = reader.GetString(6)
                    });
                }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT approval_id, status, expires_at_utc, token, used, request_json FROM approvals";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    snapshot.Approvals.Add(new StoredApproval
                    {
                        ApprovalId = reader.GetString(0),
                        Status = reader.GetString(1),
                        ExpiresAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                        Token = reader.GetString(3),
                        Used = reader.GetInt32(4) == 1,
                        RequestJson = reader.GetString(5)
                    });
                }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT timestamp_utc, token_cost FROM rate_events ORDER BY timestamp_utc";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    snapshot.RateEvents.Add(new StoredRateEvent
                    {
                        TimestampUtc = DateTimeOffset.Parse(reader.GetString(0)),
                        TokenCost = reader.GetInt32(1)
                    });
                }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT date_utc, reserved_cents FROM budget_state LIMIT 1";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    snapshot.BudgetState = new StoredBudgetState
                    {
                        DateUtc = DateTime.Parse(reader.GetString(0)).Date,
                        ReservedCents = reader.GetInt32(1)
                    };
                }
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT policy_version, policy_hash FROM policy_state LIMIT 1";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    snapshot.PolicyState = new StoredPolicyState
                    {
                        PolicyVersion = reader.GetString(0),
                        PolicyHash = reader.GetString(1)
                    };
                }
            }

            return snapshot;
        }
    }

    public void UpsertLease(StoredLease lease)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO leases (lease_id, idempotency_key, acquired_at_utc, expires_at_utc, reserved_compute_units, request_json, constraints_json)
                VALUES ($leaseId, $idempotencyKey, $acquiredAt, $expiresAt, $reservedComputeUnits, $requestJson, $constraintsJson)
                ON CONFLICT(lease_id) DO UPDATE SET
                    idempotency_key = excluded.idempotency_key,
                    acquired_at_utc = excluded.acquired_at_utc,
                    expires_at_utc = excluded.expires_at_utc,
                    reserved_compute_units = excluded.reserved_compute_units,
                    request_json = excluded.request_json,
                    constraints_json = excluded.constraints_json;
                """;
            cmd.Parameters.AddWithValue("$leaseId", lease.LeaseId);
            cmd.Parameters.AddWithValue("$idempotencyKey", lease.IdempotencyKey);
            cmd.Parameters.AddWithValue("$acquiredAt", lease.AcquiredAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$expiresAt", lease.ExpiresAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$reservedComputeUnits", lease.ReservedComputeUnits);
            cmd.Parameters.AddWithValue("$requestJson", lease.RequestJson);
            cmd.Parameters.AddWithValue("$constraintsJson", lease.ConstraintsJson);
            cmd.ExecuteNonQuery();
        }
    }

    public void RemoveLease(string leaseId)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM leases WHERE lease_id = $leaseId";
            cmd.Parameters.AddWithValue("$leaseId", leaseId);
            cmd.ExecuteNonQuery();
        }
    }

    public void ReplaceApprovals(IEnumerable<StoredApproval> approvals)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();

            using (var clear = connection.CreateCommand())
            {
                clear.CommandText = "DELETE FROM approvals";
                clear.ExecuteNonQuery();
            }

            foreach (var approval in approvals)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO approvals (approval_id, status, expires_at_utc, token, used, request_json) VALUES ($approvalId, $status, $expiresAt, $token, $used, $requestJson)";
                cmd.Parameters.AddWithValue("$approvalId", approval.ApprovalId);
                cmd.Parameters.AddWithValue("$status", approval.Status);
                cmd.Parameters.AddWithValue("$expiresAt", approval.ExpiresAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("$token", approval.Token);
                cmd.Parameters.AddWithValue("$used", approval.Used ? 1 : 0);
                cmd.Parameters.AddWithValue("$requestJson", approval.RequestJson);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void ReplaceRateEvents(IEnumerable<StoredRateEvent> events)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();

            using (var clear = connection.CreateCommand())
            {
                clear.CommandText = "DELETE FROM rate_events";
                clear.ExecuteNonQuery();
            }

            foreach (var evt in events)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO rate_events (timestamp_utc, token_cost) VALUES ($timestamp, $tokenCost)";
                cmd.Parameters.AddWithValue("$timestamp", evt.TimestampUtc.ToString("O"));
                cmd.Parameters.AddWithValue("$tokenCost", evt.TokenCost);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void SaveBudgetState(StoredBudgetState state)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();

            using (var clear = connection.CreateCommand())
            {
                clear.CommandText = "DELETE FROM budget_state";
                clear.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO budget_state (date_utc, reserved_cents) VALUES ($dateUtc, $reservedCents)";
                cmd.Parameters.AddWithValue("$dateUtc", state.DateUtc.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$reservedCents", state.ReservedCents);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    public void SavePolicyState(StoredPolicyState state)
    {
        lock (_lock)
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();

            using (var clear = connection.CreateCommand())
            {
                clear.CommandText = "DELETE FROM policy_state";
                clear.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO policy_state (policy_version, policy_hash) VALUES ($policyVersion, $policyHash)";
                cmd.Parameters.AddWithValue("$policyVersion", state.PolicyVersion);
                cmd.Parameters.AddWithValue("$policyHash", state.PolicyHash);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    private void EnsureSchema()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS leases (
                lease_id TEXT PRIMARY KEY,
                idempotency_key TEXT NOT NULL,
                acquired_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                reserved_compute_units INTEGER NOT NULL,
                request_json TEXT NOT NULL,
                constraints_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS approvals (
                approval_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                expires_at_utc TEXT NOT NULL,
                token TEXT NOT NULL,
                used INTEGER NOT NULL,
                request_json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS rate_events (
                timestamp_utc TEXT NOT NULL,
                token_cost INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS budget_state (
                date_utc TEXT NOT NULL,
                reserved_cents INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS policy_state (
                policy_version TEXT NOT NULL,
                policy_hash TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
