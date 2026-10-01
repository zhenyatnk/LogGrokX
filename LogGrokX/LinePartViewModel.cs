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
    private readonly TextModel?[] _decodedTextModels = new TextModel?[(int)Base64Content.All + 1];
    private Base64Content? _content;
    private IReadOnlyList<StructuredSpan>? _structuredSpans;
    private Base64Content _decoded;

    public LinePartViewModel(int uniqueId, string source, bool detectBase64 = true)
    {
        _uniqueId = uniqueId;
        _detectBase64 = detectBase64;
        _originalTextModel = new TextModel(uniqueId, source);
        OriginalText = source;
    }

    public TextModel TextModel => _decoded == Base64Content.None
        ? _originalTextModel
        : _decodedTextModels[(int)_decoded] ?? _originalTextModel;

    public string OriginalText { get; }

    public bool IsBase64 => Content.HasFlag(Base64Content.Base64);

    public bool IsPem => Content.HasFlag(Base64Content.Pem);

    public bool IsDecoded
    {
        get => _decoded != Base64Content.None;
        set
        {
            SetDecoded(Base64Content.Pem, value);
            SetDecoded(Base64Content.Base64, value);
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

        var decoded = value ? _decoded | kind : _decoded & ~kind;
        if (decoded != Base64Content.None && _decodedTextModels[(int)decoded] == null)
        {
            Base64Detector.TryDecode(OriginalText, decoded, out var text, out _, StructuredSpans);
            _decodedTextModels[(int)decoded] = new TextModel(
                HashCode.Combine(_uniqueId, nameof(Base64Content), (int)decoded), text);
        }

        _decoded = decoded;
        InvokePropertyChanged(kind == Base64Content.Pem ? nameof(IsPemDecoded) : nameof(IsBase64Decoded));
        InvokePropertyChanged(nameof(IsDecoded));
        InvokePropertyChanged(nameof(TextModel));
    }

    public override string ToString() => OriginalText;
}
