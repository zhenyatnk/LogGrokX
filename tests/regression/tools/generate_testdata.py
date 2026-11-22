#!/usr/bin/env python3
"""Генератор статических тестовых данных регресс-набора LogGrokX.

Запуск (из корня репозитория):  python tests/regression/tools/generate_testdata.py
Результат пишется в tests/regression/testdata. Файлы детерминированы, кроме
криптографического материала (ключ RSA создаётся заново): его ожидаемые значения
(серийный номер, отпечаток и т.д.) записываются в testdata/binary/EXPECTED.md.
Требуется пакет `cryptography`.
"""
from __future__ import annotations

import base64
import datetime as dt
import hashlib
import json
import os
import struct
from pathlib import Path

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

ROOT = Path(__file__).resolve().parents[1] / "testdata"


def write(rel: str, lines: list[str] | str, newline: str = "\r\n", encoding: str = "utf-8",
          bom: bool = False, trailing: bool = True) -> None:
    path = ROOT / rel
    path.parent.mkdir(parents=True, exist_ok=True)
    text = lines if isinstance(lines, str) else newline.join(lines) + (newline if trailing else "")
    data = text.encode(encoding)
    if bom and encoding == "utf-8":
        data = b"\xef\xbb\xbf" + data
    path.write_bytes(data)


def b64(s: str | bytes) -> str:
    return base64.b64encode(s.encode() if isinstance(s, str) else s).decode()


def b64url(s: str | bytes) -> str:
    return base64.urlsafe_b64encode(s.encode() if isinstance(s, str) else s).decode().rstrip("=")


def hexs(s: str | bytes, sep: str = "", prefix: str = "") -> str:
    data = s.encode() if isinstance(s, str) else s
    return sep.join(f"{prefix}{b:02X}" for b in data)


def classic(n: int, level: str, thread: str, comp: str, msg: str, base: str = "2026-05-14 10:00:00") -> str:
    t = dt.datetime.fromisoformat(base) + dt.timedelta(milliseconds=n * 250)
    return f"{t:%Y-%m-%d %H:%M:%S}.{t.microsecond // 1000:03d} {level} {thread} {comp} {msg}"


def tab(n: int, thread: str, sev: str, comp: str, msg: str, start_ms: int = 10 * 3600 * 1000) -> str:
    ms = start_ms + n * 250
    h, rem = divmod(ms, 3600 * 1000)
    m, rem = divmod(rem, 60 * 1000)
    s, ms = divmod(rem, 1000)
    return f"{h % 24:02d}:{m:02d}:{s:02d}.{ms:03d}\t{thread}\t{sev}\t{comp}\t{msg}"


def is_readable_base64(token: str) -> bool:
    try:
        pad = "=" * (-len(token) % 4)
        raw = base64.b64decode(token.replace("-", "+").replace("_", "/") + pad, validate=True)
        text = raw.decode("utf-8")
    except Exception:
        return False
    return all(c in "\t\r\n" or (ord(c) >= 32 and ord(c) != 127 and c != "\ufffd") for c in text)


# ---------------------------------------------------------------- crypto material
def make_crypto():
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.ORGANIZATION_NAME, "LogGrokX"),
                      x509.NameAttribute(NameOID.COMMON_NAME, "loggrokx.test")])
    cert = (x509.CertificateBuilder()
            .subject_name(name).issuer_name(name).public_key(key.public_key())
            .serial_number(0x5A1C0001)
            .not_valid_before(dt.datetime(2026, 1, 2, 3, 4, 5, tzinfo=dt.timezone.utc))
            .not_valid_after(dt.datetime(2027, 1, 2, 3, 4, 5, tzinfo=dt.timezone.utc))
            .add_extension(x509.SubjectAlternativeName([x509.DNSName("loggrokx.test"),
                                                        x509.DNSName("www.loggrokx.test")]), False)
            .sign(key, hashes.SHA256()))
    der = cert.public_bytes(serialization.Encoding.DER)
    pem = cert.public_bytes(serialization.Encoding.PEM).decode().strip()
    key_pem = key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
                                serialization.NoEncryption()).decode().strip()
    spki = key.public_key().public_bytes(serialization.Encoding.DER,
                                         serialization.PublicFormat.SubjectPublicKeyInfo)
    nums = key.public_key().public_numbers()
    modulus_le = nums.n.to_bytes(256, "big")[::-1]
    blob = struct.pack("<BBHI4sII", 0x06, 0x02, 0, 0xA400, b"RSA1", 2048, nums.e) + modulus_le
    expected = {
        "Subject": "CN=loggrokx.test, O=LogGrokX",
        "Issuer": "CN=loggrokx.test, O=LogGrokX",
        "Serial number": "5A1C0001",
        "Not before": "2026-01-02 03:04:05 UTC",
        "Not after": "2027-01-02 03:04:05 UTC",
        "Thumbprint (SHA-1)": hashlib.sha1(der).hexdigest().upper(),
        "Signature algorithm": "sha256RSA",
        "Public key": "RSA 2048 bits",
        "Subject alternative names": "loggrokx.test, www.loggrokx.test",
        "PUBLICKEYBLOB Algorithm": "CALG_RSA_KEYX",
        "PUBLICKEYBLOB Key size": "2048 bits",
        "PUBLICKEYBLOB Public exponent": str(nums.e),
        "PUBLICKEYBLOB Modulus SHA-1": hashlib.sha1(nums.n.to_bytes(256, "big")).hexdigest().upper(),
    }
    return der, pem, key_pem, spki, blob, expected


