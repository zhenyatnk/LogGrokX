using System;
using LogGrokX.Data;

namespace LogGrokX;

public class LinePartViewModel : ViewModelBase
{
    private readonly int _uniqueId;
    private readonly bool _detectBase64;
    private readonly TextModel _originalTextModel;
    private TextModel? _decodedTextModel;
    private string? _decodedText;
    private bool? _isBase64;
    private bool _isBase64Decoded;

    public LinePartViewModel(int uniqueId, string source, bool detectBase64 = true)
    {
        _uniqueId = uniqueId;
        _detectBase64 = detectBase64;
        _originalTextModel = new TextModel(uniqueId, source);
        OriginalText = source;
    }

    public TextModel TextModel => _isBase64Decoded && _decodedTextModel is { } decoded
        ? decoded
        : _originalTextModel;

    public string OriginalText { get; }

    public bool IsBase64
    {
        get
        {
            if (_isBase64 is { } known)
                return known;

            string? decoded = null;
            var isBase64 = _detectBase64 && Base64Detector.TryDecode(OriginalText, out decoded);
            _decodedText = isBase64 ? decoded : null;
            _isBase64 = isBase64;
            return isBase64;
        }
    }

    public bool IsBase64Decoded
    {
        get => _isBase64Decoded;
        set
        {
            if (value && !IsBase64)
                value = false;
            if (_isBase64Decoded == value)
                return;

            if (value && _decodedText is { } decodedText)
                _decodedTextModel ??= new TextModel(HashCode.Combine(_uniqueId, nameof(IsBase64Decoded)), decodedText);

            _isBase64Decoded = value;
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(TextModel));
        }
    }

    public override string ToString() => OriginalText;
}
