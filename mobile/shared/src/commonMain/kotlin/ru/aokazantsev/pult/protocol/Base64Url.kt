package ru.aokazantsev.pult.protocol

object Base64Url {

    private const val ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"

    fun encode(data: ByteArray): String {
        val out = StringBuilder((data.size * 4 + 2) / 3)
        var index = 0
        while (index + 2 < data.size) {
            val chunk = (data[index].toInt() and 0xFF shl 16) or
                (data[index + 1].toInt() and 0xFF shl 8) or
                (data[index + 2].toInt() and 0xFF)
            out.append(ALPHABET[chunk shr 18 and 0x3F])
            out.append(ALPHABET[chunk shr 12 and 0x3F])
            out.append(ALPHABET[chunk shr 6 and 0x3F])
            out.append(ALPHABET[chunk and 0x3F])
            index += 3
        }
        when (data.size - index) {
            1 -> {
                val chunk = data[index].toInt() and 0xFF shl 16
                out.append(ALPHABET[chunk shr 18 and 0x3F])
                out.append(ALPHABET[chunk shr 12 and 0x3F])
            }
            2 -> {
                val chunk = (data[index].toInt() and 0xFF shl 16) or (data[index + 1].toInt() and 0xFF shl 8)
                out.append(ALPHABET[chunk shr 18 and 0x3F])
                out.append(ALPHABET[chunk shr 12 and 0x3F])
                out.append(ALPHABET[chunk shr 6 and 0x3F])
            }
        }
        return out.toString()
    }

    fun decode(text: String): ByteArray? {
        if (text.length % 4 == 1) return null
        val values = IntArray(text.length)
        for (position in text.indices) {
            val value = ALPHABET.indexOf(text[position])
            if (value < 0) return null
            values[position] = value
        }
        val out = ByteArray(text.length * 3 / 4)
        var written = 0
        var position = 0
        while (position + 3 < text.length) {
            val chunk = (values[position] shl 18) or (values[position + 1] shl 12) or
                (values[position + 2] shl 6) or values[position + 3]
            out[written++] = (chunk shr 16).toByte()
            out[written++] = (chunk shr 8).toByte()
            out[written++] = chunk.toByte()
            position += 4
        }
        when (text.length - position) {
            2 -> {
                val chunk = (values[position] shl 18) or (values[position + 1] shl 12)
                out[written++] = (chunk shr 16).toByte()
            }
            3 -> {
                val chunk = (values[position] shl 18) or (values[position + 1] shl 12) or (values[position + 2] shl 6)
                out[written++] = (chunk shr 16).toByte()
                out[written++] = (chunk shr 8).toByte()
            }
        }
        return if (encode(out) == text) out else null
    }
}