# ---------------------------------------------------------------- files
def gen_basic_tab():
    """Общий TAB-лог (формат Classic #2): цвета, фильтры, потоки, маппинги."""
    rows = [
        ("0x0A01", "INF", "Net", "[T01] connection opened host=alpha"),
        ("0x0A01", "INF", "Net", "[T02] handshake step=1 completed"),
        ("0x0A01", "DBG", "Net", "[T03] handshake step=2 debug trace"),
        ("0x0A02", "WRN", "Db", "[T04] slow query step=1 took 1200ms"),
        ("0x0A02", "ERR", "Db", "[T05] query failed step=2 code=0x80070005"),
        ("0x0A01", "INF", "Net", "[T06] connection reused step=3"),
        ("0x0A03", "IMP", "Ui", "[T07] important user action step=1"),
        ("0x0A03", "INF", "Ui", "[T08] render completed step=2"),
        ("0x0A03", "INF", "Ui", "[T09] FATAL renderer crashed step=3"),
        ("0x0A02", "ERR", "Db", "[T10] No error found while checking step=4"),
        ("0x0A02", "INF", "Db", "[T11] result error: 0x0 returned step=5"),
        ("0x0A01", "ERR", "Net", "[T12] fatal network error step=4"),
        ("0x0A01", "INF", "Net", "[T13] connection closed step=5"),
        ("0x0A02", "INF", "Db", "[T14] pool drained step=6"),
        ("0x0A03", "INF", "Ui", "[T15] window closed step=4"),
        ("0x0A03", "DBG", "Ui", "[T16] dispose step=5"),
    ]
    write("tab-basic.log", [tab(i, *r) for i, r in enumerate(rows)])


def gen_threads():
    seq = (["0x0B01"] * 3 + ["0x0B02"] * 2 + ["0x0B01"] + ["0x0B03"] * 4 + ["0x0B02"] * 3 + ["0x0B01"] * 2
           + ["0x0B03"] + ["0x0B02"] * 2)
    lines = [tab(i, th, "INF", "Worker", f"[G{i + 1:02d}] thread {th} step {i + 1}") for i, th in enumerate(seq)]
    write("threads.log", lines)


def gen_classic_multiline():
    lines = [
        classic(0, "INF", "Thread-01", "Startup", "[M01] application started version=1.2.3"),
        classic(1, "ERR", "Thread-02", "PaymentService", "[M02] Payment authorization failed orderId=1001"),
        "   at LogGrokX.Demo.PaymentService.Process(Order order) in C:\\src\\PaymentService.cs:line 118",
        "   at LogGrokX.Demo.OrderService.Handle(Request request) in C:\\src\\OrderService.cs:line 42",
        "   continuation-only-token-QX7 appears only in this continuation line",
        classic(2, "INF", "Thread-01", "Startup", "[M03] Сервер запущен, ошибка не обнаружена"),
        classic(3, "WRN", "Thread-03", "Cache", "[M04] Ошибка кэша: ключ не найден"),
        classic(4, "INF", "Thread-03", "Cache", "[M05] 日本語のメッセージ and emoji-free unicode ✓"),
        classic(5, "INF", "Thread-01", "Startup", "[M06] last record of file"),
    ]
    write("classic-multiline.log", lines)


def gen_search():
    m = [
        "[S01] user = alice role=admin path: C:\\temp\\data",
        "[S02] status:200 code : 404 key\t=\tvalue-tab",
        "[S03] name=\"John Smith\" title='Lead dev'",
        "[S04] mount \\\\?\\Volume{3F2504E0-4F89-11D3-9A0C-0305E82C3301}\\ and \\Device\\HarddiskVolume1\\ done",
        "[S05] [outer [inner] tail] <a<b>c> (x(y)z)",
        "[S06] Can't connect, won't retry",
        "[S07] requestor={unknown} owner={ID-42}",
        "[S08] price is $5.00 (approx) [x] a+b*c? ^start end$",
        "[S09] Payment FAILED for order 1",
        "[S10] payment failed for order 2",
        "[S11] PAYMENT Failed for order 3",
        "[S12] Ошибка подключения к серверу",
        "[S13] ошибка повторного подключения",
        "[S14] alpha beta gamma delta",
        "[S15] " + "a" * 40 + "!",
        "[S16] alpha only",
        "[S17] delta only",
        "[S18] mixed alpha and delta and gamma",
    ]
    lines = [classic(i, "INF", f"Thread-0{1 + i % 3}", "Search", s) for i, s in enumerate(m)]
    lines.insert(3, "   continuation line with needle-ZZ9 inside")
    write("search.log", lines)


