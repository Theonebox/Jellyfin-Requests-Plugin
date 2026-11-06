namespace Jellyfin.Plugin.Requests.Services;

public sealed class DuplicateRequestException : InvalidOperationException
{
    public DuplicateRequestException(string message)
        : base(message)
    {
    }
}
