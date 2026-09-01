using System.Collections;
using System.Linq.Expressions;
using MiniOrm.ChangeTracking;
using MiniOrm.Mapping;
using MiniOrm.Query;
using Npgsql;

namespace MiniOrm;

public sealed class DbSet<T> where T : class, new()
{
    private readonly MiniOrmContext _context;
    private readonly List<LambdaExpression> _filters = new();
    private readonly List<string> _orderBy = new();
    private readonly List<string> _includes = new();

    internal DbSet(MiniOrmContext context) => _context = context;

    private DbSet(MiniOrmContext context, List<LambdaExpression> filters, List<string> orderBy, List<string> includes)
    {
        _context = context;
        _filters.AddRange(filters);
        _orderBy.AddRange(orderBy);
        _includes.AddRange(includes);
    }

    private EntityMetadata Meta => MetadataCache.Get<T>();

    public DbSet<T> Where(Expression<Func<T, bool>> predicate)
    {
        var clone = Clone();
        clone._filters.Add(predicate);
        return clone;
    }

    public DbSet<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var clone = Clone();
        clone._orderBy.Add(WhereTranslator.TranslateOrder(Meta, keySelector, desc: false));
        return clone;
    }

    public DbSet<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        var clone = Clone();
        clone._orderBy.Add(WhereTranslator.TranslateOrder(Meta, keySelector, desc: true));
        return clone;
    }

    public DbSet<T> Include<TNav>(Expression<Func<T, TNav>> navigation)
    {
        var clone = Clone();
        clone._includes.Add(PropertyName(navigation.Body));
        return clone;
    }

    public DbSet<T> ThenInclude<TPrev, TNav>(Expression<Func<TPrev, TNav>> navigation)
    {
        if (_includes.Count == 0)
            throw new InvalidOperationException("ThenInclude zahtijeva prethodni Include.");
        var clone = Clone();
        clone._includes[^1] = $"{clone._includes[^1]}.{PropertyName(navigation.Body)}";
        return clone;
    }

    public T Add(T entity)
    {
        _context.ChangeTracker.Track(entity, Meta, EntityState.Added);
        return entity;
    }

    public void Remove(T entity)
    {
        var entry = _context.ChangeTracker.Track(entity, Meta, EntityState.Deleted);
        entry.State = EntityState.Deleted;
    }

    public T? Find(object key)
    {
        var where = $"{WhereTranslator.Quote(Meta.Key.ColumnName)} = @id";
        using var cmd = new NpgsqlCommand(
            $"SELECT {SelectList()} FROM {WhereTranslator.Quote(Meta.TableName)} WHERE {where} LIMIT 1;",
            _context.Connection);
        cmd.Parameters.AddWithValue("@id", TypeMapper.ToDb(key));
        return Query(cmd).FirstOrDefault();
    }

    public bool Any()
    {
        var where = WhereTranslator.Translate(Meta, _filters);
        using var cmd = new NpgsqlCommand(
            $"SELECT EXISTS(SELECT 1 FROM {WhereTranslator.Quote(Meta.TableName)} WHERE {where.Sql});",
            _context.Connection);
        foreach (var p in where.Parameters) cmd.Parameters.Add(p);
        return (bool)cmd.ExecuteScalar()!;
    }

    public int Count()
    {
        var where = WhereTranslator.Translate(Meta, _filters);
        using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {WhereTranslator.Quote(Meta.TableName)} WHERE {where.Sql};",
            _context.Connection);
        foreach (var p in where.Parameters) cmd.Parameters.Add(p);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public T? FirstOrDefault() => ToList().FirstOrDefault();

    public List<T> ToList()
    {
        var where = WhereTranslator.Translate(Meta, _filters);
        var order = _orderBy.Count == 0 ? "" : " ORDER BY " + string.Join(", ", _orderBy);
        using var cmd = new NpgsqlCommand(
            $"SELECT {SelectList()} FROM {WhereTranslator.Quote(Meta.TableName)} WHERE {where.Sql}{order};",
            _context.Connection);
        foreach (var p in where.Parameters) cmd.Parameters.Add(p);
        var items = Query(cmd);
        if (_includes.Count > 0)
            _context.EagerLoad(items, _includes);
        return items;
    }

    private List<T> Query(NpgsqlCommand cmd)
    {
        var list = new List<T>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var entity = Materializer.Materialize<T>(reader, Meta);
            _context.ChangeTracker.Track(entity, Meta, EntityState.Unchanged);
            Materializer.AttachLazyCollections(entity, _context);
            list.Add(entity);
        }
        return list;
    }

    private string SelectList() =>
        string.Join(", ", Meta.Columns.Select(c => WhereTranslator.Quote(c.ColumnName)));

    private DbSet<T> Clone() => new(_context, _filters, _orderBy, _includes);

    private static string PropertyName(Expression body)
    {
        if (body is UnaryExpression u)
            body = u.Operand;
        if (body is MemberExpression m)
            return m.Member.Name;
        throw new NotSupportedException("Include prihvaća samo navigacijsko svojstvo.");
    }
}