def gen_many_matches():
    lines = [classic(i, "INF", "Thread-01", "Bulk", f"bulk-match {i:05d}") for i in range(5000)]
    write("many-matches.log", lines)


def gen_json_xml():
    big = json.dumps({"items": [{"id": i, "name": f"item-{i}", "tags": ["a", "b"], "ok": i % 2 == 0}
                                for i in range(1000)]})
    deep: object = {"leaf": "bottom"}
    for i in range(60):
        deep = {f"l{i}": deep}
    m = [
        '[J01] payload {"id":1,"name":"alpha","items":[1,2,3],"nested":{"a":{"b":{"c":true}}}} end',
        '[J02] array [{"k":1},{"k":2},{"k":3}]',
        '[J03] broken {"id":1, "name": "unterminated',
        '[J05] big ' + big,
        '[J06] deep ' + json.dumps(deep),
        '[J07] unicode {"city":"Москва","esc":"\\u0041\\u0042","quote":"a\\"b"}',
        '[J08] login {"user":"alice","action":"login","meta":{"ip":"10.0.0.1"}}',
        '[J09] two {"first":1} and {"second":{"x":2}}',
        '[X01] xml <?xml version="1.0" encoding="utf-8"?><order id="7"><item sku="A1">Widget</item><item sku="B2">Gadget</item></order>',
        '[X02] xml <root><a><b>text</b></a><c/></root>',
        '[X03] generic List<int> and Dictionary<string, int> are plain text',
        '[X04] html line break <br/> is plain',
        '[X05] single <root>text only</root> is plain',
        '[X06] json-with-xml {"xml":"<r><a>1</a></r>","n":1}',
        '[X07] xml-with-json <root><data>{"k":1}</data><b>2</b></root>',
    ]
    lines = [classic(i, "INF", "Thread-01", "Json", s) for i, s in enumerate(m)]
    # J04 (#31): JSON, разбитый на строки с табуляцией
    idx = 3
    lines[idx:idx] = [classic(50, "INF", "Thread-02", "Json", "[J04] multiline {"),
                      '\t"alpha": 1,', '\t"beta": [1, 2],', '\t"gamma": {"x": "y"}', "}"]
    write("json-xml.log", lines)



def gen_json_arrays():
    """JSON-массивы верхнего уровня (#111) и текст в квадратных скобках, который не является JSON."""
    m = [
        '[A01] primitives [1,2,3] tail',
        '[A02] strings ["alpha","beta","gamma"]',
        '[A03] objects [{"id":1,"tags":["a","b"]},{"id":2,"tags":[]}]',
        '[A04] nested [[1,2],[3,[4,5]]]',
        '[A05] mixed {"list":[10,20]} and [true,false,null]',
        '[A06] empty [] and {}',
        '[A07] plain level [INFO] thread [T-1] marker [S01]',
        '[A08] plain index retry [3] of [5]',
        '[A09] broken [1,2',
        '[A10] trailing comma [1,2,]',
        '[A11] single number [-1.5e3] [ 42 ] [7,] vs ["3"] [[3]] [3,5]',
    ]
    write("json-arrays.log", [classic(i, "INF", "Thread-01", "Json", s) for i, s in enumerate(m)])


def gen_unicode():
    """Смешение письменностей в одной строке: font fallback (#108) и общая базовая линия (PR #149)."""
    m = [
        "[U01] latin baseline reference Hxgp 0123",
        "[U02] 日本語のメッセージ mixed with Latin Hxgp",
        "[U03] 中文日志 简体 繁體 mixed Hxgp",
        "[U04] 한국어 로그 메시지 mixed Hxgp",
        "[U05] Кириллица Ελληνικά Hxgp",
        "[U06] हिन्दी देवनागरी mixed Hxgp",
        "[U07] math ∑∫√∞≠≤≥ arrows ←↑→↓ box ┌─┐ check ✓✗",
        "[U08] Hxgp日本語Hxgp한국어Hxgp中文Hxgp",
    ]
    write("unicode.log", [classic(i, "INF", "Thread-01", "Text", s) for i, s in enumerate(m)])


