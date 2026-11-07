package ru.aokazantsev.pult.protocol

import java.math.BigInteger
import java.security.KeyFactory
import java.security.Signature
import java.security.spec.ECPublicKeySpec
import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import ru.aokazantsev.pult.crypto.EcCodec
import ru.aokazantsev.pult.crypto.JcaCrypto
import ru.aokazantsev.pult.vectors.FixedKeys
import ru.aokazantsev.pult.vectors.Vectors
import ru.aokazantsev.pult.vectors.Vectors.list
import ru.aokazantsev.pult.vectors.Vectors.str

class AuthVectorsTest {

    private val vectors = Vectors.load("auth")
    private val crypto = JcaCrypto()
    private val order = BigInteger("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551", 16)

    @Test
    fun loginStringsAndSignaturesMatch() {
        vectors.list("login").forEach { case ->
            val string = Wire.loginString(vectors.str("pair"), case.str("from"), case.str("challenge"))
            assertEquals(case.str("string"), string)
            assertTrue(verify(case.str("public"), string, case.str("sig")))
        }
    }

    @Test
    fun postStringsHashesAndSignaturesMatch() {
        vectors.list("post").forEach { case ->
            val body = case.str("body").encodeToByteArray()
            val hash = Base64Url.encode(crypto.sha256(body))
            assertEquals(case.str("body_sha256"), hash)
            val n = (case.jsonObject.getValue("n") as JsonPrimitive).content.toLong()
            val string = Wire.postString(case.str("op"), vectors.str("pair"), case.str("from"), case.str("stream"), n, hash)
            assertEquals(case.str("string"), string)
            assertTrue(verify(case.str("public"), string, case.str("sig")))
        }
    }

    @Test
    fun derIsConvertedToP1363WithFixedLengthCoordinates() {
        (vectors.list("login") + vectors.list("post")).forEach { case ->
            val converted = EcCodec.derToP1363(Vectors.hex(case.str("der_hex")))
            assertEquals(EcCodec.SIGNATURE_BYTES, converted.size)
            assertContentEquals(Base64Url.decode(case.str("sig")), converted)
        }
    }

    @Test
    fun verifyCasesAgreeWithTheEtalon() {
        vectors.list("verify").forEach { case ->
            val expected = (case.jsonObject.getValue("valid") as JsonPrimitive).content.toBoolean()
            assertEquals(expected, verify(case.str("public"), case.str("string"), case.str("sig")), case.str("case"))
        }
    }

    @Test
    fun ownSignatureFromTheFixedKeyPassesTheEtalonVerification() {
        val keys = FixedKeys.phone()
        repeat(20) { index ->
            val string = Wire.loginString(vectors.str("pair"), "phone", "challenge$index")
            val signature = keys.sign(string.encodeToByteArray())
            assertEquals(EcCodec.SIGNATURE_BYTES, signature.size)
            assertTrue(verify(FixedKeys.publicOf("phone_sign"), string, Base64Url.encode(signature)))
        }
    }

    private fun verify(publicKey: String, string: String, signature: String): Boolean {
        val point = Base64Url.decode(publicKey) ?: return false
        val raw = Base64Url.decode(signature) ?: return false
        if (raw.size != EcCodec.SIGNATURE_BYTES) return false
        val r = BigInteger(1, raw.copyOfRange(0, 32))
        val s = BigInteger(1, raw.copyOfRange(32, 64))
        if (r.signum() == 0 || s.signum() == 0 || r >= order || s >= order) return false
        val key = KeyFactory.getInstance("EC").generatePublic(ECPublicKeySpec(EcCodec.sec1ToPublic(point).w, EcCodec.p256))
        return Signature.getInstance("SHA256withECDSA").run {
            initVerify(key)
            update(string.encodeToByteArray())
            verify(derOf(r, s))
        }
    }

    private fun derOf(r: BigInteger, s: BigInteger): ByteArray {
        val rb = r.toByteArray()
        val sb = s.toByteArray()
        val body = byteArrayOf(2, rb.size.toByte()) + rb + byteArrayOf(2, sb.size.toByte()) + sb
        return byteArrayOf(0x30, body.size.toByte()) + body
    }
}
