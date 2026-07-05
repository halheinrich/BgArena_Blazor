namespace BgArena_Blazor.Services;

/// <summary>
/// A downloaded match export: the file bytes together with the content type and
/// download filename the tournament server served them under. Unlike the other
/// <see cref="ArenaResult{T}"/> payloads this is not a <c>BgTournament.Api</c>
/// JSON shape but an opaque file — a terminal match's Jellyfish <c>.MAT</c>
/// transcript — captured verbatim so the Arena relay can re-emit it unchanged.
/// </summary>
/// <param name="Content">The exported file's raw bytes.</param>
/// <param name="ContentType">
/// The served <c>Content-Type</c> header value (media type and any parameters,
/// e.g. <c>text/plain; charset=utf-8</c>); null if the server sent none.
/// </param>
/// <param name="FileName">
/// The download filename from the served <c>Content-Disposition</c> (e.g.
/// <c>match_{id}.mat</c>); null if the server sent no filename.
/// </param>
public sealed record MatchExportFile(byte[] Content, string? ContentType, string? FileName);