def gen_binary(der, pem, key_pem, spki, blob, expected):
    jwt_header = b64url('{"alg":"HS256","typ":"JWT"}')
    jwt_payload = b64url('{"sub":"1234567890","name":"John Doe","iat":1516239022}')
    jwt = f"{jwt_header}.{jwt_payload}.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
    # URL-safe текст, base64 которого содержит '-' и '_'
    url_text = "subjects?>>>??~~~ok"
    url_token = b64url(url_text)
    assert "-" in url_token or "_" in url_token
    plus_text = "~~~>>>???<<<~~~ value"
    plus_token = b64(plus_text)
    assert "+" in plus_token or "/" in plus_token
    neg_alpha = "ThisIsNotBase64Text"
    candidates = [neg_alpha, "ThisIsNotBase64AtAllReally", "QWERTYUIOPASDFGHJKLZ"]
    neg_alpha = next(c for c in candidates if not is_readable_base64(c))
    pem_text = "-----BEGIN MESSAGE-----\n" + b64("Hello from a PEM text payload") + "\n-----END MESSAGE-----"
    pem_esc = pem.replace("\n", "\\n")
    blob_b64 = b64(blob)
    assert blob_b64.startswith("BgIAAACkAABSU0Ex")
    xml_payload = b64("a < b & c \"quoted\"")
    m = [
        "[B01] placeholder",
        "[B02] placeholder",
        "[B03] payload=eyJpZCI6NDIsIm9rIjp0cnVlfQ== accepted",
        f"[B04] short token={b64('Hello')} only",
        f"[B05] jwt {jwt}",
        f"[B06] call https://host/api/v1/{b64url('{\"user\":\"bob\",\"role\":\"admin\"}')}",
        f"[B07] header X-Token-{b64url('{\"session\":\"s-77\",\"ttl\":300}')}",
        f"[B08] suffix {b64url('{\"glued\":true,\"n\":8}')}_v2",
        f"[B09] urlsafe {url_token}",
        f"[B10] negatives guid=3F2504E0-4F89-11D3-9A0C-0305E82C3301 hex=deadbeefdeadbeefdeadbeef word={neg_alpha} bin={b64(bytes(range(16)))}",
        "[B12] json-pem {\"cert\":\"" + pem_esc + "\"}",
        "[B13] mismatched -----BEGIN CERTIFICATE-----\\nMIIBszCCAVmgAwIBAgIUEXAMPLE\\n-----END PUBLIC KEY-----",
        f"[B16] der {b64(der)}",
        f"[B17] spki {b64(spki)}",
        f"[B18] blob {blob_b64}",
        '[B19] json {"token":"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCIsInNlciI6ImVtcHR5In0","n":1,"eyJpZCI6NDIsIm9rIjp0cnVlfQ":"key-is-not-decoded"}',
        '[B20] json-escaped {"v":"' + plus_token.replace("+", "\\u002B").replace("/", "\\/") + '"}',
        f'[B21] xml <root><a>{xml_payload}</a><b attr="{xml_payload}"/><c><![CDATA[{xml_payload}]]></c></root>',
        f"[B22] base64-of-pem {b64(pem)}",
        '[B23] json-cert {"cert":"' + pem_esc + '","two":"' + pem_esc + "\\n" + pem_esc + '","blob":"' + blob_b64 + '"}',
        f"[B24] both {b64('{\"kind\":\"base64-part\"}')} and " + pem_esc,
    ]
    lines = [classic(i, "INF", "Thread-01", "Crypto", s) for i, s in enumerate(m)]
    # значение целиком в поле Component (минимум 8 символов для целого значения)
    lines[0] = classic(0, "INF", "Thread-01", "eyJpZCI6NDIsIm9rIjp0cnVlfQ==", "[B01] whole value in Component")
    lines[1] = classic(1, "INF", "Thread-01", '"' + b64("Hello!!") + '"', "[B02] quoted whole value in Component")
    lines.insert(2, classic(2, "INF", "Thread-01", b64("Hello"), "[B02b] 8-char whole value in Component"))
    lines.insert(3, classic(2, "INF", "Thread-01", b64("Hi"), "[B02c] 4-char whole value in Component"))
    # многострочные PEM-записи (реальные переводы строк)
    pos = 10
    lines[pos:pos] = [classic(40, "INF", "Thread-02", "Crypto", "[B11] certificate follows")] + pem.split("\n")
    lines += [classic(41, "INF", "Thread-02", "Crypto", "[B14] private key follows")] + key_pem.split("\n")
    lines += [classic(42, "INF", "Thread-02", "Crypto", "[B15] text payload follows")] + pem_text.split("\n")
    write("binary/binary.log", lines)
    # Тот же контент в формате без Component (профиль Regression, формат RegN)
    nocomp = [f"2026-05-14 10:00:{i:02d}.000 :: INF :: [N{i + 1:02d}] {s.split('] ', 1)[1]}"
              for i, s in enumerate([m[0], m[2], m[4]])]
    write("binary/no-component.log", nocomp)
    exp = ["# Ожидаемые значения для testdata/binary", "",
           "Файл сгенерирован `tools/generate_testdata.py` вместе с `binary.log` и `hex.log`.", "",
           "| Поле | Значение |", "|---|---|"]
    exp += [f"| {k} | `{v}` |" for k, v in expected.items()]
    exp += ["", f"- Декодированный JWT header (B05): `{{\"alg\":\"HS256\",\"typ\":\"JWT\"}}`",
            "- B01/B03: `{\"id\":42,\"ok\":true}`",
            f"- B02: `Hello!!`; B02b (`{b64('Hello')}`): `Hello`; B02c (`{b64('Hi')}`) — НЕ декодируется (< 8 символов); B04 (`{b64('Hello')}`) — НЕ декодируется (фрагмент < 16 символов)",
            f"- B09 URL-safe: `{url_text}`",
            f"- B20: `{plus_text}`",
            "- B21: `a < b & c \"quoted\"` (в XML должно быть экранировано: `&lt;`, `&amp;`, `&quot;`)",
            f"- B10 слово-негатив: `{neg_alpha}`"]
    write("binary/EXPECTED.md", exp, newline="\n")
    return der, pem


