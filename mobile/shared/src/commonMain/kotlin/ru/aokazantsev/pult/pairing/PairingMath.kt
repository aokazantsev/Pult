package ru.aokazantsev.pult.pairing

import ru.aokazantsev.pult.crypto.PultCrypto
import ru.aokazantsev.pult.protocol.DirectionKeys

class PairingMath(private val crypto: PultCrypto) {

    fun claimTag(secret: ByteArray, transcript: String): ByteArray =
        crypto.hmacSha256(secret, "pult-v2-claim\n$transcript".encodeToByteArray())

    fun fingerprint(transcript: String): String {
        val hash = crypto.sha256("pult-v2-fingerprint\n$transcript".encodeToByteArray())
        var value = 0UL
        for (index in 0 until FINGERPRINT_BYTES) value = (value shl 8) or hash[index].toUByte().toULong()
        val digits = (value % FINGERPRINT_MODULO).toString().padStart(FINGERPRINT_DIGITS, '0')
        return digits.chunked(GROUP).joinToString(" ")
    }

    fun deriveKeys(sharedSecret: ByteArray, transcript: String): DirectionKeys {
        val salt = crypto.sha256("pult-v2-salt\n$transcript".encodeToByteArray())
        val prk = crypto.hmacSha256(salt, sharedSecret)
        return DirectionKeys(
            pcToPhone = expand(prk, "pult-v2-key-pc-phone"),
            phoneToPc = expand(prk, "pult-v2-key-phone-pc"),
        )
    }

    private fun expand(prk: ByteArray, info: String): ByteArray =
        crypto.hmacSha256(prk, info.encodeToByteArray() + byteArrayOf(1))

    private companion object {
        const val FINGERPRINT_BYTES = 8
        const val FINGERPRINT_DIGITS = 16
        const val GROUP = 4
        val FINGERPRINT_MODULO = 10_000_000_000_000_000UL
    }
}
