using System;
using System.Collections.Generic;
using System.Linq;
using LogGrokX.Data;

namespace LogGrokX;

public class LinePartViewModel : ViewModelBase
{
    private readonly int _uniqueId;
    private readonly bool _detectBase64;
    private readonly TextModel _originalTextModel;
    private const int HexDecodedFlag = (int)Base64Content.All + 1;

    private readonly TextModel?[] _decodedTextModels = new TextModel?[HexDecodedFlag * 2];
    private Base64Content? _content;
    private IReadOnlyList<StructuredSpan>? _structuredSpans;
    private Base64Content _decoded;
    private bool? _isHex;
    private bool _isHexDecoded;

    public LinePartViewModel(int uniqueId, string source, bool detectBase64 = true)
    {
        _uniqueId = uniqueId;
        _detectBase64 = detectBase64;
        _originalTextModel = new TextModel(uniqueId, source);
        OriginalText = source;
    }

    public TextModel TextModel => IsDecoded
        ? _decodedTextModels[DecodedKey] ?? _originalTextModel
        : _originalTextModel;

    public string OriginalText { get; }

    public bool IsBase64 => Content.HasFlag(Base64Content.Base64);

    public bool IsPem => Content.HasFlag(Base64Content.Pem);

    public bool IsHex => _isHex ??= _detectBase64 && HexText.ContainsDecodableHex(OriginalText);

    public bool IsDecoded
    {
        get => _decoded != Base64Content.None || _isHexDecoded;
        set
        {
            SetDecoded(Base64Content.Pem, value);
            SetDecoded(Base64Content.Base64, value);
            IsHexDecoded = value;
        }
    }

    public bool IsHexDecoded
    {
        get => _isHexDecoded;
        set
        {
            if (value && !IsHex)
                value = false;
            if (_isHexDecoded == value)
                return;

            _isHexDecoded = value;
            EnsureDecodedTextModel();
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(IsDecoded));
            InvokePropertyChanged(nameof(TextModel));
        }
    }

    public bool IsBase64Decoded
    {
        get => _decoded.HasFlag(Base64Content.Base64);
        set => SetDecoded(Base64Content.Base64, value);
    }

    public bool IsPemDecoded
    {
        get => _decoded.HasFlag(Base64Content.Pem);
        set => SetDecoded(Base64Content.Pem, value);
    }

    private Base64Content Content =>
        _content ??= _detectBase64 ? Base64Detector.Detect(OriginalText, StructuredSpans) : Base64Content.None;

    private IReadOnlyList<StructuredSpan> StructuredSpans =>
        _structuredSpans ??= TextOperations.GetStructuredRanges(OriginalText)
            .Select(r => new StructuredSpan(r.start, r.length, r.kind == StructuredTextKind.Xml))
            .ToList();

    private void SetDecoded(Base64Content kind, bool value)
    {
        if (value && !Content.HasFlag(kind))
            value = false;
        if (_decoded.HasFlag(kind) == value)
            return;

        _decoded = value ? _decoded | kind : _decoded & ~kind;
        EnsureDecodedTextModel();
        InvokePropertyChanged(kind == Base64Content.Pem ? nameof(IsPemDecoded) : nameof(IsBase64Decoded));
        InvokePropertyChanged(nameof(IsDecoded));
        InvokePropertyChanged(nameof(TextModel));
    }

    private int DecodedKey => (int)_decoded + (_isHexDecoded ? HexDecodedFlag : 0);

    private void EnsureDecodedTextModel()
    {
        var key = DecodedKey;
        if (key == 0 || _decodedTextModels[key] != null)
            return;

        var text = OriginalText;
        if (_decoded != Base64Content.None)
            Base64Detector.TryDecode(OriginalText, _decoded, out text, out _, StructuredSpans);
        if (_isHexDecoded && HexText.TryDecode(text, out var hexDecoded))
            text = hexDecoded;

        _decodedTextModels[key] = new TextModel(
            HashCode.Combine(_uniqueId, nameof(Base64Content), key), text);
    }

    public override string ToString() => OriginalText;
}
