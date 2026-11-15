# Ожидаемые значения для testdata/binary

Файл сгенерирован `tools/generate_testdata.py` вместе с `binary.log` и `hex.log`.

| Поле | Значение |
|---|---|
| Subject | `CN=loggrokx.test, O=LogGrokX` |
| Issuer | `CN=loggrokx.test, O=LogGrokX` |
| Serial number | `5A1C0001` |
| Not before | `2026-01-02 03:04:05 UTC` |
| Not after | `2027-01-02 03:04:05 UTC` |
| Thumbprint (SHA-1) | `71938A7A92FAAD808D9FF9CF5880858F717209B3` |
| Signature algorithm | `sha256RSA` |
| Public key | `RSA 2048 bits` |
| Subject alternative names | `loggrokx.test, www.loggrokx.test` |
| PUBLICKEYBLOB Algorithm | `CALG_RSA_KEYX` |
| PUBLICKEYBLOB Key size | `2048 bits` |
| PUBLICKEYBLOB Public exponent | `65537` |
| PUBLICKEYBLOB Modulus SHA-1 | `5334927F83F74B6CD0B616284C1C94E0EC18169F` |

- Декодированный JWT header (B05): `{"alg":"HS256","typ":"JWT"}`
- B01/B03: `{"id":42,"ok":true}`
- B02: `Hello!!`; B02b (`SGVsbG8=`): `Hello`; B02c (`SGk=`) — НЕ декодируется (< 8 символов); B04 (`SGVsbG8=`) — НЕ декодируется (фрагмент < 16 символов)
- B09 URL-safe: `subjects?>>>??~~~ok`
- B20: `~~~>>>???<<<~~~ value`
- B21: `a < b & c "quoted"` (в XML должно быть экранировано: `&lt;`, `&amp;`, `&quot;`)
- B10 слово-негатив: `ThisIsNotBase64Text`