def gen_hex(der, pem):
    m = [
        "[H01] data=48656C6C6F20776F726C64",
        "[H02] spaced 48 65 6C 6C 6F 20 77 6F 72 6C 64",
        "[H03] dashed 48-65-6C-6C-6F-20-77-6F-72-6C-64",
        "[H04] colon 48:65:6C:6C:6F:20:77:6F:72:6C:64",
        "[H05] prefixed 0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x77, 0x6F, 0x72, 0x6C, 0x64",
        "[H06] utf16 " + hexs("Hello world".encode("utf-16-le")),
        "[H07a] hresult 0x80070005 only",
        "[H07b] eight-bytes 0x4142434445464748 only",
        "[H07c] digits 41424344454647484950 only",
        "[H07d] sha256 " + hashlib.sha256(b"loggrokx").hexdigest(),
        "[H07e] guid 3F2504E0-4F89-11D3-9A0C-0305E82C3301",
        "[H07f] eight-printable " + hexs("Hello!!!"),
        "[H07g] nine-printable " + hexs("Hello!!!!"),
        '[H08] json {"payload":"' + hexs('Hello "world"\n') + '","' + hexs("Hello world") + '":1}',
        '[H09] xml <root><v>' + hexs("<tag> & more") + '</v><w attr="' + hexs("x < y & z") + '"/></root>',
        "[H10] hex-json " + hexs('{"hex":true,"n":5}'),
        "[H11] hex-der " + hexs(der),
        "[H12] hex-pem " + hexs(pem),
        '[H13] json-hex-der {"cert":"' + hexs(der) + '"}',
    ]
    write("hex.log", [classic(i, "INF", "Thread-01", "Hex", s) for i, s in enumerate(m)])


def gen_colors():
    write("colors.log", [tab(i, "0x0C01", sev, "Color", msg) for i, (sev, msg) in enumerate([
        ("ERR", "[C01] tab ERR line"),
        ("WRN", "[C02] tab WRN line"),
        ("DBG", "[C03] tab DBG line"),
        ("IMP", "[C04] tab IMP line"),
        ("INF", "[C05] something FATAL happened"),
        ("INF", "[C06] an error occurred (word error)"),
        ("ERR", "[C07] No error here"),
        ("INF", "[C08] status error: 0x0 means ok"),
        ("ERR", "[C09] fatal and ERR together"),
        ("INF", "[C10] plain info line"),
        ("INF", "[C11] custom-rule-token here"),
    ])])


def gen_mapping():
    rows = [
        ("0x00001", "[V01] request type 0x00001 failed: 0x80070005"),
        ("Core", "[V02] boundary 0x000010 and x0x00001 and 0x00001x"),
        ("Core", "[V03] case TYPE_A vs type_a"),
        ("Core", "[V04] phrase disk full and disk alone"),
        ("Core", '[V05] json {"code":5,"name":"0x00001","0x00001":1,"list":[5,"TYPE_A"]}'),
        ("Core", '[V06] xml <r a="0x00001"><v>0x00002</v><c><![CDATA[0x00001]]></c></r>'),
        ("Core", "[V07] dup value 0x00003"),
        ("Core", "[V08] amp value AMP_CODE"),
    ]
    write("mapping/mapping.log", [tab(i, "0x0D01", "INF", c, msg) for i, (c, msg) in enumerate(rows)])
    lst = {"Name": "Reg-Types", "Enabled": True, "IgnoreCase": True, "Fields": ["Message"],
           "Items": [{"Value": "0x00001", "Mapping": "type 1"}, {"Value": "0x00002", "Mapping": "type 2"}]}
    (ROOT / "mapping").mkdir(parents=True, exist_ok=True)
    (ROOT / "mapping/vm-list.json").write_text(json.dumps(lst, indent=2), encoding="utf-8")
    arr = [
        {"Name": "Reg-Case", "Enabled": True, "IgnoreCase": False, "Fields": [],
         "Items": [{"Value": "TYPE_A", "Mapping": "upper A"}, {"Value": "AMP_CODE", "Mapping": "a<b & \"c\""}]},
        {"Name": "Reg-Phrase", "Enabled": True, "IgnoreCase": True, "Fields": [],
         "Items": [{"Value": "disk", "Mapping": "short"}, {"Value": "disk full", "Mapping": "long wins"},
                   {"Value": "5", "Mapping": "five"}, {"Value": "0x00003", "Mapping": "first list"}]},
        {"Name": "Reg-Second", "Enabled": True, "IgnoreCase": True, "Fields": [],
         "Items": [{"Value": "0x00003", "Mapping": "second list"}]},
    ]
    (ROOT / "mapping/vm-array.json").write_text(json.dumps(arr, indent=2), encoding="utf-8")
    (ROOT / "mapping/vm-plain.json").write_text(json.dumps({"0x80070005": "ACCESS_DENIED_PLAIN"}, indent=2),
                                                encoding="utf-8")
    (ROOT / "mapping/vm-invalid.json").write_text('{"Name": "Broken", "Items": [ {"Value": ', encoding="utf-8")
    replace = dict(lst, Items=[{"Value": "0x00001", "Mapping": "type 1 (replaced)"}])
    (ROOT / "mapping/vm-replace.json").write_text(json.dumps(replace, indent=2), encoding="utf-8")


