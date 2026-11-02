namespace Chum.App.Models;

public enum ResponseModeType
{
    Default = 1,
    RagOnly = 2,
    Detailed = 3,
    Coding = 4
}

public sealed record QaTurn(
    string Question,
    string Answer,
    string Category,
    ResponseModeType Mode,
    DateTimeOffset Timestamp
);
