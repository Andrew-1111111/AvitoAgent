namespace AvitoAgent.Core.Models;

public sealed class ListingPhoto
{
    public int SortOrder { get; init; }

    public byte[] Content { get; init; } = [];

    public string ContentType { get; init; } = string.Empty;
}