def gen_callstack():
    lines = [
        classic(0, "ERR", "Thread-01", "Crash", "[K01] unhandled exception, Call stack:"),
        " 0# test_.dll!38B3DC8D-3D1D-FADD-E67B-593D48ABEC14,1!86da0",
        " 1# kernel32.dll!14ad",
        " 2# ntdll.dll+0x5aa58",
        classic(1, "INF", "Thread-01", "Crash", "[K02] not a stack: 0# is just text here"),
        classic(2, "ERR", "Thread-01", "Crash", "[K03] single frame stack:"),
        " 0# missing_module.dll+0x10",
    ]
    write("callstack/callstack-synthetic.log", lines)


def gen_time():
    t0 = 23 * 3600 * 1000 + 59 * 60 * 1000 + 57 * 1000
    # переход через полночь: шаг 250 мс начиная с 23:59:57.000
    write("time/midnight.log", [tab(i, "0x0E01", "INF", "Clock", f"[D{i + 1:02d}] tick", start_ms=t0)
                                for i in range(25)])
    base = [classic(i, "INF", "Thread-01", "Order", f"[U{i + 1:02d}] ordered") for i in range(10)]
    base[5] = classic(-20, "WRN", "Thread-01", "Order", "[U06] goes back in time")
    write("time/unordered.log", base)
    write("time/same-time.log", [f"2026-05-14 10:00:00.000 INF Thread-01 Same [Q{i + 1:02d}] same timestamp"
                                 for i in range(20)])
    write("time/single-line.log", [classic(0, "INF", "Thread-01", "One", "[O01] the only line")])
    write("time/notime.txt", [f"plain text line {i + 1:03d} without timestamp" for i in range(200)])
    small = [tab(i, "0x0E02", "INF", "Clock", f"[R{i + 1:02d}] small back-jump") for i in range(6)]
    small[3] = tab(-8, "0x0E02", "INF", "Clock", "[R04] 2 seconds back (clamped)")
    write("time/small-backjump.log", small)


def gen_merge():
    a, b = [], []
    for i in range(12):
        ms = 100 + i * 200
        a.append(f"2026-05-14 10:00:{ms // 1000:02d}.{ms % 1000:03d} {'ERROR' if i == 4 else 'INFO'} "
                 f"[T-{1 + i % 2}] Api - [A{i + 1:02d}] api event {i + 1}")
    for i in range(12):
        ms = 200 + i * 200
        b.append(f"2026-05-14T10:00:{ms // 1000:02d}.{ms % 1000:03d}|{'WARN' if i == 2 else 'INFO'}|"
                 f"w{1 + i % 2}|Worker|[B{i + 1:02d}] worker event {i + 1}")
    # одинаковое время в обоих источниках: A12 == B11 (2.200)
    a[11] = "2026-05-14 10:00:02.200 INFO [T-2] Api - [A12] same time as B11"
    write("merge/merge-a.log", a)
    write("merge/merge-b.log", b)
    write("merge/merge-plain.txt", [f"plain merged line {i + 1}" for i in range(5)])
    un = [f"2026-05-14 10:00:0{i}.000 INFO [T-9] Api - [Z{i + 1:02d}] unordered source" for i in range(5)]
    un[2] = "2026-05-14 09:00:00.000 INFO [T-9] Api - [Z03] back in time"
    write("merge/merge-unordered.log", un)


def gen_transform():
    payload = b64('{"user":"bob","licenses":3}')
    l1 = tab(0, "0x0F01", "INF", "lic", '[ContentImp.cpp:12] ticket "TicketBody" : { "Id" : 1, "Data" : "'
             + payload + '" } [TR01]')
    l2 = tab(1, "0x0F01", "INF", "app", '[TR02] same payload outside rule "Data" : "' + payload + '"')
    write("transform/transform.log", [l1, l2])
    import re as _re
    json_payload = next(t for t in (b64('{"seats":5,"owner":"' + "alice"[:k] + "x" * j + '"}')
                                    for k in range(1, 6) for j in range(0, 12))
                        if _re.fullmatch(r"\w+==", t))
    line = tab(0, "0x0F02", "INF", "lic", '[ContentImp.cpp:99] "TicketBody" x "' + json_payload + '" [TR03]')
    data = (line + "\r\n" + tab(1, "0x0F02", "INF", "app", "[TR04] plain xor line") + "\r\n").encode()
    (ROOT / "transform/transform-xor.log").write_bytes(bytes(c ^ 0xEF for c in data))
    xor_plain = [tab(i, "0x0F03", "INF", "Secure", f"[XR{i + 1:02d}] secure line {i + 1}") for i in range(10)]
    data = ("\r\n".join(xor_plain) + "\r\n").encode()
    (ROOT / "xor/xor-ef.log").parent.mkdir(parents=True, exist_ok=True)
    (ROOT / "xor/xor-ef.log").write_bytes(bytes(c ^ 0xEF for c in data))
    (ROOT / "xor/xor-5a.log").write_bytes(bytes(c ^ 0x5A for c in data))


