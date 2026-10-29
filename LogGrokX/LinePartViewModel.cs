using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using LogGrokX.Data;
using LogGrokX.Data.Symbols;
using LogGrokX.Symbols;

namespace LogGrokX;

public class LinePartViewModel : ViewModelBase
{
    private readonly int _uniqueId;
    private readonly bool _followDetectionChanges;
    private readonly Func<FieldValueMapper>? _valueMapperResolver;
    private readonly DecodedPartKey? _decodedState;
    private int _decodedStateVersion = -1;
    private BinaryDetectionOptions _detection;
    private FieldValueMapper _valueMapper;
    private int _detectionVersion;
    private int _valueMapperVersion;
    private TextModel _originalTextModel;
    private const int HexDecodedFlag = (int)Base64Content.All + 1;

    private readonly TextModel?[] _decodedTextModels = new TextModel?[HexDecodedFlag * 2];
    private readonly string?[] _decodedTexts = new string?[HexDecodedFlag * 2];
    private readonly DisplaySettingsMonitor _displaySettings;
    private readonly CallStackSymbolService? _symbols;
    private readonly bool _detectCallStack;
    private int _symbolOptionsVersion;
    private int _symbolsVersion;
    private bool? _isCallStack;
    private bool _isSymbolsResolved;
    private TextModel? _symbolsTextModel;
    private int _symbolsTextKey = -1;
    private bool _waitingForSymbols;
    private ICommand? _showCallStackCommand;
    private Base64Content? _content;
    private IReadOnlyList<StructuredSpan>? _structuredSpans;
    private Base64Content _decoded;
    private bool? _isHex;
    private bool _isHexDecoded;

    public LinePartViewModel(int uniqueId, string source, DisplaySettingsMonitor displaySettings, bool detectBase64 = true,
        BinaryDetectionOptions? detection = null, FieldValueMapper? valueMapper = null,
        Func<FieldValueMapper>? valueMapperResolver = null, DecodedPartKey? decodedState = null)
    {
        ArgumentNullException.ThrowIfNull(displaySettings);
        _uniqueId = uniqueId;
        _displaySettings = displaySettings;
        _symbols = detectBase64 ? displaySettings.Symbols : null;
        _detectCallStack = _symbols != null;
        _symbolOptionsVersion = SymbolOptions.CurrentVersion;
        _symbolsVersion = _symbols?.Version ?? 0;
        _decodedState = decodedState;
        _valueMapperResolver = valueMapperResolver;
        _valueMapper = valueMapperResolver?.Invoke() ?? valueMapper ?? FieldValueMapper.None;
        _valueMapperVersion = ValueMapper.CurrentVersion;
        _originalTextModel = new TextModel(uniqueId, ValueMappingText.Apply(source, _valueMapper));
        _followDetectionChanges = detectBase64 && detection == null;
        _detection = detectBase64 ? detection ?? BinaryDetectionOptions.Current : BinaryDetectionOptions.None;
        _detectionVersion = BinaryDetectionOptions.CurrentVersion;
        OriginalText = source;
        if (_followDetectionChanges || valueMapperResolver != null || _detectCallStack)
        {
            displaySettings.Register(this);
        }
    }

    public TextModel TextModel
    {
        get
        {
            RefreshSettingsIfNeeded();
            var baseModel = _originalTextModel;
            if (IsDecoded)
            {
                EnsureDecodedTextModel();
                baseModel = _decodedTextModels[DecodedKey] ?? _originalTextModel;
            }

            return _isSymbolsResolved ? GetSymbolsTextModel(baseModel) : baseModel;
        }
    }

    public bool HasAlternateText => IsDecoded || IsSymbolsResolved;

