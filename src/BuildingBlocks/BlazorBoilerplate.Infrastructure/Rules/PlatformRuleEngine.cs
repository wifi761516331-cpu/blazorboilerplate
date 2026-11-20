using BlazorBoilerplate.Application.Rules;

namespace BlazorBoilerplate.Infrastructure.Rules;

/// <summary>通用规则引擎（支持自定义 IRuleEvaluator 扩展与 All/Any 组合求值）。</summary>
public sealed class PlatformRuleEngine : IRuleEngine
{
    private readonly Dictionary<string, IRuleEvaluator> _evaluators;

    public PlatformRuleEngine(IEnumerable<IRuleEvaluator>? customEvaluators = null)
    {
        _evaluators = new Dictionary<string, IRuleEvaluator>(StringComparer.OrdinalIgnoreCase);
        RegisterBuiltIns();
        if (customEvaluators is not null)
        {
            foreach (var eval in customEvaluators)
                _evaluators[eval.RuleKey] = eval;
        }
    }

    public IReadOnlyList<string> GetRegisteredKeys() =>
        _evaluators.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool Evaluate(RuleSet ruleSet, RuleContext context)
    {
        if (ruleSet.Conditions.Count == 0)
            return true;

        return ruleSet.Logic switch
        {
            RuleLogic.All => ruleSet.Conditions.All(c => EvaluateSingle(c, context)),
            RuleLogic.Any => ruleSet.Conditions.Any(c => EvaluateSingle(c, context)),
            _ => false
        };
    }

    private bool EvaluateSingle(RuleCondition condition, RuleContext context) =>
        _evaluators.TryGetValue(condition.RuleKey, out var evaluator) && evaluator.Evaluate(condition, context);

    private void RegisterBuiltIns()
    {
        var builtIns = new IRuleEvaluator[]
        {
            new AuthenticatedRuleEvaluator(),
            new RoleRuleEvaluator(),
            new TenantRuleEvaluator(),
            new CultureRuleEvaluator(),
            new UtcHourRuleEvaluator()
        };
        foreach (var eval in builtIns)
            _evaluators[eval.RuleKey] = eval;
    }

    internal static bool MatchString(string? actual, string op, string? expected)
    {
        var a = actual ?? string.Empty;
        var e = expected ?? string.Empty;
        return op.ToLowerInvariant() switch
        {
            "eq" or "==" or "equals" => string.Equals(a, e, StringComparison.OrdinalIgnoreCase),
            "neq" or "!=" => !string.Equals(a, e, StringComparison.OrdinalIgnoreCase),
            "contains" => a.Contains(e, StringComparison.OrdinalIgnoreCase),
            "in" => e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Any(item => string.Equals(item, a, StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private sealed class AuthenticatedRuleEvaluator : IRuleEvaluator
    {
        public string RuleKey => "user.authenticated";
        public bool Evaluate(RuleCondition condition, RuleContext context)
        {
            var isAuth = context.UserId.HasValue && context.UserId.Value != Guid.Empty;
            var expected = bool.TryParse(condition.Value, out var b) && b;
            return condition.Operator.Equals("neq", StringComparison.OrdinalIgnoreCase)
                ? isAuth != expected
                : isAuth == expected;
        }
    }

    private sealed class RoleRuleEvaluator : IRuleEvaluator
    {
        public string RuleKey => "user.role";
        public bool Evaluate(RuleCondition condition, RuleContext context)
        {
            var roles = context.Roles;
            if (roles is null || roles.Count == 0)
                return condition.Operator.Equals("neq", StringComparison.OrdinalIgnoreCase);

            var expected = (condition.Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return condition.Operator.ToLowerInvariant() switch
            {
                "neq" or "!=" => !expected.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)),
                "in" or "eq" or "==" or "contains" => expected.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)),
                _ => false
            };
        }
    }

    private sealed class TenantRuleEvaluator : IRuleEvaluator
    {
        public string RuleKey => "tenant.id";
        public bool Evaluate(RuleCondition condition, RuleContext context) =>
            MatchString(context.TenantId?.ToString(), condition.Operator, condition.Value);
    }

    private sealed class CultureRuleEvaluator : IRuleEvaluator
    {
        public string RuleKey => "culture";
        public bool Evaluate(RuleCondition condition, RuleContext context) =>
            MatchString(context.Culture, condition.Operator, condition.Value);
    }

    private sealed class UtcHourRuleEvaluator : IRuleEvaluator
    {
        public string RuleKey => "time.utc_hour";
        public bool Evaluate(RuleCondition condition, RuleContext context)
        {
            var hour = (context.NowUtc ?? DateTimeOffset.UtcNow).Hour;
            var parts = (condition.Value ?? string.Empty).Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end))
                return start <= end ? (hour >= start && hour <= end) : (hour >= start || hour <= end);
            return int.TryParse(condition.Value, out var exact) && hour == exact;
        }
    }
}

