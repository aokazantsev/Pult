package ru.aokazantsev.pult

import java.util.Base64

object DebugQrExtra {
    const val NAME = "pult_qr_b64"

    fun decode(encoded: String?): String? {
        if (encoded.isNullOrEmpty()) return null
        return try {
            Base64.getUrlDecoder().decode(encoded).decodeToString()
        } catch (_: IllegalArgumentException) {
            null
        }
    }
}
