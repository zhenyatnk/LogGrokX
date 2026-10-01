using System;

namespace LogGrokX;

public class LinePartViewModel : ViewModelBase
{
    private readonly int _uniqueId;
    private readonly TextModel _sourceTextModel;
    private bool? _isHexDetected;
    private string? _hexDecodedText;
    private TextModel? _hexDecodedTextModel;
    private bool _isHexDecoded;

    public LinePartViewModel(int uniqueId, string source, bool detectHex = true)
    {
        _uniqueId = uniqueId;
        _sourceTextModel = new TextModel(uniqueId, source);
        OriginalText = source;
        if (!detectHex)
            _isHexDetected = false;
    }

    public TextModel TextModel => IsHexDecoded && GetHexDecodedTextModel() is { } decoded
        ? decoded
        : _sourceTextModel;

    public string OriginalText { get; }

    public bool IsHexDetected => _isHexDetected ??= DetectHex();

    public bool IsHexDecoded
    {
        get => _isHexDecoded;
        set
        {
            if (value && !IsHexDetected)
                return;

            if (_isHexDecoded == value)
                return;

            _isHexDecoded = value;
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(TextModel));
        }
    }

    private TextModel? GetHexDecodedTextModel()
    {
        if (_hexDecodedTextModel != null)
            return _hexDecodedTextModel;

        if (!IsHexDetected || _hexDecodedText is not { } decoded)
            return null;

        _hexDecodedTextModel = new TextModel(HashCode.Combine(_uniqueId, nameof(HexText)), decoded);
        return _hexDecodedTextModel;
    }

    private bool DetectHex()
    {
        if (!HexText.TryDecode(OriginalText, out var decoded))
            return false;

        _hexDecodedText = decoded;
        return true;
    }

    public override string ToString() => OriginalText;
};