    public bool IsCallStack
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _isCallStack ??= _detectCallStack && SymbolOptions.Current.Enabled &&
                                    CallStackParser.ContainsCallStack(OriginalText);
        }
    }

    public bool IsSymbolsResolved
    {
        get
        {
            RefreshSettingsIfNeeded();
            return _isSymbolsResolved;
        }
        set
        {
            RefreshSettingsIfNeeded();
            if (value && !IsCallStack)
                value = false;
            if (_isSymbolsResolved == value)
                return;

            _isSymbolsResolved = value;
            SaveDecodedState();
            InvokePropertyChanged();
            InvokePropertyChanged(nameof(HasAlternateText));
            InvokePropertyChanged(nameof(TextModel));
        }
    }

    public ICommand ShowCallStackCommand => _showCallStackCommand ??=
        new DelegateCommand(() => _symbols?.ShowCallStack(GetBaseText()));

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
            SaveDecodedState();
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
        SaveDecodedState();
        EnsureDecodedTextModel();
        InvokePropertyChanged(kind == Base64Content.Pem ? nameof(IsPemDecoded) : nameof(IsBase64Decoded));
        InvokePropertyChanged(nameof(IsDecoded));
        InvokePropertyChanged(nameof(TextModel));
    }

    private void SaveDecodedState()
    {
        if (_decodedState is not { } key)
            return;

        key.Set(new DecodedPartState(_decoded, _isHexDecoded, _isSymbolsResolved));
        _decodedStateVersion = key.State.Version;
    }

    // Another view model of the same line (re-created by virtualization or a collection reset) may have
    // changed the shared state, so it is re-read whenever the state version changes (#78).
    private void SyncDecodedState()
    {
        if (_decodedState is not { } key || key.State.Version == _decodedStateVersion)
            return;

        _decodedStateVersion = key.State.Version;
        var state = key.Get();
        var decoded = state.Base64 & Content;
        var isHexDecoded = state.Hex && IsHex;
        var isSymbolsResolved = state.Symbols && IsCallStack;
        var changedKinds = decoded ^ _decoded;
        var hexChanged = isHexDecoded != _isHexDecoded;
        var symbolsChanged = isSymbolsResolved != _isSymbolsResolved;
        if (changedKinds == Base64Content.None && !hexChanged && !symbolsChanged)
            return;

        _decoded = decoded;
        _isHexDecoded = isHexDecoded;
        _isSymbolsResolved = isSymbolsResolved;
        if (changedKinds.HasFlag(Base64Content.Pem))
            InvokePropertyChanged(nameof(IsPemDecoded));
        if (changedKinds.HasFlag(Base64Content.Base64))
            InvokePropertyChanged(nameof(IsBase64Decoded));
        if (hexChanged)
            InvokePropertyChanged(nameof(IsHexDecoded));
        if (symbolsChanged)
            InvokePropertyChanged(nameof(IsSymbolsResolved));
        InvokePropertyChanged(nameof(IsDecoded));
        InvokePropertyChanged(nameof(HasAlternateText));
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

        _decodedTexts[key] = text;
        _decodedTextModels[key] = new TextModel(
            HashCode.Combine(_uniqueId, nameof(Base64Content), key), text);
    }

    internal void RefreshSettingsIfNeeded()
    {
        RefreshDisplaySettingsIfNeeded();
        RefreshSymbolsIfNeeded();
        SyncDecodedState();
    }

    private string GetBaseText()
    {
        if (!IsDecoded)
            return ValueMappingText.Apply(OriginalText, _valueMapper);

        EnsureDecodedTextModel();
        return _decodedTexts[DecodedKey] ?? OriginalText;
    }

    private TextModel GetSymbolsTextModel(TextModel baseModel)
    {
        if (_symbols == null)
            return baseModel;

        var key = DecodedKey;
        if (_symbolsTextModel != null && _symbolsTextKey == key)
            return _symbolsTextModel;

        var frames = CallStackParser.Parse(GetBaseText());
        var text = AnnotateCallStack(GetBaseText(), frames, out var pending);
        _symbolsTextKey = key;
        _symbolsTextModel = new TextModel(HashCode.Combine(_uniqueId, nameof(IsSymbolsResolved), key), text);
        if (pending && !_waitingForSymbols)
        {
            _waitingForSymbols = true;
            var version = _symbols.Version;
            _symbols.ResolveAllAsync(frames).ContinueWith(_ => _displaySettings.Post(() =>
            {
                _waitingForSymbols = false;
                if (version != _symbols.Version)
                    return;
                _symbolsTextModel = null;
                if (_isSymbolsResolved)
                    InvokePropertyChanged(nameof(TextModel));
            }), TaskScheduler.Default);
        }

        return _symbolsTextModel;
    }

    private string AnnotateCallStack(string text, IReadOnlyList<CallStackFrame> frames, out bool pending)
    {
        pending = false;
        var builder = new StringBuilder(text.Length * 2);
        var position = 0;
        foreach (var frame in frames)
        {
            builder.Append(text, position, frame.Start - position);
            var original = text.Substring(frame.Start, frame.Length);
            var symbol = _symbols!.Get(frame);
            switch (symbol.Status)
            {
                case FrameSymbolStatus.Pending:
                    pending = true;
                    builder.Append(original).Append("  ").Append(Localization.Strings.Get("ResolvingSymbols"));
                    break;
                case FrameSymbolStatus.Resolved:
                    builder.Append(frame.Number).Append("# ").Append(symbol.FormatFrame(frame));
                    break;
                case FrameSymbolStatus.NoSymbols:
                    builder.Append(original).Append("  (").Append(Localization.Strings.Get("SymbolsNotFound")).Append(')');
                    break;
                default:
                    builder.Append(original).Append("  (").Append(Localization.Strings.Get("SymbolNotResolved")).Append(')');
                    break;
            }

            position = frame.Start + frame.Length;
        }

        builder.Append(text, position, text.Length - position);
        return builder.ToString();
    }

    private void RefreshSymbolsIfNeeded()
    {
        if (!_detectCallStack)
            return;

        var optionsVersion = SymbolOptions.CurrentVersion;
        var symbolsVersion = _symbols!.Version;
        if (optionsVersion == _symbolOptionsVersion && symbolsVersion == _symbolsVersion)
            return;

        _symbolOptionsVersion = optionsVersion;
        _symbolsVersion = symbolsVersion;
        _isCallStack = null;
        _symbolsTextModel = null;
        if (_isSymbolsResolved && !IsCallStack)
        {
            _isSymbolsResolved = false;
            SaveDecodedState();
        }

        InvokePropertyChanged(nameof(IsCallStack));
        InvokePropertyChanged(nameof(IsSymbolsResolved));
        InvokePropertyChanged(nameof(HasAlternateText));
        InvokePropertyChanged(nameof(TextModel));
    }

    private void RefreshDisplaySettingsIfNeeded()
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
        Array.Clear(_decodedTexts);
        _symbolsTextModel = null;
        _originalTextModel = new TextModel(_uniqueId, ValueMappingText.Apply(OriginalText, _valueMapper));
        if (detectionChanged)
        {
            _decoded &= Content;
            _isHexDecoded = _isHexDecoded && IsHex;
            SaveDecodedState();
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
