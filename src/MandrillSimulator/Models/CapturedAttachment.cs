namespace MandrillSimulator.Models;

public class CapturedAttachment
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public byte[] Content { get; init; } = [];
    public bool IsEmbeddedImage { get; init; }

    public int SizeBytes => Content.Length;
}