public abstract class MiniOrmContext : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private NpgsqlConnection? _connection;

    protected MiniOrmContext(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        ChangeTracker = new ChangeTracker();
        InitializeSets();
    }

    public ChangeTracker ChangeTracker { get; }
    internal NpgsqlConnection Connection => _connection ??= _dataSource.OpenConnection();

    public IReadOnlyList<Type> EntityTypes { get; private set; } = Array.Empty<Type>();

    private void InitializeSets()
    {
        var types = new List<Type>();
        foreach (var prop in GetType().GetProperties().Where(p =>
                     p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)))
        {
            var entityType = prop.PropertyType.GetGenericArguments()[0];
            types.Add(entityType);
            var setType = typeof(DbSet<>).MakeGenericType(entityType);
            prop.SetValue(this, Activator.CreateInstance(setType, this));
        }
        EntityTypes = types;
    }

    public int SaveChanges()
    {
        ChangeTracker.DetectChanges();
        var affected = 0;
        using var tx = Connection.BeginTransaction();
        try
        {
            foreach (var entry in ChangeTracker.Entries.Where(e => e.State == EntityState.Added))
            {
                using var cmd = SqlCommandFactory.Insert(Connection, entry);
                cmd.Transaction = tx;
                var id = cmd.ExecuteScalar();
                if (entry.Metadata.Key.IsIdentity && id is not null && id is not DBNull)
                    entry.Metadata.Key.SetValue(entry.Entity, id);
                affected++;
            }

            foreach (var entry in ChangeTracker.Entries.Where(e => e.State == EntityState.Modified))
            {
                using var cmd = SqlCommandFactory.Update(Connection, entry);
                if (cmd is null) continue;
                cmd.Transaction = tx;
                affected += cmd.ExecuteNonQuery();
            }

            foreach (var entry in ChangeTracker.Entries.Where(e => e.State == EntityState.Deleted))
            {
                using var cmd = SqlCommandFactory.Delete(Connection, entry);
                cmd.Transaction = tx;
                affected += cmd.ExecuteNonQuery();
            }

            tx.Commit();
            ChangeTracker.AcceptAll();
            return affected;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void Migrate() => new Migrations.MigrationRunner(this).ApplyPending();

    public void RollbackLastMigration() => new Migrations.MigrationRunner(this).RollbackLast();

    public Migrations.GeneratedMigration GenerateMigration(string? name = null) =>
        new Migrations.MigrationRunner(this).Generate(name);

    internal IList LoadCollection<TParent, TChild>(TParent parent, NavigationMetadata navigation)
        where TParent : class, new()
        where TChild : class, new()
    {
        var parentMeta = MetadataCache.Get<TParent>();
        var childMeta = MetadataCache.Get<TChild>();
        var fk = childMeta.ColumnByProperty(navigation.ForeignKeyProperty);
        var key = parentMeta.Key.GetValue(parent);
        using var cmd = new NpgsqlCommand(
            $"SELECT {string.Join(", ", childMeta.Columns.Select(c => WhereTranslator.Quote(c.ColumnName)))} FROM {WhereTranslator.Quote(childMeta.TableName)} WHERE {WhereTranslator.Quote(fk.ColumnName)} = @id;",
            Connection);
        cmd.Parameters.AddWithValue("@id", TypeMapper.ToDb(key));
        var list = new List<TChild>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var child = Materializer.Materialize<TChild>(reader, childMeta);
            ChangeTracker.Track(child, childMeta, EntityState.Unchanged);
            Materializer.AttachLazyCollections(child, this);
            list.Add(child);
        }
        return list;
    }

    internal void EagerLoad<T>(List<T> parents, List<string> includePaths) where T : class, new()
    {
        foreach (var path in includePaths)
        {
            var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            LoadPath(parents.Cast<object>().ToList(), typeof(T), segments, 0);
        }
    }

    private void LoadPath(List<object> current, Type currentType, string[] segments, int index)
    {
        if (index >= segments.Length || current.Count == 0)
            return;

        var meta = MetadataCache.Get(currentType);
        var nav = meta.Navigation(segments[index]);
        var targetMeta = MetadataCache.Get(nav.TargetType);

        if (nav.Kind == NavigationKind.HasMany)
        {
            var fk = targetMeta.ColumnByProperty(nav.ForeignKeyProperty);
            var ids = current.Select(e => meta.Key.GetValue(e)).Where(v => v is not null).Distinct().ToList();
            var children = LoadByForeignKeys(targetMeta, fk, ids);

            var grouped = children.GroupBy(c => fk.GetValue(c)?.ToString() ?? "");
            var map = grouped.ToDictionary(g => g.Key, g => g.ToList());

            var listType = typeof(List<>).MakeGenericType(nav.TargetType);
            foreach (var parent in current)
            {
                var key = meta.Key.GetValue(parent)?.ToString() ?? "";
                var items = map.TryGetValue(key, out var found) ? found : new List<object>();
                var typed = (IList)Activator.CreateInstance(listType)!;
                foreach (var item in items) typed.Add(item);
                nav.Property.SetValue(parent, typed);
            }

            var next = children;
            LoadPath(next, nav.TargetType, segments, index + 1);
        }
        else if (nav.Kind == NavigationKind.BelongsTo)
        {
            var fk = meta.ColumnByProperty(nav.ForeignKeyProperty);
            var ids = current.Select(e => fk.GetValue(e)).Where(v => v is not null).Distinct().ToList();
            var related = LoadByKeys(targetMeta, ids);
            var lookup = related.ToDictionary(r => targetMeta.Key.GetValue(r)?.ToString() ?? "", r => r);
            foreach (var parent in current)
            {
                var key = fk.GetValue(parent)?.ToString() ?? "";
                lookup.TryGetValue(key, out var match);
                nav.Property.SetValue(parent, match);
            }
            LoadPath(related, nav.TargetType, segments, index + 1);
        }
        else
        {
            var fk = targetMeta.ColumnByProperty(nav.ForeignKeyProperty);
            var ids = current.Select(e => meta.Key.GetValue(e)).Where(v => v is not null).Distinct().ToList();
            var related = LoadByForeignKeys(targetMeta, fk, ids);
            var lookup = related.ToDictionary(r => fk.GetValue(r)?.ToString() ?? "", r => r);
            foreach (var parent in current)
            {
                var key = meta.Key.GetValue(parent)?.ToString() ?? "";
                lookup.TryGetValue(key, out var match);
                nav.Property.SetValue(parent, match);
            }
            LoadPath(related, nav.TargetType, segments, index + 1);
        }
    }

    private List<object> LoadByKeys(EntityMetadata meta, List<object?> ids)
    {
        if (ids.Count == 0) return new List<object>();
        var inParams = string.Join(", ", ids.Select((_, i) => $"@k{i}"));
        using var cmd = new NpgsqlCommand(
            $"SELECT {string.Join(", ", meta.Columns.Select(c => WhereTranslator.Quote(c.ColumnName)))} FROM {WhereTranslator.Quote(meta.TableName)} WHERE {WhereTranslator.Quote(meta.Key.ColumnName)} IN ({inParams});",
            Connection);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@k{i}", TypeMapper.ToDb(ids[i]));
        return ReadEntities(cmd, meta);
    }

    private List<object> LoadByForeignKeys(EntityMetadata meta, ColumnMetadata fk, List<object?> ids)
    {
        if (ids.Count == 0) return new List<object>();
        var inParams = string.Join(", ", ids.Select((_, i) => $"@k{i}"));
        using var cmd = new NpgsqlCommand(
            $"SELECT {string.Join(", ", meta.Columns.Select(c => WhereTranslator.Quote(c.ColumnName)))} FROM {WhereTranslator.Quote(meta.TableName)} WHERE {WhereTranslator.Quote(fk.ColumnName)} IN ({inParams});",
            Connection);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@k{i}", TypeMapper.ToDb(ids[i]));
        return ReadEntities(cmd, meta);
    }

    private List<object> ReadEntities(NpgsqlCommand cmd, EntityMetadata meta)
    {
        var list = new List<object>();
        using var reader = cmd.ExecuteReader();
        var materialize = typeof(Materializer).GetMethod(nameof(Materializer.Materialize))!.MakeGenericMethod(meta.ClrType);
        while (reader.Read())
        {
            var entity = materialize.Invoke(null, new object[] { reader, meta })!;
            ChangeTracker.Track(entity, meta, EntityState.Unchanged);
            var attach = typeof(Materializer).GetMethod(nameof(Materializer.AttachLazyCollections))!.MakeGenericMethod(meta.ClrType);
            attach.Invoke(null, new[] { entity, this });
            list.Add(entity);
        }
        return list;
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _dataSource.Dispose();
        GC.SuppressFinalize(this);
    }
}
