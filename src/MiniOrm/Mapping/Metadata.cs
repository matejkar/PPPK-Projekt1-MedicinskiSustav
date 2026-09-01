using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using MiniOrm.Attributes;

namespace MiniOrm.Mapping;

public sealed class ColumnMetadata
{
    public required PropertyInfo Property { get; init; }
    public required string ColumnName { get; init; }
    public required string PostgresType { get; init; }
    public bool IsKey { get; init; }
    public bool IsIdentity { get; init; }
    public bool IsNullable { get; init; }
    public bool IsUnique { get; init; }
    public string? DefaultSql { get; init; }
    public string? ForeignTable { get; init; }
    public string? ForeignColumn { get; init; }

    public object? GetValue(object entity) => Property.GetValue(entity);

    public void SetValue(object entity, object? value)
    {
        if (value is DBNull)
        {
            Property.SetValue(entity, null);
            return;
        }

        if (value is null)
        {
            Property.SetValue(entity, null);
            return;
        }

        var target = Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
        if (value is string s && PostgresType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase))
            value = s.TrimEnd();
        Property.SetValue(entity, TypeMapper.ConvertToClr(value, target));
    }
}

public enum NavigationKind
{
    HasMany,
    HasOne,
    BelongsTo
}

public sealed class NavigationMetadata
{
    public required PropertyInfo Property { get; init; }
    public required NavigationKind Kind { get; init; }
    public required string ForeignKeyProperty { get; init; }
    public required Type TargetType { get; init; }
}

public sealed class EntityMetadata
{
    public required Type ClrType { get; init; }
    public required string TableName { get; init; }
    public required IReadOnlyList<ColumnMetadata> Columns { get; init; }
    public required IReadOnlyList<NavigationMetadata> Navigations { get; init; }

    public ColumnMetadata Key =>
        Columns.FirstOrDefault(c => c.IsKey)
        ?? throw new InvalidOperationException($"Entitet {ClrType.Name} nema [Key] stupac.");

    public ColumnMetadata ColumnByProperty(string propertyName) =>
        Columns.FirstOrDefault(c => c.Property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Svojstvo {propertyName} nije mapirano na stupac ({ClrType.Name}).");

    public ColumnMetadata? TryColumn(string propertyName) =>
        Columns.FirstOrDefault(c => c.Property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

    public NavigationMetadata Navigation(string propertyName) =>
        Navigations.FirstOrDefault(n => n.Property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Navigacija {propertyName} nije definirana na {ClrType.Name}.");
}

public static class MetadataCache
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    public static EntityMetadata Get(Type type) => Cache.GetOrAdd(type, Build);

    public static EntityMetadata Get<T>() => Get(typeof(T));

    private static EntityMetadata Build(Type type)
    {
        var table = type.GetCustomAttribute<TableAttribute>()?.Name
                    ?? ToSnake(type.Name) + "s";

        var columns = new List<ColumnMetadata>();
        var navigations = new List<NavigationMetadata>();

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<IgnoreAttribute>() is not null)
                continue;

            var hasMany = prop.GetCustomAttribute<HasManyAttribute>();
            var hasOne = prop.GetCustomAttribute<HasOneAttribute>();
            var belongsTo = prop.GetCustomAttribute<BelongsToAttribute>();

            if (hasMany is not null)
            {
                navigations.Add(new NavigationMetadata
                {
                    Property = prop,
                    Kind = NavigationKind.HasMany,
                    ForeignKeyProperty = hasMany.ForeignKeyProperty,
                    TargetType = GetCollectionElementType(prop.PropertyType)
                });
                continue;
            }

            if (hasOne is not null)
            {
                navigations.Add(new NavigationMetadata
                {
                    Property = prop,
                    Kind = NavigationKind.HasOne,
                    ForeignKeyProperty = hasOne.ForeignKeyProperty,
                    TargetType = prop.PropertyType
                });
                continue;
            }

            if (belongsTo is not null)
            {
                navigations.Add(new NavigationMetadata
                {
                    Property = prop,
                    Kind = NavigationKind.BelongsTo,
                    ForeignKeyProperty = belongsTo.ForeignKeyProperty,
                    TargetType = prop.PropertyType
                });
                continue;
            }

            if (IsNavigationLike(prop.PropertyType))
                continue;

            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var clr = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            var isNullableRef = !prop.PropertyType.IsValueType && new NullabilityInfoContext().Create(prop).WriteState != NullabilityState.NotNull;
            var isNullableVal = Nullable.GetUnderlyingType(prop.PropertyType) is not null;

            columns.Add(new ColumnMetadata
            {
                Property = prop,
                ColumnName = colAttr?.Name ?? ToSnake(prop.Name),
                PostgresType = TypeMapper.ToPostgres(clr, colAttr),
                IsKey = prop.GetCustomAttribute<KeyAttribute>() is not null,
                IsIdentity = prop.GetCustomAttribute<IdentityAttribute>() is not null,
                IsNullable = prop.GetCustomAttribute<NotNullAttribute>() is null && (isNullableRef || isNullableVal) && prop.GetCustomAttribute<KeyAttribute>() is null,
                IsUnique = prop.GetCustomAttribute<UniqueAttribute>() is not null,
                DefaultSql = prop.GetCustomAttribute<SqlDefaultAttribute>()?.Sql,
                ForeignTable = prop.GetCustomAttribute<ForeignKeyAttribute>()?.ReferencedTable,
                ForeignColumn = prop.GetCustomAttribute<ForeignKeyAttribute>()?.ReferencedColumn
            });
        }

        if (columns.Count(c => c.IsKey) != 1)
            throw new InvalidOperationException($"Entitet {type.Name} mora imati točno jedan [Key] stupac.");

        return new EntityMetadata
        {
            ClrType = type,
            TableName = table,
            Columns = columns,
            Navigations = navigations
        };
    }

