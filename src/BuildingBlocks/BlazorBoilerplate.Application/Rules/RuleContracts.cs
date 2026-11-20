namespace BlazorBoilerplate.Application.Rules;

/// <summary>规则组合逻辑。</summary>
public enum RuleLogic
{
    /// <summary>全部条件满足。</summary>
    All,

    /// <summary>任一条件满足。</summary>
    Any
}

/// <summary>单条规则条件。</summary>
public sealed record RuleCondition(string RuleKey, string Operator, string? Value);

/// <summary>规则集定义。</summary>
public sealed record RuleSet(string Name, RuleLogic Logic, IReadOnlyList<RuleCondition> Conditions);

/// <summary>规则求值上下文。</summary>
public sealed record RuleContext(
    Guid? UserId = null,
    string? UserName = null,
    Guid? TenantId = null,
    IReadOnlySet<string>? Roles = null,
    string? Culture = null,
    DateTimeOffset? NowUtc = null,
    IReadOnlyDictionary<string, object?>? Extra = null);

/// <summary>单类规则评估器。</summary>
public interface IRuleEvaluator
{
    /// <summary>规则键名（如 user.role / tenant.id / culture）。</summary>
    string RuleKey { get; }

    /// <summary>评估条件是否成立。</summary>
    bool Evaluate(RuleCondition condition, RuleContext context);
}

/// <summary>通用规则引擎接口。</summary>
public interface IRuleEngine
{
    /// <summary>评估整个规则集是否匹配。</summary>
    bool Evaluate(RuleSet ruleSet, RuleContext context);

    /// <summary>获取所有已注册的规则键。</summary>
    IReadOnlyList<string> GetRegisteredKeys();
}

