using MiniOrm.Mapping;
using MiniOrm.Query;
using Npgsql;

namespace MiniOrm.ChangeTracking;

public enum EntityState
{
    Detached,
    Unchanged,
    Added,
    Modified,
    Deleted
}

public sealed class EntityEntry
{
    public required object Entity { get; init; }
    public required EntityMetadata Metadata { get; init; }
    public EntityState State { get; set; }
    public Dictionary<string, object?> OriginalValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ModifiedColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Snapshot()
    {
        OriginalValues.Clear();
        foreach (var column in Metadata.Columns)
            OriginalValues[column.ColumnName] = Clone(column.GetValue(Entity));
        ModifiedColumns.Clear();
        if (State == EntityState.Modified)
            State = EntityState.Unchanged;
    }

    public void DetectChanges()
    {
        if (State is EntityState.Added or EntityState.Deleted or EntityState.Detached)
            return;

        ModifiedColumns.Clear();
        foreach (var column in Metadata.Columns)
        {
            OriginalValues.TryGetValue(column.ColumnName, out var original);
            var current = column.GetValue(Entity);
            if (!EqualsNormalized(original, current))
                ModifiedColumns.Add(column.ColumnName);
        }

        State = ModifiedColumns.Count > 0 ? EntityState.Modified : EntityState.Unchanged;
    }

    private static object? Clone(object? value) => value is ICloneable c ? c.Clone() : value;

    private static bool EqualsNormalized(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a is Enum || b is Enum)
            return string.Equals(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
        return Equals(a, b);
    }
}

public sealed class ChangeTracker
{
    private readonly Dictionary<object, EntityEntry> _entries = new(ReferenceEqualityComparer.Instance);

    public IEnumerable<EntityEntry> Entries => _entries.Values;

    public EntityEntry Track(object entity, EntityMetadata metadata, EntityState state)
    {
        if (_entries.TryGetValue(entity, out var existing))
        {
            if (state != EntityState.Unchanged)
                existing.State = state;
            return existing;
        }

        var entry = new EntityEntry
        {
            Entity = entity,
            Metadata = metadata,
            State = state
        };
        if (state == EntityState.Unchanged)
            entry.Snapshot();
        _entries[entity] = entry;
        return entry;
    }

    public EntityEntry? Find(object entity) =>
        _entries.TryGetValue(entity, out var e) ? e : null;

    public void DetectChanges()
    {
        foreach (var entry in _entries.Values)
            entry.DetectChanges();
    }

    public void AcceptAll()
    {
        foreach (var entry in _entries.Values.ToList())
        {
            if (entry.State == EntityState.Deleted)
            {
                _entries.Remove(entry.Entity);
                continue;
            }

            entry.State = EntityState.Unchanged;
            entry.Snapshot();
        }
    }
}

internal static class SqlCommandFactory
{
    public static NpgsqlCommand Insert(NpgsqlConnection connection, EntityEntry entry)
    {
        var meta = entry.Metadata;
        var columns = meta.Columns.Where(c => !c.IsIdentity || c.GetValue(entry.Entity) is not null and not 0).ToList();
        if (columns.Count == 0)
            columns = meta.Columns.Where(c => !c.IsIdentity).ToList();

        var names = string.Join(", ", columns.Select(c => WhereTranslator.Quote(c.ColumnName)));
        var parms = string.Join(", ", columns.Select((_, i) => $"@i{i}"));
        var sql = $"INSERT INTO {WhereTranslator.Quote(meta.TableName)} ({names}) VALUES ({parms}) RETURNING {WhereTranslator.Quote(meta.Key.ColumnName)};";

        var cmd = new NpgsqlCommand(sql, connection);
        for (var i = 0; i < columns.Count; i++)
            cmd.Parameters.AddWithValue($"@i{i}", TypeMapper.ToDb(columns[i].GetValue(entry.Entity)));
        return cmd;
    }

    public static NpgsqlCommand? Update(NpgsqlConnection connection, EntityEntry entry)
    {
        if (entry.ModifiedColumns.Count == 0)
            return null;

        var meta = entry.Metadata;
        var sets = new List<string>();
        var cmd = new NpgsqlCommand { Connection = connection };
        var i = 0;
        foreach (var column in meta.Columns.Where(c => entry.ModifiedColumns.Contains(c.ColumnName) && !c.IsKey))
        {
            var p = $"@u{i++}";
            sets.Add($"{WhereTranslator.Quote(column.ColumnName)} = {p}");
            cmd.Parameters.AddWithValue(p, TypeMapper.ToDb(column.GetValue(entry.Entity)));
        }

        if (sets.Count == 0)
            return null;

        cmd.Parameters.AddWithValue("@id", TypeMapper.ToDb(meta.Key.GetValue(entry.Entity)));
        cmd.CommandText =
            $"UPDATE {WhereTranslator.Quote(meta.TableName)} SET {string.Join(", ", sets)} WHERE {WhereTranslator.Quote(meta.Key.ColumnName)} = @id;";
        return cmd;
    }

    public static NpgsqlCommand Delete(NpgsqlConnection connection, EntityEntry entry)
    {
        var meta = entry.Metadata;
        var cmd = new NpgsqlCommand(
            $"DELETE FROM {WhereTranslator.Quote(meta.TableName)} WHERE {WhereTranslator.Quote(meta.Key.ColumnName)} = @id;",
            connection);
        cmd.Parameters.AddWithValue("@id", TypeMapper.ToDb(meta.Key.GetValue(entry.Entity)));
        return cmd;
    }
}
