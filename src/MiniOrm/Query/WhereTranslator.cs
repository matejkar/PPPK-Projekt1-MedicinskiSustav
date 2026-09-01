using System.Linq.Expressions;
using System.Text;
using MiniOrm.Mapping;
using Npgsql;

namespace MiniOrm.Query;

internal sealed class WhereResult
{
    public required string Sql { get; init; }
    public required IReadOnlyList<NpgsqlParameter> Parameters { get; init; }
}

internal static class WhereTranslator
{
    public static WhereResult Translate(EntityMetadata metadata, IEnumerable<LambdaExpression> predicates)
    {
        var parts = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        foreach (var predicate in predicates)
        {
            var visitor = new Visitor(metadata, parameters);
            visitor.Visit(predicate.Body);
            if (visitor.Sql.Length > 0)
                parts.Add(visitor.Sql.ToString());
        }

        return new WhereResult
        {
            Sql = parts.Count == 0 ? "TRUE" : string.Join(" AND ", parts),
            Parameters = parameters
        };
    }

    public static string TranslateOrder<T, TKey>(EntityMetadata metadata, Expression<Func<T, TKey>> keySelector, bool desc)
    {
        var column = ResolveColumn(metadata, keySelector.Body);
        return $"{Quote(column)} {(desc ? "DESC" : "ASC")}";
    }

    internal static string Quote(string ident) => $"\"{ident.Replace("\"", "\"\"")}\"";

    private static string ResolveColumn(EntityMetadata metadata, Expression body)
    {
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
            body = u.Operand;
        if (body is MemberExpression member)
            return metadata.ColumnByProperty(member.Member.Name).ColumnName;
        throw new NotSupportedException("ORDER BY podržava samo jednostavna svojstva entiteta.");
    }

    private sealed class Visitor : ExpressionVisitor
    {
        private readonly EntityMetadata _metadata;
        private readonly List<NpgsqlParameter> _parameters;
        public readonly StringBuilder Sql = new();

        public Visitor(EntityMetadata metadata, List<NpgsqlParameter> parameters)
        {
            _metadata = metadata;
            _parameters = parameters;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (IsNullCompare(node, out var member, out var isEqual))
            {
                var col = _metadata.ColumnByProperty(member.Member.Name).ColumnName;
                Sql.Append(Quote(col));
                Sql.Append(isEqual ? " IS NULL" : " IS NOT NULL");
                return node;
            }

            Sql.Append('(');
            Visit(node.Left);
            Sql.Append(node.NodeType switch
            {
                ExpressionType.Equal => " = ",
                ExpressionType.NotEqual => " <> ",
                ExpressionType.GreaterThan => " > ",
                ExpressionType.GreaterThanOrEqual => " >= ",
                ExpressionType.LessThan => " < ",
                ExpressionType.LessThanOrEqual => " <= ",
                ExpressionType.AndAlso => " AND ",
                ExpressionType.OrElse => " OR ",
                ExpressionType.Add => " + ",
                ExpressionType.Subtract => " - ",
                _ => throw new NotSupportedException($"Operator {node.NodeType} nije podržan.")
            });
            Visit(node.Right);
            Sql.Append(')');
            return node;
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
            {
                Sql.Append("NOT (");
                Visit(node.Operand);
                Sql.Append(')');
                return node;
            }

            if (node.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
                return Visit(node.Operand)!;

            return base.VisitUnary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ParameterExpression)
            {
                var col = _metadata.ColumnByProperty(node.Member.Name).ColumnName;
                Sql.Append(Quote(col));
                return node;
            }

            AppendParameter(Evaluate(node));
            return node;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            AppendParameter(node.Value);
            return node;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(string))
            {
                var columnExpr = node.Object ?? throw new NotSupportedException();
                var arg = node.Arguments[0];

                Visit(columnExpr);
                var patternParam = AddParameter(Evaluate(arg));

                Sql.Append(node.Method.Name switch
                {
                    nameof(string.Contains) => $" LIKE '%' || {patternParam} || '%'",
                    nameof(string.StartsWith) => $" LIKE {patternParam} || '%'",
                    nameof(string.EndsWith) => $" LIKE '%' || {patternParam}",
                    nameof(string.Equals) => $" = {patternParam}",
                    _ => throw new NotSupportedException($"Metoda string.{node.Method.Name} nije podržana u filteru.")
                });
                return node;
            }

            AppendParameter(Evaluate(node));
            return node;
        }

        private void AppendParameter(object? value)
        {
            Sql.Append(AddParameter(value));
        }

        private string AddParameter(object? value)
        {
            var name = $"@p{_parameters.Count}";
            _parameters.Add(new NpgsqlParameter(name, TypeMapper.ToDb(value)));
            return name;
        }

        private static object? Evaluate(Expression expression)
        {
            if (expression is ConstantExpression c)
                return c.Value;
            var lambda = Expression.Lambda(expression);
            return lambda.Compile().DynamicInvoke();
        }

        private static bool IsNullCompare(BinaryExpression node, out MemberExpression member, out bool isEqual)
        {
            member = null!;
            isEqual = node.NodeType == ExpressionType.Equal;
            if (node.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
                return false;

            if (node.Left is MemberExpression left && IsNull(node.Right) && left.Expression is ParameterExpression)
            {
                member = left;
                return true;
            }

            if (node.Right is MemberExpression right && IsNull(node.Left) && right.Expression is ParameterExpression)
            {
                member = right;
                return true;
            }

            return false;
        }

        private static bool IsNull(Expression expression) =>
            expression is ConstantExpression { Value: null };
    }
}
