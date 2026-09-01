namespace MiniOrm.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class TableAttribute : Attribute
{
    public string Name { get; }
    public TableAttribute(string name) => Name = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    public string? Name { get; }
    /// <summary>PostgreSQL type override, e.g. INT, VARCHAR, TEXT, DECIMAL, FLOAT, CHAR, TIMESTAMP WITH TIMEZONE.</summary>
    public string? PgType { get; set; }
    public int Length { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }

    public ColumnAttribute() { }
    public ColumnAttribute(string name) => Name = name;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class KeyAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class IdentityAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotNullAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class UniqueAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SqlDefaultAttribute : Attribute
{
    public string Sql { get; }
    public SqlDefaultAttribute(string sql) => Sql = sql;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ForeignKeyAttribute : Attribute
{
    public string ReferencedTable { get; }
    public string ReferencedColumn { get; }

    public ForeignKeyAttribute(string referencedTable, string referencedColumn = "id")
    {
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class HasManyAttribute : Attribute
{
    public string ForeignKeyProperty { get; }
    public HasManyAttribute(string foreignKeyProperty) => ForeignKeyProperty = foreignKeyProperty;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class HasOneAttribute : Attribute
{
    public string ForeignKeyProperty { get; }
    public HasOneAttribute(string foreignKeyProperty) => ForeignKeyProperty = foreignKeyProperty;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class BelongsToAttribute : Attribute
{
    public string ForeignKeyProperty { get; }
    public BelongsToAttribute(string foreignKeyProperty) => ForeignKeyProperty = foreignKeyProperty;
}
