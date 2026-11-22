using System.Diagnostics.CodeAnalysis;

namespace Catalyst.Common.Models;

/// <summary>Either a value or the <see cref="Issue"/> that explains why there is none.</summary>
internal sealed class Outcome<T>
{
    private readonly T? _value;
    private readonly Issue? _problem;

    private Outcome(T? value, Issue? problem)
    {
        _value = value;
        _problem = problem;
    }

    public static Outcome<T> Ok(T value) => new(value, null);

    public static Outcome<T> Fail(Issue problem) => new(default, problem);

    public static Outcome<T> Fail(string code, string message) => Fail(new Issue(code, message));

    public bool TryGet([MaybeNullWhen(false)] out T value, [NotNullWhen(false)] out Issue? problem)
    {
        value = _value;
        problem = _problem;
        return _problem is null;
    }
}
