using AudioYotoShelf.Core.Enums;

namespace AudioYotoShelf.Core.DTOs.Icons;

public record GenerateIconRequest(
    string Title,
    string? BookTitle,
    string? Genre,
    bool IsBookCover = false
);

public record IconResponse(
    Guid Id,
    string Url,
    IconSource Source,
    string Prompt,
    byte[]? IconData
);

public record PublicIconSearchRequest(
    string Query,
    int MaxResults = 10
);
