using System.Globalization;

namespace TaikoMapper.Cli.Support;

/// <summary>
/// A small, value-option-aware command-line reader. Given the set of options that
/// take a value, it classifies each token into positionals, <c>--key value</c> /
/// <c>--key=value</c> options, and bare <c>--flag</c> switches — so a flag before a
/// positional never swallows it.
/// </summary>
internal sealed class ArgParser
{
    private readonly List<string> _positionals = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    /// <param name="args">The command's arguments (after the verb).</param>
    /// <param name="valueOptions">Option names (e.g. "--difficulty") that consume a following value.</param>
    public ArgParser(string[] args, IReadOnlySet<string> valueOptions)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                _positionals.Add(token);
                continue;
            }

            var eq = token.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                _options[token[..eq]] = token[(eq + 1)..];
                continue;
            }

            if (valueOptions.Contains(token) && i + 1 < args.Length)
            {
                _options[token] = args[++i];
                continue;
            }

            _flags.Add(token);
        }
    }

    public string? FirstPositional() => _positionals.Count > 0 ? _positionals[0] : null;

    public IReadOnlyList<string> Positionals => _positionals;

    public bool GetFlag(string name) => _flags.Contains(name) || _options.ContainsKey(name);

    public string? GetString(string name) => _options.TryGetValue(name, out var value) ? value : null;

    public double? GetDouble(string name)
    {
        var raw = GetString(name);
        if (raw is null)
            return null;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"option {name} expects a number but got '{raw}'.");

        return value;
    }

    public int? GetInt(string name)
    {
        var raw = GetString(name);
        if (raw is null)
            return null;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"option {name} expects an integer but got '{raw}'.");

        return value;
    }
}
