using System.Net;

namespace BgArena_Blazor.Services;

/// <summary>
/// The outcome of an <see cref="ArenaClient"/> call that has
/// contract-documented refusal responses: either the deserialized payload, or
/// the refusing status code with the server's reason. Only documented
/// refusals are folded in here — an undocumented status throws at the client
/// boundary instead (fail loud, per the producer contract).
/// </summary>
/// <typeparam name="T">The success payload shape (a <c>BgTournament.Api</c> record).</typeparam>
public sealed record ArenaResult<T>
{
    private readonly T? _value;

    private ArenaResult(T? value, bool isSuccess, HttpStatusCode statusCode, string? error)
    {
        _value = value;
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        Error = error;
    }

    /// <summary>True when the call succeeded and <see cref="Value"/> is available.</summary>
    public bool IsSuccess { get; }

    /// <summary>The HTTP status the server answered with.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// The server's refusal reason (<c>ErrorResponse.error</c>) when the
    /// refusal carried one; null on success and on bodyless refusals
    /// (404 from a by-id GET).
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// The deserialized payload. Throws <see cref="InvalidOperationException"/>
    /// when <see cref="IsSuccess"/> is false — check first.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"The call was refused ({(int)StatusCode}): {Error ?? "no detail"}.");

    /// <summary>Wraps a successful payload.</summary>
    public static ArenaResult<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new ArenaResult<T>(value, isSuccess: true, HttpStatusCode.OK, error: null);
    }

    /// <summary>Wraps a contract-documented refusal.</summary>
    public static ArenaResult<T> Refused(HttpStatusCode statusCode, string? error) =>
        new(default, isSuccess: false, statusCode, error);
}
