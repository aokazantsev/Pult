package ru.aokazantsev.pult.protocol

import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.crypto.PultCrypto

class EnvelopeCodec(private val crypto: PultCrypto) {

    fun seal(
        key: ByteArray,
        pair: String,
        from: String,
        id: String,
        ts: Long,
        ttl: Int,
        inner: JsonObject,
        nonce: ByteArray = crypto.randomBytes(NONCE_BYTES),
    ): String {
        val aad = Wire.aad(VERSION_TEXT, pair, from, id, ts.toString(), ttl.toString())
        val sealed = crypto.aesGcmEncrypt(key, nonce, aad.encodeToByteArray(), inner.toString().encodeToByteArray())
        val ct = Base64Url.encode(nonce + sealed)
        return "{\"v\":2,\"pair\":\"$pair\",\"from\":\"$from\",\"id\":\"$id\",\"ts\":$ts,\"ttl\":$ttl,\"ct\":\"$ct\"}"
    }

    fun open(text: String, pair: String, ownSide: String, keys: DirectionKeys): OpenResult {
        val root = StrictJson.parseObjectOrNull(text) ?: return OpenResult.Invalid("не JSON")
        if (root.keys != FIELDS) return OpenResult.Invalid("набор полей")
        val version = JsonFields.canonicalInteger(root["v"]) ?: return OpenResult.Invalid("v")
        val envelopePair = JsonFields.string(root["pair"]) ?: return OpenResult.Invalid("pair")
        val from = JsonFields.string(root["from"]) ?: return OpenResult.Invalid("from")
        val id = JsonFields.string(root["id"])?.takeIf(JsonFields::isIdentifier) ?: return OpenResult.Invalid("id")
        val ts = JsonFields.canonicalInteger(root["ts"]) ?: return OpenResult.Invalid("ts")
        val ttl = JsonFields.canonicalInteger(root["ttl"]) ?: return OpenResult.Invalid("ttl")
        val ct = JsonFields.string(root["ct"]) ?: return OpenResult.Invalid("ct")
        if (version != VERSION_TEXT) return OpenResult.Incompatible(version.toLong())
        if (envelopePair != pair) return OpenResult.Invalid("чужая пара")
        if (from != Wire.FROM_PC && from != Wire.FROM_PHONE) return OpenResult.Invalid("from")
        if (from == ownSide) return OpenResult.Invalid("собственное направление")
        val sealed = Base64Url.decode(ct)?.takeIf { it.size >= NONCE_BYTES + TAG_BYTES } ?: return OpenResult.Invalid("ct")
        val key = if (from == Wire.FROM_PC) keys.pcToPhone else keys.phoneToPc
        val aad = Wire.aad(version, pair, from, id, ts, ttl)
        val plaintext = crypto.aesGcmDecrypt(
            key = key,
            nonce = sealed.copyOfRange(0, NONCE_BYTES),
            aad = aad.encodeToByteArray(),
            sealed = sealed.copyOfRange(NONCE_BYTES, sealed.size),
        ) ?: return OpenResult.Invalid("не расшифровывается")
        val inner = StrictJson.parseObjectOrNull(plaintext.decodeToString()) ?: return OpenResult.Invalid("содержимое")
        return OpenResult.Opened(Envelope(id = id, from = from, ttl = ttl.toInt(), inner = inner))
    }

    private companion object {
        const val VERSION_TEXT = "2"
        const val NONCE_BYTES = 12
        const val TAG_BYTES = 16
        val FIELDS = setOf("v", "pair", "from", "id", "ts", "ttl", "ct")
    }
}
