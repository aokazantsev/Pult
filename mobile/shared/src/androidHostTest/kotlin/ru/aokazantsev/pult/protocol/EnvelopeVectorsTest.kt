package ru.aokazantsev.pult.protocol

import kotlin.test.Test
import kotlin.test.assertContentEquals
import kotlin.test.assertEquals
import kotlin.test.assertIs
import ru.aokazantsev.pult.crypto.JcaCrypto
import ru.aokazantsev.pult.vectors.Vectors
import ru.aokazantsev.pult.vectors.Vectors.list
import ru.aokazantsev.pult.vectors.Vectors.objectText
import ru.aokazantsev.pult.vectors.Vectors.str

class EnvelopeVectorsTest {

    private val vectors = Vectors.load("envelope")
    private val crypto = JcaCrypto()
    private val codec = EnvelopeCodec(crypto)
    private val pair = vectors.str("pair")
    private val keys = DirectionKeys(
        pcToPhone = Vectors.hex(vectors.str("key_pc_to_phone_hex")),
        phoneToPc = Vectors.hex(vectors.str("key_phone_to_pc_hex")),
    )

    @Test
    fun validEnvelopesAreOpenedByTheirRecipient() {
        vectors.list("valid").forEach { case ->
            val result = codec.open(case.objectText("envelope"), pair, ownSide = case.str("to"), keys = keys)
            val opened = assertIs<OpenResult.Opened>(result, case.str("from"))
            assertEquals(StrictJson.parseObject(case.str("plaintext")), opened.envelope.inner)
            assertEquals(case.str("from"), opened.envelope.from)
        }
    }

    @Test
    fun encryptionWithTheVectorNonceReproducesTheCiphertext() {
        vectors.list("valid").forEach { case ->
            val envelope = StrictJson.parseObject(case.objectText("envelope"))
            val aad = Wire.aad(
                "2", pair, case.str("from"), JsonFields.string(envelope, "id")!!,
                JsonFields.canonicalInteger(envelope["ts"])!!, JsonFields.canonicalInteger(envelope["ttl"])!!,
            )
            assertEquals(case.str("aad"), aad)
            val nonce = Vectors.hex(case.str("nonce_hex"))
            val sealed = crypto.aesGcmEncrypt(
                Vectors.hex(case.str("key_hex")), nonce, aad.encodeToByteArray(), case.str("plaintext").encodeToByteArray(),
            )
            assertContentEquals(Base64Url.decode(JsonFields.string(envelope, "ct")!!)!!, nonce + sealed)
        }
    }

    @Test
    fun sealedEnvelopeIsOpenedByTheOtherSideOnly() {
        val inner = StrictJson.parseObject("""{"type":"hello","protocol":[2],"capabilities":[]}""")
        val text = codec.seal(keys.phoneToPc, pair, Wire.FROM_PHONE, "ph-1", 1793989146, 0, inner)
        val opened = assertIs<OpenResult.Opened>(codec.open(text, pair, Wire.FROM_PC, keys))
        assertEquals(inner, opened.envelope.inner)
        assertIs<OpenResult.Invalid>(codec.open(text, pair, Wire.FROM_PHONE, keys))
    }

    @Test
    fun everyRejectCaseIsRejectedByItsRecipient() {
        vectors.list("reject").forEach { case ->
            val result = codec.open(case.objectText("envelope"), pair, case.str("to"), keys)
            assertEquals(false, result is OpenResult.Opened, case.str("case"))
        }
    }

    @Test
    fun rawTextRejectCasesAreRejected() {
        vectors.list("reject_text").forEach { case ->
            val result = codec.open(case.str("text"), pair, case.str("to"), keys)
            assertEquals(false, result is OpenResult.Opened, case.str("case"))
        }
    }

    @Test
    fun unsupportedVersionIsReportedAsIncompatible() {
        val case = vectors.list("reject").first { it.str("case") == "version 1" }
        assertIs<OpenResult.Incompatible>(codec.open(case.objectText("envelope"), pair, case.str("to"), keys))
    }
}
