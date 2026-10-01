using LogGrokX.Data;

namespace LogGrokX;

public sealed record BinaryDetectionOptions(bool Pem, bool Base64, bool Hex)
{
    public static BinaryDetectionOptions All { get; } = new(true, true, true);

    public static BinaryDetectionOptions None { get; } = new(false, false, false);

    public static BinaryDetectionOptions Current { get; set; } = All;

    public Base64Content Base64Kinds =>
        (Pem ? Base64Content.Pem : Base64Content.None) | (Base64 ? Base64Content.Base64 : Base64Content.None);

    public static BinaryDetectionOptions FromSettings(ViewSettings settings) =>
        settings.DetectBinary
            ? new BinaryDetectionOptions(settings.DetectPem, settings.DetectBase64, settings.DetectHex)
            : None;
}