    internal static string ToSnake(string name)
    {
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    private static bool IsNavigationLike(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[]))
            return false;
        if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            return true;
        return type.IsClass && type != typeof(string) && !type.IsPrimitive;
    }

    private static Type GetCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType()!;
        if (type.IsGenericType)
            return type.GetGenericArguments()[0];
        throw new InvalidOperationException($"Ne mogu odrediti element kolekcije za {type.Name}.");
    }
}

public static class TypeMapper
{
    public static string ToPostgres(Type clrType, ColumnAttribute? column)
    {
        if (!string.IsNullOrWhiteSpace(column?.PgType))
            return NormalizeDeclaredType(column.PgType, column);

        if (clrType.IsEnum)
            return column?.Length > 0 ? $"VARCHAR({column.Length})" : "VARCHAR(64)";
        if (clrType == typeof(int)) return "INTEGER";
        if (clrType == typeof(long)) return "BIGINT";
        if (clrType == typeof(short)) return "SMALLINT";
        if (clrType == typeof(decimal))
        {
            if (column?.Precision > 0)
                return $"DECIMAL({column.Precision},{Math.Max(column.Scale, 0)})";
            return "DECIMAL(18,4)";
        }
        if (clrType == typeof(float)) return "REAL";
        if (clrType == typeof(double)) return "DOUBLE PRECISION";
        if (clrType == typeof(bool)) return "BOOLEAN";
        if (clrType == typeof(char)) return "CHAR(1)";
        if (clrType == typeof(DateTimeOffset)) return "TIMESTAMP WITH TIME ZONE";
        if (clrType == typeof(DateTime)) return "TIMESTAMP WITHOUT TIME ZONE";
        if (clrType == typeof(Guid)) return "UUID";
        if (clrType == typeof(string))
        {
            if (column?.Length > 0)
                return $"VARCHAR({column.Length})";
            return "TEXT";
        }

        throw new NotSupportedException($"Tip {clrType.Name} nije podržan u MiniOrm TypeMapperu.");
    }

    public static object? ConvertToClr(object value, Type target)
    {
        if (target.IsInstanceOfType(value))
            return value;
        if (target.IsEnum)
        {
            if (value is string s)
                return Enum.Parse(target, s, ignoreCase: true);
            return Enum.ToObject(target, value);
        }
        if (target == typeof(char) && value is string cs)
            return cs.Length == 0 ? '\0' : cs[0];
        if (target == typeof(DateTimeOffset) && value is DateTime dt)
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
        if (target == typeof(DateTime) && value is DateTimeOffset dto)
            return dto.UtcDateTime;
        return Convert.ChangeType(value, target);
    }

    public static object ToDb(object? value)
    {
        if (value is null) return DBNull.Value;
        if (value is Enum e) return e.ToString();
        if (value is char ch) return ch.ToString();
        return value;
    }

    private static string NormalizeDeclaredType(string declared, ColumnAttribute column)
    {
        var t = declared.Trim().ToUpperInvariant().Replace('_', ' ');
        t = t.Replace("TIMESTAMP WITH TIMEZONE", "TIMESTAMP WITH TIME ZONE");
        t = t.Replace("TIMESTAMP WITHOUT TIMEZONE", "TIMESTAMP WITHOUT TIME ZONE");
        t = t.Replace("TIMESTAMPTZ", "TIMESTAMP WITH TIME ZONE");
        return t switch
        {
            "INT" or "INTEGER" => "INTEGER",
            "DECIMAL" or "NUMERIC" => column.Precision > 0
                ? $"DECIMAL({column.Precision},{Math.Max(column.Scale, 0)})"
                : "DECIMAL(18,4)",
            "FLOAT" => "DOUBLE PRECISION",
            "REAL" => "REAL",
            "VARCHAR" => column.Length > 0 ? $"VARCHAR({column.Length})" : "VARCHAR(255)",
            "CHAR" => column.Length > 0 ? $"CHAR({column.Length})" : "CHAR(1)",
            "TEXT" => "TEXT",
            "TIMESTAMP WITH TIME ZONE" => "TIMESTAMP WITH TIME ZONE",
            "TIMESTAMP WITHOUT TIME ZONE" => "TIMESTAMP WITHOUT TIME ZONE",
            _ => declared
        };
    }
}
