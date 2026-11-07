package ru.aokazantsev.pult.crypto

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import android.util.Log
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec

class AndroidDeviceKeyStore : DeviceKeyStore {

    private val keyStore: KeyStore = KeyStore.getInstance(PROVIDER).apply { load(null) }

    override fun create(pair: String): CreatedKeys {
        delete(pair)
        val signAlias = signAlias(pair)
        val agreeAlias = agreeAlias(pair)
        val signSpec = KeyGenParameterSpec.Builder(signAlias, KeyProperties.PURPOSE_SIGN)
            .setAlgorithmParameterSpec(ECGenParameterSpec(CURVE))
            .setDigests(KeyProperties.DIGEST_SHA256)
            .build()
        val agreeSpec = KeyGenParameterSpec.Builder(agreeAlias, KeyProperties.PURPOSE_AGREE_KEY)
            .setAlgorithmParameterSpec(ECGenParameterSpec(CURVE))
            .build()
        val signPair = generate(signSpec)
        val agreePair = generate(agreeSpec)
        val signProtection = protection(signAlias, signPair.private)
        val agreeProtection = protection(agreeAlias, agreePair.private)
        if (signProtection == KeyProtection.Software || agreeProtection == KeyProtection.Software) {
            Log.w(TAG, "Ключи не в аппаратном хранилище: подпись=$signProtection, согласование=$agreeProtection")
        }
        val keys = JcaDeviceKeys(
            signPrivate = signPair.private,
            signPublic = EcCodec.publicToSec1(signPair.public as ECPublicKey),
            agreePrivate = agreePair.private,
            agreePublic = EcCodec.publicToSec1(agreePair.public as ECPublicKey),
            agreementProvider = PROVIDER,
        )
        return CreatedKeys(keys, signProtection, agreeProtection)
    }

    override fun open(pair: String): DeviceKeys? {
        val signPrivate = keyStore.getKey(signAlias(pair), null) as? PrivateKey ?: return null
        val agreePrivate = keyStore.getKey(agreeAlias(pair), null) as? PrivateKey ?: return null
        val signPublic = keyStore.getCertificate(signAlias(pair))?.publicKey as? ECPublicKey ?: return null
        val agreePublic = keyStore.getCertificate(agreeAlias(pair))?.publicKey as? ECPublicKey ?: return null
        return JcaDeviceKeys(
            signPrivate = signPrivate,
            signPublic = EcCodec.publicToSec1(signPublic),
            agreePrivate = agreePrivate,
            agreePublic = EcCodec.publicToSec1(agreePublic),
            agreementProvider = PROVIDER,
        )
    }

    override fun delete(pair: String) {
        listOf(signAlias(pair), agreeAlias(pair)).forEach { alias ->
            if (keyStore.containsAlias(alias)) keyStore.deleteEntry(alias)
        }
    }

    override fun deleteAllExcept(pair: String?) {
        val keep = pair?.let { setOf(signAlias(it), agreeAlias(it)) }.orEmpty()
        keyStore.aliases().toList().filter { it.startsWith(ALIAS_PREFIX) && it !in keep }.forEach(keyStore::deleteEntry)
    }

    private fun generate(spec: KeyGenParameterSpec) =
        KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, PROVIDER).run {
            initialize(spec)
            generateKeyPair()
        }

    private fun protection(alias: String, privateKey: PrivateKey): KeyProtection {
        val info = KeyFactory.getInstance(privateKey.algorithm, PROVIDER).getKeySpec(privateKey, KeyInfo::class.java)
        val level = info.securityLevel
        Log.i(TAG, "Ключ $alias: securityLevel=$level")
        return when (level) {
            KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT,
            KeyProperties.SECURITY_LEVEL_STRONGBOX,
            KeyProperties.SECURITY_LEVEL_UNKNOWN_SECURE -> KeyProtection.Hardware
            else -> KeyProtection.Software
        }
    }

    private fun signAlias(pair: String) = "${ALIAS_PREFIX}sign.$pair"

    private fun agreeAlias(pair: String) = "${ALIAS_PREFIX}agree.$pair"

    private companion object {
        const val PROVIDER = "AndroidKeyStore"
        const val CURVE = "secp256r1"
        const val ALIAS_PREFIX = "pult."
        const val TAG = "Pult"
    }
}
