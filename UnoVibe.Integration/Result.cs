using System.Net;

namespace UnoVibe.Integration;

/// <summary>
/// Error returned by the opencode server API. Carries the HTTP status code (0 for
/// network-level failures) and a human-readable message suitable for display.
/// </summary>
public readonly record struct ApiError(HttpStatusCode StatusCode, string Message)
{
    public static ApiError Network(string message) => new(0, message);
    public static ApiError Http(HttpStatusCode statusCode, string message) => new(statusCode, message);

    public override string ToString() => DisplayMessage;
    public string DisplayMessage => $"{(int)StatusCode}: {Message}";
}

/// <summary>
/// Discriminated result of an API call: either a successful <see cref="Value"/> or an
/// <see cref="Error"/>. Callers must explicitly handle both paths via
/// <see cref="GetOrThrow"/> or <see cref="TryGetValue"/>.
/// </summary>
public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly ApiError _error;

    private Result(T? value, ApiError error, bool isSuccess)
    {
        _value = value;
        _error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>When <see cref="IsSuccess"/> is <c>false</c>, holds the error details.</summary>
    public ApiError Error => !IsSuccess ? _error : throw new InvalidOperationException("Cannot get error when operation is successul");

    /// <summary><c>true</c> when the API call succeeded and <see cref="Value"/> is safe to read.</summary>
    public bool IsSuccess { get; }

    /// <summary>Returns the value on success; throws <see cref="HttpRequestException"/> on failure.</summary>
    public T GetOrThrow()
    {
        if (IsSuccess) return _value!;
        throw new HttpRequestException(
            $"API error {(Error.StatusCode > 0 ? $"({Error.StatusCode})" : "")}: {Error.Message}");
    }

    /// <summary>Returns the value on success; returns input value on failure.</summary>
    public T GetOr(T valueOnFailure)
    {
        if (IsSuccess) return _value!;
        return valueOnFailure;
    }

    /// <summary>Returns the value on success; calls input delegate and return value on failure.</summary>
    public T GetOr(Func<T> valueCreatorOnFailure)
    {
        if (IsSuccess) return _value!;
        return valueCreatorOnFailure();
    }

    /// <summary>Returns the value on success; returns default(T) on failure.</summary>
    public T? GetOrDefault()
    {
        if (IsSuccess) return _value;
        return default;
    }

    /// <summary>
    /// Attempts to extract the value. Returns <c>true</c> when the call succeeded and
    /// <paramref name="value"/> is populated; returns <c>false</c> on failure.
    /// </summary>
    public bool TryGetValue(out T value)
    {
        if (IsSuccess) { value = _value!; return true; }
        value = default!;
        return false;
    }
    /// <summary>
    /// Attempts to extract the value. Returns <c>true</c> when the call succeeded and
    /// <paramref name="value"/> is populated; returns <c>false</c> on failure.
    /// </summary>
    public bool TryGetValue(out T value, out ApiError error)
    {
        if (IsSuccess) {
            value = _value!;
            error = default;
            return true;
        }
        value = default!;
        error = _error;
        return false;
    }

    public static Result<T> Success(T value) => new(value, default, true);
    public static Result<T> Failure(ApiError error) => new(default, error, false);
}
