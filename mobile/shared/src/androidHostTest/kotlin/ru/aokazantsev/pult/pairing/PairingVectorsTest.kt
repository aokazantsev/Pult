package ru.aokazantsev.pult.pairing

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNotEquals
import kotlin.test.assertTrue
import ru.aokazantsev.pult.crypto.JcaCrypto
import ru.aokazantsev.pult.protocol.Base64Url
import ru.aokazantsev.pult.protocol.Wire
import ru.aokazantsev.pult.vectors.FixedKeys
import ru.aokazantsev.pult.vectors.Vectors
import ru.aokazantsev.pult.vectors.Vectors.list
import ru.aokazantsev.pult.vectors.Vectors.str

class PairingVectorsTest {

    private val vectors = Vectors.load("pairing")
    private val crypto = JcaCrypto()
    private val math = PairingMath(crypto)

    @Test
    fun keysFileMatchesPublicEncoding() {
        listOf("pc_sign", "pc_agree", "phone_sign", "phone_agree").forEach {
            assertEquals(FixedKeys.publicOf(it), Base64Url.encode(FixedKeys.sec1Of(it)))
        }
    }

    @Test
    fun qrIsParsed() {
        val parsed = assertIs<QrParseResult.Parsed>(QrParser(crypto, allowPlainHttp = false).parse(vectors.str("qr")))
        val payload = parsed.payload
        assertEquals(listOf(2L), payload.versions)
        assertEquals(vectors.str("server"), payload.server)
        assertEquals(vectors.str("pair"), payload.pair)
        assertEquals(vectors.str("pc_sign"), payload.pcSign)
        assertEquals(vectors.str("pc_agree"), payload.pcAgree)
        assertContentEquals(Base64Url.decode(vectors.str("secret")), payload.secret)
        assertEquals(14400, payload.idleSeconds)
    }

    @Test
    fun everyRejectedQrIsRejectedInReleaseMode() {
        val parser = QrParser(crypto, allowPlainHttp = false)
        vectors.list("qr_reject").forEach {
            assertIs<QrParseResult.Rejected>(parser.parse(it.str("qr")), it.str("case"))
        }
    }

    @Test
    fun plainHttpQrIsAcceptedOnlyInDebug() {
        val http = vectors.list("qr_reject").first { it.str("case") == "plain http server" }.str("qr")
        assertIs<QrParseResult.Parsed>(QrParser(crypto, allowPlainHttp = true).parse(http))
        assertIs<QrParseResult.Rejected>(QrParser(crypto, allowPlainHttp = false).parse(http))
    }

    @Test
    fun transcriptTagAndFingerprintMatch() {
        val transcript = Wire.transcript(
            vectors.str("pair"),
            vectors.str("pc_sign"),
            vectors.str("pc_agree"),
            vectors.str("phone_sign"),
            vectors.str("phone_agree"),
        )
        assertEquals(vectors.str("transcript"), transcript)
        val tag = math.claimTag(Base64Url.decode(vectors.str("secret"))!!, transcript)
        assertEquals(vectors.str("tag"), Base64Url.encode(tag))
        assertEquals(vectors.str("fingerprint"), math.fingerprint(transcript))
    }

    @Test
    fun wrongTagsDoNotMatchTheirTranscripts() {
        val secret = Base64Url.decode(vectors.str("secret"))!!
        vectors.list("reject").forEach {
            val expected = Base64Url.encode(math.claimTag(secret, it.str("transcript")))
            assertNotEquals(it.str("tag"), expected, it.str("case"))
        }
    }

    @Test
    fun sharedSecretAndDirectionKeysMatchOnBothSides() {
        val transcript = vectors.str("transcript")
        val phoneZ = FixedKeys.phone().agree(FixedKeys.sec1Of("pc_agree"))
        val pcZ = FixedKeys.pc().agree(FixedKeys.sec1Of("phone_agree"))
        assertEquals(vectors.str("shared_secret_hex"), phoneZ.toHex())
        assertContentEquals(phoneZ, pcZ)
        val keys = math.deriveKeys(phoneZ, transcript)
        assertEquals(vectors.str("key_pc_to_phone_hex"), keys.pcToPhone.toHex())
        assertEquals(vectors.str("key_phone_to_pc_hex"), keys.phoneToPc.toHex())
    }

    @Test
    fun pointValidationRejectsOffCurveAndMalformedPoints() {
        val good = FixedKeys.sec1Of("pc_sign")
        assertTrue(crypto.isValidPublicPoint(good))
        assertFalse(crypto.isValidPublicPoint(good.copyOf().also { it[64] = (it[64] + 1).toByte() }))
        assertFalse(crypto.isValidPublicPoint(good.copyOfRange(0, 64)))
        assertFalse(crypto.isValidPublicPoint(good.copyOf().also { it[0] = 2 }))
        assertFalse(crypto.isValidPublicPoint(ByteArray(65).also { it[0] = 4 }))
    }

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it) }
}