def gen_biglines():
    def line(n, size):
        # size = длина поля Message (порог BigLineSize применяется к тексту ячейки)
        head = f"[L{n + 1:02d}] size={size} "
        tail = "END" + str(n + 1)
        body = "".join(chr(ord("a") + (k % 26)) for k in range(max(0, size - len(head) - len(tail))))
        return classic(n, "INF", "Thread-01", "Big", head + body + tail)
    sizes = [100, 4095, 4096, 4097, 10000, 100000]
    write("biglines.log", [line(i, s) for i, s in enumerate(sizes)])


def gen_encodings():
    lines = [classic(i, "INF", "Thread-01", "Enc", f"[E{i + 1:02d}] Кириллица и ümlaut №{i + 1}") for i in range(5)]
    text = "\r\n".join(lines) + "\r\n"
    write("encoding/utf8-bom.log", text, bom=True)
    write("encoding/utf8-nobom.log", text)
    (ROOT / "encoding/utf16le-bom.log").write_bytes(b"\xff\xfe" + text.encode("utf-16-le"))
    (ROOT / "encoding/utf16le-nobom.log").write_bytes(text.encode("utf-16-le"))
    (ROOT / "encoding/utf16be-bom.log").write_bytes(b"\xfe\xff" + text.encode("utf-16-be"))
    mixed = (lines[0] + "\r\n" + lines[1] + "\n" + lines[2] + "\r" + lines[3] + "\r\n" + lines[4]).encode()
    (ROOT / "encoding/mixed-eol-no-trailing.log").write_bytes(mixed)
    (ROOT / "encoding/empty.log").write_bytes(b"")
    (ROOT / "encoding/only-newlines.log").write_bytes(b"\r\n" * 10)
    (ROOT / "encoding/binary.bin").write_bytes(bytes((i * 37 + 11) % 256 for i in range(65536)))
    d = ROOT / "encoding/путь с пробелами"
    d.mkdir(parents=True, exist_ok=True)
    (d / "лог файл.log").write_text(text, encoding="utf-8")


def gen_formats():
    # Файл, который одинаково подходит двум форматам Classic (#2 и #4): выигрывает первый.
    write("formats/ambiguous-tab.log", [tab(i, "0x0101", "INF", "Comp", f"[F{i + 1:02d}] ambiguous") for i in range(5)])
    # Файл, где 3% строк подходят формату (ниже порога 5%) => текстовый режим
    ls = [f"noise line {i}" for i in range(97)]
    for i in (10, 50, 90):
        ls[i] = classic(i, "INF", "Thread-01", "Rare", "[F90] rare formatted line")
    write("formats/below-threshold.log", ls)
    # 10% строк подходят => формат выбран
    ls = [f"noise line {i}" for i in range(100)]
    for i in range(0, 100, 10):
        ls[i] = classic(i, "INF", "Thread-01", "Rare", f"[F91] formatted {i}")
    write("formats/above-threshold.log", ls)
    write("formats/venn.log", [
        "2026-05-14 9:05:01.123 - [INFO] - [1:200:300] - [svc.exe] - [Core] - [V01] venn info",
        "2026-05-14 9:05:02.456 - [ERRO] - [1:200:301] - [svc.exe] - [Core] - [V02] venn error",
        "2026-05-14 9:05:03.789 - [WARN] - [1:200:300] - [svc.exe] - [Net] - [V03] venn warn",
        "2026-05-14 9:05:04.000 - [CRIT] - [1:200:302] - [svc.exe] - [irql 2] - [Drv] - [V04] venn crit",
        "2026-05-14 9:05:05.000 - [TRAC] - [1:200:300] - [Core] - [V05] venn trace",
    ])
    write("formats/regression-a.log", [
        f"2026-05-14 10:00:0{i}.000 {lvl} [T-1] Api - [RA{i + 1:02d}] regression format A"
        for i, lvl in enumerate(["INFO", "ERROR", "WARN"])])
    write("formats/regression-b.log", [
        f"2026-05-14T10:00:0{i}.000|{sev}|worker-{i % 2}|svc.{comp}|[RB{i + 1:02d}] regression format B"
        for i, (sev, comp) in enumerate([("Info", "api"), ("Error", "db"), ("Info", "api"), ("Warn", "db")])])
    write("formats/regression-n.log", [
        f"2026-05-14 10:00:0{i}.000 :: {lvl} :: [RN{i + 1:02d}] regression format N without Component"
        for i, lvl in enumerate(["Info", "Error", "Debug"])])


