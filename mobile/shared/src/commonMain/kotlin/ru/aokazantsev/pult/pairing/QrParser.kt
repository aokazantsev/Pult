package ru.aokazantsev.pult.pairing

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.crypto.PultCrypto
import ru.aokazantsev.pult.protocol.Base64Url
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.protocol.StrictJsonException

class QrParser(private val crypto: PultCrypto, private val allowPlainHttp: Boolean) {

    fun parse(text: String): QrParseResult {
        val root = try {
            StrictJson.parse(text) as? JsonObject ?: return rejected("не объект")
        } catch (failure: StrictJsonException) {
            return rejected("не JSON")
        }
        if (root.keys != EXPECTED_KEYS) return rejected("набор ключей не совпадает")
        val versions = versions(root["v"]) ?: return rejected("версии")
        val server = JsonFields.string(root["s"])?.takeIf(::serverAllowed) ?: return rejected("адрес сервера")
        val pair = JsonFields.string(root["c"])?.takeIf(JsonFields::isIdentifier) ?: return rejected("идентификатор пары")
        val secret = JsonFields.string(root["k"])?.let(Base64Url::decode)?.takeIf { it.size == SECRET_BYTES }
            ?: return rejected("секрет")
        val pcSign = publicKey(root["ps"]) ?: return rejected("ключ подписи ПК")
        val pcAgree = publicKey(root["pa"]) ?: return rejected("ключ согласования ПК")
        val idle = JsonFields.integer(root["n"])?.takeIf { it in MIN_IDLE..MAX_IDLE } ?: return rejected("срок простоя")
        return QrParseResult.Parsed(
            QrPayload(
                versions = versions,
                server = server,
                pair = pair,
                secret = secret,
                pcSign = pcSign,
                pcAgree = pcAgree,
                idleSeconds = idle.toInt(),
            ),
        )
    }

    private fun versions(element: kotlinx.serialization.json.JsonElement?): List<Long>? {
        val array = element as? JsonArray ?: return null
        if (array.isEmpty()) return null
        return array.map { JsonFields.integer(it) ?: return null }
    }

    private fun serverAllowed(value: String): Boolean {
        val rest = when {
            value.startsWith("https://") -> value.removePrefix("https://")
            allowPlainHttp && value.startsWith("http://") -> value.removePrefix("http://")
            else -> return false
        }
        return rest.isNotEmpty() && !rest.endsWith("/") &&
            rest.none { it.isWhitespace() || it == '?' || it == '#' || it == '\\' }
    }

    private fun publicKey(element: kotlinx.serialization.json.JsonElement?): String? {
        val text = JsonFields.string(element) ?: return null
        val bytes = Base64Url.decode(text) ?: return null
        if (bytes.size != POINT_BYTES || bytes[0] != POINT_PREFIX) return null
        return if (crypto.isValidPublicPoint(bytes)) text else null
    }

    private fun rejected(reason: String) = QrParseResult.Rejected("QR не от Pult: $reason")

    private companion object {
        val EXPECTED_KEYS = setOf("v", "s", "c", "k", "ps", "pa", "n")
        const val SECRET_BYTES = 32
        const val POINT_BYTES = 65
        const val POINT_PREFIX: Byte = 4
        const val MIN_IDLE = 900L
        const val MAX_IDLE = 129600L
    }
}
