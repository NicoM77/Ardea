namespace Ardea.CommentCheck;

internal sealed record Finding(string Path, int Line, string Message, Range Removal);