REGRESSION_PROFILE = """    - Name: Regression
      InheritLegacySettings: false
      ColorSettings:
        Rules:
          - RegexString: '\\bERROR\\b'
            ForegroundColor: Red
          - RegexString: '\\bWARN\\b'
            ForegroundColor: Chocolate
      LogFormats:
        # RegA: dated, Level, Thread in [], Component
        - Regex: '^(?<Time>\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3}) (?<Level>[A-Z]{3,5}) \\[(?<Thread>[^\\]]+)\\] (?<Component>\\S+) - (?<Message>.*)'
          IndexedFields:
            - Level
            - Thread
            - Component
          TimeFormat: yyyy-MM-dd HH:mm:ss.fff
        # RegB: ISO 'T', lower-case thread/component, Severity, explicit TimeField
        - Regex: '^(?<time>\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3})\\|(?<Severity>\\w+)\\|(?<thread>[^|]+)\\|(?<component>[^|]+)\\|(?<Message>.*)'
          IndexedFields:
            - Severity
            - thread
            - component
          TimeField: time
          TimeFormat: yyyy-MM-ddTHH:mm:ss.fff
        # RegN: no Component field
        - Regex: '^(?<Time>\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3}) :: (?<Level>\\w+) :: (?<Message>.*)'
          IndexedFields:
            - Level
          TimeFormat: yyyy-MM-dd HH:mm:ss.fff
"""

LEGACY = """Settings:
  ColorSettings:
    Rules:
      - RegexString: \\tERR\\t
        ForegroundColor: Red
  LogFormats:
    - Regex: ^(?'Time'\\d{2}\\:\\d{2}\\:\\d{2}\\.\\d{3})\\t(?'Thread'0x[0-9a-fA-F]+)\\t(?'Severity'\\w+)\\t(?:(?'Component'[^\\t]+)?\\t)?(?'Message'.*)
      IndexedFields:
        - Thread
        - Severity
        - Component
      TimeFormat: HH:mm:ss.fff
  ViewSettings:
    Language: en
    UpdateMode: disabled
"""


def gen_configs():
    repo = ROOT.parents[2]
    shipped = (repo / "LogGrokX" / "appsettings.yaml").read_text(encoding="utf-8")
    # Комментарии-примеры строк лога из штатного конфига (с табуляцией) не переносим:
    # тестовые данные не должны содержать имён и путей, похожих на реальные продукты.
    shipped = "".join(l for l in shipped.splitlines(keepends=True)
                      if not (l.lstrip().startswith("#") and "\t" in l))
    marker = "\n  DebugSettings:"
    assert marker in shipped
    reg = shipped.replace(marker, "\n" + REGRESSION_PROFILE.rstrip("\n") + "\n" + marker, 1)
    reg = reg.replace("    Language: system", "    Language: en", 1)
    reg = reg.replace("    UpdateMode: check", "    UpdateMode: disabled", 1)
    out = ROOT / "config"
    out.mkdir(parents=True, exist_ok=True)
    (out / "appsettings.regression.yaml").write_text(reg, encoding="utf-8")
    (out / "appsettings.legacy.yaml").write_text(LEGACY, encoding="utf-8")
    inherit = LEGACY.replace("  ViewSettings:", """  SelectedProfile: Inherits
  Profiles:
    - Name: Inherits
    - Name: ExplicitEmpty
      ColorSettings:
        Rules: []
      LogFormats: []
    - Name: NoInherit
      InheritLegacySettings: false
  ViewSettings:""")
    (out / "appsettings.inherit.yaml").write_text(inherit, encoding="utf-8")
    bad = reg.replace("SelectedProfile: Classic", "SelectedProfile: Broken", 1).replace(
        "    - Name: Regression", """    - Name: Broken
      InheritLegacySettings: false
      ColorSettings:
        Rules: []
      LogFormats:
        - Regex: '^(?<Time>[unclosed'
    - Name: Regression""", 1)
    (out / "appsettings.badprofile.yaml").write_text(bad, encoding="utf-8")
    (out / "appsettings.malformed.yaml").write_text(reg.replace("  ViewSettings:", "  ViewSettings: [broken", 1),
                                                    encoding="utf-8")


def main():
    der, pem, key_pem, spki, blob, expected = make_crypto()
    gen_basic_tab(); gen_threads(); gen_classic_multiline(); gen_search(); gen_many_matches()
    gen_json_xml(); gen_json_arrays(); gen_unicode()
    gen_binary(der, pem, key_pem, spki, blob, expected)
    gen_hex(der, pem)
    gen_colors(); gen_mapping(); gen_callstack(); gen_time(); gen_merge(); gen_transform()
    gen_biglines(); gen_encodings(); gen_formats(); gen_configs()
    print("testdata generated in", ROOT)


if __name__ == "__main__":
    main()
