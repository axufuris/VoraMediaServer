using System.Linq.Expressions;

namespace Vora.Application.Libraries.ViewModels;

public static class ProjectionExtensions
{
    public static Expression<Func<TSource, TResult>> Assign<TSource, TResult, TValue>(
        this Expression<Func<TSource, TResult>> projection,
        Expression<Func<TResult, TValue>> member,
        Expression<Func<TSource, TValue>> value)
    {
        if (projection.Body is not MemberInitExpression init)
            throw new ArgumentException("The projection must build its result with an object initializer.", nameof(projection));
        if (member.Body is not MemberExpression { Member: var target })
            throw new ArgumentException("The member must be a property of the result.", nameof(member));

        var body = new ParameterSwap(value.Parameters[0], projection.Parameters[0]).Visit(value.Body);
        var bindings = init.Bindings
            .Where(binding => binding.Member.Name != target.Name)
            .Append(Expression.Bind(target, body));
        return Expression.Lambda<Func<TSource, TResult>>(Expression.MemberInit(init.NewExpression, bindings), projection.Parameters);
    }

    private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
