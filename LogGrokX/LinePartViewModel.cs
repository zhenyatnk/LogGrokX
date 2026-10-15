using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using LogGrokX.Data;

namespace LogGrokX;

public class LinePartViewModel : ViewModelBase
{
    private readonly int _uniqueId;
    private readonly bool _followDetectionChanges;
    private readonly Func<FieldValueMapper>? _valueMapperResolver;
    // Keeps the handler alive: the static weak events hold only a WeakReference to the delegate.
    private readonly Action? _onDisplaySettingsChanged;
    private BinaryDetectionOptions _detection;
    private FieldValueMapper _valueMapper;
    private int _detectionVersion;
    private int _valueMapperVersion;
    private TextModel _originalTextModel;
    private const int HexDecodedFlag = (int)Base64Content.All + 1;

    private readonly TextModel?[] _decodedTextModels = new TextModel?[HexDecodedFlag * 2];
    private Base64Content? _content;
    private IReadOnlyList<StructuredSpan>? _structuredSpans;
    private Base64Content _decoded;
    private bool? _isHex;
    private bool _isHexDecoded;

    public LinePartViewModel(int uniqueId, string source, bool detectBase64 = true,
        BinaryDetectionOptions? detection = null, FieldValueMapper? valueMapper = null,
        Func<FieldValueMapper>? valueMapperResolver = null)
    {
        _uniqueId = uniqueId;
        _valueMapperResolver = valueMapperResolver;
        _valueMapper = valueMapperResolver?.Invoke() ?? valueMapper ?? FieldValueMapper.None;
        _valueMapperVersion = ValueMapper.CurrentVersion;
        _originalTextModel = new TextModel(uniqueId, ValueMappingText.Apply(source, _valueMapper));
        _followDetectionChanges = detectBase64 && detection == null;
        _detection = detectBase64 ? detection ?? BinaryDetectionOptions.Current : BinaryDetectionOptions.None;
        _detectionVersion = BinaryDetectionOptions.CurrentVersion;
        OriginalText = source;
        if (_followDetectionChanges || valueMapperResolver != null)
        {
            _onDisplaySettingsChanged = OnDisplaySettingsChanged;
            BinaryDetectionOptions.CurrentChanged += _onDisplaySettingsChanged;
            ValueMapper.CurrentChanged += _onDisplaySettingsChanged;
        }
    }

    public TextModel TextModel
    {
        get
        {
            RefreshSettingsIfNeeded();
            return IsDecoded
                ? _decodedTextModels[DecodedKey] ?? _originalTextModel
                : _originalTextModel;
        }
    }

    public string OriginalText { get; }

    public bool IsBase64
    {
        get
        {
            RefreshSettingsIfNeeded();
            return Content.HasFlag(Base64Content.Base64);
        }
    }

    public bool IsPem
    {
        get
        {
            RefreshSettingsIfNeeded();
            return Content.HasFlag(Base64Content.Pem);
        }
    }

    public bool IsHex
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _isHex ??= _detection.Hex && HexText.ContainsDecodableHex(OriginalText);
        }
    }

    public bool IsDecoded
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _decoded != Base64Content.None || _isHexDecoded;
        }
        set
        {
            RefreshSettingsIfNeeded();
            SetDecoded(Base64Content.Pem, value);
            SetDecoded(Base64Content.Base64, value);
            IsHexDecoded = value;
        }
    }

    public bool IsHexDecoded
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _isHexDecoded;
        }
        set
        {
            RefreshSettingsIfNeeded();
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
        get
        {
            RefreshSettingsIfNeeded();
            return _decoded.HasFlag(Base64Content.Base64);
        }
        set
        {
            RefreshSettingsIfNeeded();
            SetDecoded(Base64Content.Base64, value);
        }
    }

    public bool IsPemDecoded
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _decoded.HasFlag(Base64Content.Pem);
        }
        set
        {
            RefreshSettingsIfNeeded();
            SetDecoded(Base64Content.Pem, value);
        }
    }

    private Base64Content Content
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _content ??= _detection.Base64Kinds != Base64Content.None
                ? Base64Detector.Detect(OriginalText, StructuredSpans) & _detection.Base64Kinds
                : Base64Content.None;
        }
    }

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
        text = ValueMappingText.Apply(text, _valueMapper);

        _decodedTextModels[key] = new TextModel(
            HashCode.Combine(_uniqueId, nameof(Base64Content), key), text);
    }

    private void OnDisplaySettingsChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            RefreshSettingsIfNeeded();
            return;
        }

        try
        {
            dispatcher.BeginInvoke(RefreshSettingsIfNeeded);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is shutting down; the part refreshes lazily on next access.
        }
    }

    private void RefreshSettingsIfNeeded()
    {
        var detectionVersion = BinaryDetectionOptions.CurrentVersion;
        var valueMapperVersion = ValueMapper.CurrentVersion;
        var detectionChanged = _followDetectionChanges && detectionVersion != _detectionVersion;
        var valueMappingChanged = _valueMapperResolver != null && valueMapperVersion != _valueMapperVersion;
        if (!detectionChanged && !valueMappingChanged)
            return;

        _detectionVersion = detectionVersion;
        _valueMapperVersion = valueMapperVersion;
        if (detectionChanged)
            _detection = BinaryDetectionOptions.Current;
        if (valueMappingChanged)
            _valueMapper = _valueMapperResolver?.Invoke() ?? FieldValueMapper.None;
        if (detectionChanged)
        {
            _content = null;
            _structuredSpans = null;
            _isHex = null;
        }

        Array.Clear(_decodedTextModels);
        _originalTextModel = new TextModel(_uniqueId, ValueMappingText.Apply(OriginalText, _valueMapper));
        if (detectionChanged)
        {
            _decoded &= Content;
            _isHexDecoded = _isHexDecoded && IsHex;
        }

        InvokePropertyChanged(nameof(IsPem));
        InvokePropertyChanged(nameof(IsBase64));
        InvokePropertyChanged(nameof(IsHex));
        InvokePropertyChanged(nameof(IsDecoded));
        InvokePropertyChanged(nameof(IsPemDecoded));
        InvokePropertyChanged(nameof(IsBase64Decoded));
        InvokePropertyChanged(nameof(IsHexDecoded));
        InvokePropertyChanged(nameof(TextModel));
    }

    public override string ToString() => OriginalText;
}
