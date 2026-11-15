using System.Globalization;

namespace Usa.Simulation.Ports;

public enum StateValueKind
{
    None,
    Number,
    Bool,
    Text,
}

/// <summary>A dynamically typed value read from simulation state or written by an effect.</summary>
public readonly struct StateValue : IEquatable<StateValue>
{
    private readonly double _number;
    private readonly string? _text;

    private StateValue(StateValueKind kind, double number, string? text)
    {
        Kind = kind;
        _number = number;
        _text = text;
    }

    public static readonly StateValue None;

    public StateValueKind Kind { get; }

    public static StateValue Of(double value) => new(StateValueKind.Number, value, null);

    public static StateValue Of(bool value) => new(StateValueKind.Bool, value ? 1 : 0, null);

    public static StateValue Of(string value) => new(StateValueKind.Text, 0, value ?? throw new ArgumentNullException(nameof(value)));

    public static implicit operator StateValue(double value) => Of(value);

    public static implicit operator StateValue(bool value) => Of(value);

    public static implicit operator StateValue(string value) => Of(value);

    public double AsNumber() => Kind switch
    {
        StateValueKind.Number => _number,
        StateValueKind.Bool => _number,
        _ => throw new InvalidOperationException($"State value {this} is not a number."),
    };

    public bool AsBool() => Kind switch
    {
        StateValueKind.Bool => _number != 0,
        StateValueKind.Number => _number != 0,
        _ => throw new InvalidOperationException($"State value {this} is not a boolean."),
    };

    public string AsText() => Kind == StateValueKind.Text
        ? _text!
        : throw new InvalidOperationException($"State value {this} is not text.");

    public bool Equals(StateValue other) => Kind == other.Kind && Kind switch
    {
        StateValueKind.None => true,
        StateValueKind.Text => string.Equals(_text, other._text, StringComparison.Ordinal),
        _ => _number.Equals(other._number),
    };

    public override bool Equals(object? obj) => obj is StateValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _number, _text);

    public static bool operator ==(StateValue left, StateValue right) => left.Equals(right);

    public static bool operator !=(StateValue left, StateValue right) => !left.Equals(right);

    public override string ToString() => Kind switch
    {
        StateValueKind.Number => _number.ToString("R", CultureInfo.InvariantCulture),
        StateValueKind.Bool => _number != 0 ? "true" : "false",
        StateValueKind.Text => $"'{_text}'",
        _ => "none",
    };
}
