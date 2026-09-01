using System.Reflection;
using MiniOrm.Loading;
using MiniOrm.Mapping;
using Npgsql;

namespace MiniOrm;

internal static class Materializer
{
    public static T Materialize<T>(NpgsqlDataReader reader, EntityMetadata metadata) where T : class, new()
    {
        var entity = new T();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var column = metadata.Columns.FirstOrDefault(c => c.ColumnName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (column is null)
                continue;
            column.SetValue(entity, reader.IsDBNull(i) ? null : reader.GetValue(i));
        }
        return entity;
    }

    public static void AttachLazyCollections<T>(T entity, MiniOrmContext context) where T : class
    {
        var metadata = MetadataCache.Get<T>();
        foreach (var nav in metadata.Navigations.Where(n => n.Kind == NavigationKind.HasMany))
        {
            if (nav.Property.GetValue(entity) is not null)
                continue;

            var method = typeof(Materializer).GetMethod(nameof(CreateLazy), BindingFlags.NonPublic | BindingFlags.Static)!;
            var list = method.MakeGenericMethod(typeof(T), nav.TargetType).Invoke(null, new object[] { entity, context, nav });
            nav.Property.SetValue(entity, list);
        }
    }

    private static LazyLoadList<TChild> CreateLazy<TParent, TChild>(
        TParent parent,
        MiniOrmContext context,
        NavigationMetadata navigation)
        where TParent : class, new()
        where TChild : class, new()
    {
        return new LazyLoadList<TChild>(() =>
            context.LoadCollection<TParent, TChild>(parent, navigation).Cast<TChild>().ToList());
    }
}
