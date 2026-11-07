package ru.aokazantsev.pult.crypto

import java.math.BigInteger
import java.security.AlgorithmParameters
import java.security.KeyFactory
import java.security.interfaces.ECPublicKey
import java.security.spec.ECFieldFp
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec

object EcCodec {

    const val COORDINATE_BYTES = 32
    const val POINT_BYTES = 65
    const val SIGNATURE_BYTES = 64

    val p256: ECParameterSpec by lazy {
        AlgorithmParameters.getInstance("EC").run {
            init(ECGenParameterSpec("secp256r1"))
            getParameterSpec(ECParameterSpec::class.java)
        }
    }

    fun fixed32(value: BigInteger): ByteArray {
        require(value.signum() >= 0) { "negative coordinate" }
        val raw = value.toByteArray()
        val trimmed = if (raw.size > 1 && raw[0] == 0.toByte()) raw.copyOfRange(1, raw.size) else raw
        require(trimmed.size <= COORDINATE_BYTES) { "coordinate too long" }
        return ByteArray(COORDINATE_BYTES - trimmed.size) + trimmed
    }

    fun publicToSec1(key: ECPublicKey): ByteArray =
        byteArrayOf(4) + fixed32(key.w.affineX) + fixed32(key.w.affineY)

    fun sec1ToPublic(point: ByteArray): ECPublicKey {
        require(isOnCurve(point)) { "point is not on P-256" }
        val x = BigInteger(1, point.copyOfRange(1, 1 + COORDINATE_BYTES))
        val y = BigInteger(1, point.copyOfRange(1 + COORDINATE_BYTES, POINT_BYTES))
        return KeyFactory.getInstance("EC").generatePublic(ECPublicKeySpec(ECPoint(x, y), p256)) as ECPublicKey
    }

    fun isOnCurve(point: ByteArray): Boolean {
        if (point.size != POINT_BYTES || point[0] != 4.toByte()) return false
        val curve = p256.curve
        val prime = (curve.field as ECFieldFp).p
        val x = BigInteger(1, point.copyOfRange(1, 1 + COORDINATE_BYTES))
        val y = BigInteger(1, point.copyOfRange(1 + COORDINATE_BYTES, POINT_BYTES))
        if (x >= prime || y >= prime) return false
        val left = y.multiply(y).mod(prime)
        val right = x.pow(3).add(curve.a.multiply(x)).add(curve.b).mod(prime)
        return left == right
    }

    fun derToP1363(der: ByteArray): ByteArray {
        var position = 0
        fun read(): Int {
            require(position < der.size) { "truncated DER" }
            return der[position++].toInt() and 0xFF
        }

        fun length(): Int {
            val first = read()
            if (first < 0x80) return first
            require(first == 0x81) { "unsupported DER length" }
            return read()
        }

        fun integer(): ByteArray {
            require(read() == 0x02) { "INTEGER expected" }
            val size = length()
            require(size in 1..(COORDINATE_BYTES + 1) && position + size <= der.size) { "bad INTEGER length" }
            val bytes = der.copyOfRange(position, position + size)
            position += size
            require(bytes[0] >= 0) { "negative INTEGER" }
            val value = BigInteger(bytes)
            require(value.signum() > 0) { "zero INTEGER" }
            return fixed32(value)
        }

        require(read() == 0x30) { "SEQUENCE expected" }
        val total = length()
        require(total == der.size - position) { "bad SEQUENCE length" }
        val r = integer()
        val s = integer()
        require(position == der.size) { "trailing DER data" }
        return r + s
    }
}
