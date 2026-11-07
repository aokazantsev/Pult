package ru.aokazantsev.pult.pairing

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.Inject
import dev.zacsweers.metro.SingleIn
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.serialization.json.JsonArray
import ru.aokazantsev.pult.crypto.DeviceKeyStore
import ru.aokazantsev.pult.crypto.PultCrypto
import ru.aokazantsev.pult.di.AppInfo
import ru.aokazantsev.pult.protocol.Base64Url
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.protocol.Wire
import ru.aokazantsev.pult.relay.RelayApi
import ru.aokazantsev.pult.relay.RelayException
import kotlin.time.Clock

@SingleIn(AppScope::class)
@Inject
class PairingController(
    private val api: RelayApi,
    private val store: PairStore,
    private val keyStore: DeviceKeyStore,
    private val crypto: PultCrypto,
    appInfo: AppInfo,
) {

    private val math = PairingMath(crypto)
    private val parser = QrParser(crypto, allowPlainHttp = appInfo.isDebug)

    private val mutableProgress = MutableStateFlow<PairingProgress>(PairingProgress.Idle)
    val progress: StateFlow<PairingProgress> = mutableProgress

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    fun submit(qrText: String) {
        scope.launch { pair(qrText) }
    }

    fun dismiss() {
        if (mutableProgress.value is PairingProgress.Failed) mutableProgress.value = PairingProgress.Idle
    }

    suspend fun pair(qrText: String) {
        if (mutableProgress.value is PairingProgress.Working) return
        if (store.pair.value != null) {
            fail("Пара уже есть. Сначала отвяжите её в настройках")
            return
        }
        var secret: ByteArray? = null
        var createdFor: String? = null
        try {
            working("Разбор QR")
            val payload = when (val parsed = parser.parse(qrText)) {
                is QrParseResult.Rejected -> return fail(parsed.reason)
                is QrParseResult.Parsed -> parsed.payload
            }
            secret = payload.secret
            ProtocolCompatibility.pcVersionProblem(payload.versions)?.let { return fail(it) }

            working("Проверка сервера")
            val serverProblem = checkServer(payload.server)
            if (serverProblem != null) return fail(serverProblem)

            working("Создание ключей в хранилище Android")
            val created = try {
                keyStore.create(payload.pair)
            } catch (failure: Exception) {
                return fail("Не удалось создать ключи в хранилище Android: ${failure.message ?: failure::class.simpleName}")
            }
            createdFor = payload.pair
            val phoneSign = Base64Url.encode(created.keys.signPublic)
            val phoneAgree = Base64Url.encode(created.keys.agreePublic)
            val transcript = Wire.transcript(
                payload.pair, payload.pcSign, payload.pcAgree, phoneSign, phoneAgree,
            )
            val tag = Base64Url.encode(math.claimTag(payload.secret, transcript))

            working("Отправка ключей на сервер")
            val body = "{\"phone_sign\":\"$phoneSign\",\"phone_agree\":\"$phoneAgree\",\"tag\":\"$tag\"}"
            val response = api.post("${payload.server}/v2/claims/${payload.pair}", body = body.encodeToByteArray())
            if (!response.isSuccess) return fail(claimFailure(RelayException(response.status, response.body)))

            store.save(
                StoredPair(
                    server = payload.server,
                    pair = payload.pair,
                    pcSign = payload.pcSign,
                    pcAgree = payload.pcAgree,
                    phoneSign = phoneSign,
                    phoneAgree = phoneAgree,
                    idleSeconds = payload.idleSeconds,
                    confirmed = false,
                    helloDeadlineEpochMs = Clock.System.now().toEpochMilliseconds() + HELLO_WAIT_MS,
                    softwareKeys = !created.isHardware,
                ),
            )
            createdFor = null
            mutableProgress.value = PairingProgress.Idle
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (failure: Exception) {
            fail("Нет связи с сервером: ${failure.message ?: failure::class.simpleName}")
        } finally {
            secret?.fill(0)
            createdFor?.let { runCatching { keyStore.delete(it) } }
        }
    }

    private suspend fun checkServer(server: String): String? {
        val response = try {
            api.get("$server/info")
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (failure: Exception) {
            return "Нет связи с сервером: ${failure.message ?: failure::class.simpleName}"
        }
        if (!response.isSuccess) return "Сервер не отвечает как сервер Pult (${response.status})"
        val info = StrictJson.parseObjectOrNull(response.body)
        if (info == null || JsonFields.string(info, "name") != "pult-relay") {
            return "Сервер не отвечает как сервер Pult"
        }
        val versions = (info["protocol"] as? JsonArray)?.map { JsonFields.integer(it) ?: return "Сервер Pult: версии протокола не разобрать" }
            ?: return "Сервер Pult: версии протокола не разобрать"
        return ProtocolCompatibility.serverVersionProblem(versions)
    }

    private fun claimFailure(failure: RelayException): String = when {
        failure.status == 404 -> "QR устарел или заявка удалена. Нужен новый QR на ПК"
        failure.code == "claim_used" -> "QR уже использован. Нужен новый QR на ПК"
        failure.status == 400 -> "Сервер отверг ключи телефона (${failure.code.orEmpty()})"
        failure.status == 429 -> "Слишком много попыток. Подождите и повторите"
        else -> "Сервер ответил ${failure.status}"
    }

    private fun working(step: String) {
        mutableProgress.value = PairingProgress.Working(step)
    }

    private fun fail(reason: String) {
        mutableProgress.value = PairingProgress.Failed(reason)
    }

    private companion object {
        const val HELLO_WAIT_MS = 120_000L
    }
}
